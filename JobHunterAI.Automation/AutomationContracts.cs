namespace JobHunterAI.Automation;

public sealed class AutomationOptions
{
    public const string SectionName = "Automation";

    public bool Headless { get; set; } = true;
    public string ScreenshotDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "automation-screenshots");
    public TimeSpan NavigationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public long MaxUploadBytes { get; set; } = 5 * 1024 * 1024;
}

public enum FieldKind
{
    Text,
    TextArea,
    Select,
    Checkbox,
    Radio,
    File,
    Other
}

/// <summary>A form control as read from the page. Selector targets the element via a stable data attribute.</summary>
public sealed class RawFormField
{
    public string Selector { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public int? MaxLength { get; set; }
    public List<string> Options { get; set; } = [];
}

public sealed record DetectedField(
    string Selector,
    FieldKind Kind,
    string Name,
    string Label,
    bool Required,
    int? MaxLength,
    IReadOnlyList<string> Options);

public sealed record AuditEntry(DateTimeOffset At, string Step, string Detail)
{
    public string? TraceId { get; init; }
    public string? CorrelationId { get; init; }
}

public interface IAutomationAuditLog
{
    void Record(string step, string detail);

    IReadOnlyList<AuditEntry> Entries { get; }
}

public interface IScreenshotStore
{
    Task<string> SaveAsync(string step, byte[] png, CancellationToken cancellationToken = default);
}

/// <summary>Browser abstraction so workflows and detection can be tested without a real browser.</summary>
public interface IApplicationPage : IAsyncDisposable
{
    string CurrentUrl { get; }

    Task NavigateAsync(Uri url, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RawFormField>> ReadFormFieldsAsync(CancellationToken cancellationToken = default);

    Task FillTextAsync(string selector, string value, CancellationToken cancellationToken = default);

    Task SelectOptionAsync(string selector, string option, CancellationToken cancellationToken = default);

    Task SetCheckedAsync(string selector, bool isChecked, CancellationToken cancellationToken = default);

    Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default);

    Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default);

    Task ClickSubmitAsync(CancellationToken cancellationToken = default);
}

public interface IBrowserService
{
    Task<IApplicationPage> OpenPageAsync(CancellationToken cancellationToken = default);
}
