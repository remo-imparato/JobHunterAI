using JobHunterAI.Application.AI;

namespace JobHunterAI.Automation;

public sealed class FormDetectionService
{
    public IReadOnlyList<DetectedField> Detect(IReadOnlyList<RawFormField> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        List<DetectedField> fields = [];
        foreach (RawFormField field in raw)
        {
            if (string.IsNullOrWhiteSpace(field.Selector) || field.Type is "hidden" or "submit" or "button" or "reset" or "image")
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label.Trim();
            fields.Add(new DetectedField(field.Selector, KindOf(field), field.Name ?? string.Empty, label, field.Required,
                field.MaxLength is > 0 ? field.MaxLength : null, field.Options ?? []));
        }

        return fields;
    }

    private static FieldKind KindOf(RawFormField field) => field.Tag.ToLowerInvariant() switch
    {
        "textarea" => FieldKind.TextArea,
        "select" => FieldKind.Select,
        "input" => field.Type.ToLowerInvariant() switch
        {
            "checkbox" => FieldKind.Checkbox,
            "radio" => FieldKind.Radio,
            "file" => FieldKind.File,
            "" or "text" or "email" or "tel" or "url" or "number" => FieldKind.Text,
            _ => FieldKind.Other
        },
        _ => FieldKind.Other
    };
}

/// <summary>Splits detected fields into identity fields filled from the profile and free-form questions for the AI.</summary>
public sealed class QuestionDetectionService
{
    public const string EmailKey = "email";
    public const string FullNameKey = "fullname";

    public static string? IdentityKeyOf(DetectedField field)
    {
        if (field.Kind != FieldKind.Text)
        {
            return null;
        }

        string text = (field.Label + " " + field.Name).ToLowerInvariant();
        if (text.Contains("email", StringComparison.Ordinal))
        {
            return EmailKey;
        }

        string label = field.Label.Trim().ToLowerInvariant();
        return label is "name" or "full name" or "your name" ? FullNameKey : null;
    }

    public IReadOnlyList<DetectedField> FindQuestionFields(IReadOnlyList<DetectedField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        List<DetectedField> questions = [];
        foreach (DetectedField field in fields)
        {
            if (field.Kind is FieldKind.Text or FieldKind.TextArea or FieldKind.Select &&
                IdentityKeyOf(field) is null && !string.IsNullOrWhiteSpace(field.Label))
            {
                questions.Add(field);
            }
        }

        return questions;
    }

    public static ApplicationQuestion ToQuestion(DetectedField field)
    {
        string text = field.Label;
        if (field.Kind == FieldKind.Select && field.Options.Count > 0)
        {
            text += " (choose exactly one of: " + string.Join(" | ", field.Options) + ")";
        }

        return new ApplicationQuestion(text, field.MaxLength);
    }
}

public sealed class ResumeUploadService(IAutomationAuditLog audit, Microsoft.Extensions.Options.IOptions<AutomationOptions> options)
{
    private static readonly string[] AllowedExtensions = [".pdf", ".docx"];

    public async Task<bool> UploadAsync(IApplicationPage page, IReadOnlyList<DetectedField> fields, string documentPath,
        string purpose, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(fields);
        Validate(documentPath);
        DetectedField? target = null;
        foreach (DetectedField field in fields)
        {
            if (field.Kind != FieldKind.File)
            {
                continue;
            }

            string text = (field.Label + " " + field.Name).ToLowerInvariant();
            bool isCover = text.Contains("cover", StringComparison.Ordinal);
            if (purpose == "cover" ? isCover : !isCover && (text.Contains("resume", StringComparison.Ordinal) ||
                text.Contains("cv", StringComparison.Ordinal) || target is null))
            {
                target = field;
                if (purpose == "cover" || text.Contains("resume", StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        if (target is null)
        {
            audit.Record("Upload", $"No {purpose} upload field found.");
            return false;
        }

        await page.UploadFileAsync(target.Selector, documentPath, cancellationToken);
        audit.Record("Upload", $"Uploaded {purpose} document '{Path.GetFileName(documentPath)}'.");
        return true;
    }

    private void Validate(string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        string extension = Path.GetExtension(documentPath).ToLowerInvariant();
        if (Array.IndexOf(AllowedExtensions, extension) < 0)
        {
            throw new ArgumentException("Only PDF and DOCX documents can be uploaded.", nameof(documentPath));
        }

        FileInfo info = new(documentPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The document to upload was not found.", documentPath);
        }

        if (info.Length > options.Value.MaxUploadBytes)
        {
            throw new ArgumentException("The document exceeds the maximum upload size.", nameof(documentPath));
        }
    }
}
