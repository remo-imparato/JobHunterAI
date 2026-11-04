using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Automation;

public sealed class AutomationAuditLog(ILogger<AutomationAuditLog> logger) : IAutomationAuditLog
{
    private readonly List<AuditEntry> _entries = [];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public void Record(string step, string detail)
    {
        _entries.Add(new AuditEntry(DateTimeOffset.UtcNow, step, detail));
        logger.LogInformation("Automation {Step}: {Detail}", step, detail);
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
