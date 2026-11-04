using System.Text;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Automation;

public sealed class AutomationAuditLog(ILogger<AutomationAuditLog> logger) : IAutomationAuditLog
{
    private readonly List<AuditEntry> _entries = [];
    private readonly object _gate = new();

    public IReadOnlyList<AuditEntry> Entries
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    public void Record(string step, string detail)
    {
        AuditEntry entry = new(DateTimeOffset.UtcNow, step, detail)
        {
            TraceId = Activity.Current?.TraceId.ToString(),
            CorrelationId = Activity.Current?.GetTagItem("jobhunter.correlation_id") as string
        };
        lock (_gate) { _entries.Add(entry); }
        // Details can contain employer field labels or filenames; keep them out of operational logs.
        logger.LogInformation("Automation audit {Step} at {AuditAt}, trace {TraceId}, correlation {CorrelationId}",
            step, entry.At, entry.TraceId, entry.CorrelationId);
    }
}

public sealed class FileScreenshotStore(IOptions<AutomationOptions> options) : IScreenshotStore
{
    public async Task<string> SaveAsync(string step, byte[] png, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png);
        StringBuilder safe = new();
        foreach (char c in step)
        {
            safe.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        Directory.CreateDirectory(options.Value.ScreenshotDirectory);
        string path = Path.Combine(options.Value.ScreenshotDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{safe}.png");
        await File.WriteAllBytesAsync(path, png, cancellationToken);
        return path;
    }
}

public static class AutomationGuard
{
    /// <summary>Only absolute http(s) URLs without embedded credentials may be automated.</summary>
    public static Uri RequireHttpUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("The application URL must be an absolute HTTP or HTTPS URL without credentials.", nameof(url));
        }

        return uri;
    }
}
