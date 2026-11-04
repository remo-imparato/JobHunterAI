using JobHunterAI.Domain;

namespace JobHunterAI.Automation;

/// <summary>A prepared application page awaiting user review. It never submits by itself.</summary>
public sealed class ApplicationSession(
    IApplicationPage page,
    IReadOnlyList<DetectedField> fields,
    IReadOnlyList<DetectedField> questionFields,
    IReadOnlyList<string> screenshots) : IAsyncDisposable
{
    public IApplicationPage Page { get; } = page;
    public IReadOnlyList<DetectedField> Fields { get; } = fields;
    public IReadOnlyList<DetectedField> QuestionFields { get; } = questionFields;
    public IReadOnlyList<string> Screenshots { get; } = screenshots;

    public ValueTask DisposeAsync() => Page.DisposeAsync();
}

public sealed record ApplicationDocuments(string? ResumePath, string? CoverLetterPath);

/// <summary>Loads an application page, detects fields, fills identity fields, uploads documents and stops for review.</summary>
public sealed class ApplicationWorkflow(
    IBrowserService browser,
    FormDetectionService formDetection,
    QuestionDetectionService questionDetection,
    ResumeUploadService uploads,
    IScreenshotStore screenshots,
    IAutomationAuditLog audit)
{
    public async Task<ApplicationSession> PrepareAsync(
        JobPosting job, CandidateProfile candidate, ApplicationDocuments documents, CancellationToken cancellationToken = default)
        => await PrepareForReviewAsync(job, candidate, documents, null, cancellationToken);

    internal async Task<ApplicationSession> PrepareForReviewAsync(
        JobPosting job, CandidateProfile candidate, ApplicationDocuments documents,
        Func<IApplicationPage, IReadOnlyList<DetectedField>, CancellationToken, Task>? suggestAnswers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(documents);
        Uri url = AutomationGuard.RequireHttpUrl(job.ApplicationUrl);
        List<string> shots = [];
        IApplicationPage page = await browser.OpenPageAsync(cancellationToken);
        try
        {
            audit.Record("Load", $"Opening application page for job {job.Id} at {url.Host}.");
            await page.NavigateAsync(url, cancellationToken);
            shots.Add(await screenshots.SaveAsync("loaded", await page.ScreenshotAsync(cancellationToken), cancellationToken));

            IReadOnlyList<DetectedField> fields = formDetection.Detect(await page.ReadFormFieldsAsync(cancellationToken));
            audit.Record("Detect", $"Detected {fields.Count} form fields.");

            foreach (DetectedField field in fields)
            {
                string? key = QuestionDetectionService.IdentityKeyOf(field);
                string? value = key switch
                {
                    QuestionDetectionService.EmailKey => candidate.Email.Value,
                    QuestionDetectionService.FullNameKey => candidate.FullName,
                    _ => null
                };
                if (value is not null)
                {
                    await page.FillTextAsync(field.Selector, value, cancellationToken);
                    audit.Record("Fill", $"Filled identity field '{field.Label}'.");
                }
            }

            IReadOnlyList<DetectedField> questions = questionDetection.FindQuestionFields(fields);
            if (suggestAnswers is not null)
            {
                await suggestAnswers(page, questions, cancellationToken);
            }

            if (documents.ResumePath is not null)
            {
                await uploads.UploadAsync(page, fields, documents.ResumePath, "resume", cancellationToken);
            }

            if (documents.CoverLetterPath is not null)
            {
                await uploads.UploadAsync(page, fields, documents.CoverLetterPath, "cover", cancellationToken);
            }

            shots.Add(await screenshots.SaveAsync("prepared", await page.ScreenshotAsync(cancellationToken), cancellationToken));
            audit.Record("Review", $"Prepared page with {questions.Count} questions awaiting user review. Nothing was submitted.");
            return new ApplicationSession(page, fields, questions, shots);
        }
        catch
        {
            audit.Record("Failed", "Preparing the application page failed.");
            await page.DisposeAsync();
            throw;
        }
    }
}
