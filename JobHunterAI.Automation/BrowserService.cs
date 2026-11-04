using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace JobHunterAI.Automation;

/// <summary>Owns the Playwright runtime and browser; one browser is shared and each page gets an isolated context.</summary>
public sealed class BrowserService(IOptions<AutomationOptions> options) : IBrowserService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<IApplicationPage> OpenPageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_browser is null)
            {
                _playwright = await Playwright.CreateAsync();
                _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = options.Value.Headless });
            }
        }
        finally
        {
            _gate.Release();
        }

        IBrowserContext context = await _browser.NewContextAsync(new BrowserNewContextOptions { AcceptDownloads = false });
        IPage page = await context.NewPageAsync();
        page.SetDefaultTimeout((float)options.Value.NavigationTimeout.TotalMilliseconds);
        return new PlaywrightApplicationPage(context, page);
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _gate.Dispose();
    }
}

/// <summary>Page object over a Playwright page. Controls are addressed by a generated data attribute, never raw user text.</summary>
internal sealed class PlaywrightApplicationPage(IBrowserContext context, IPage page) : IApplicationPage
{
    private const string Attribute = "data-jh-id";

    private const string ReadFieldsScript = """
        () => {
          const result = [];
          let counter = 0;
          for (const el of document.querySelectorAll('input, textarea, select')) {
            if (el.disabled) continue;
            const id = 'f' + (counter++);
            el.setAttribute('data-jh-id', id);
            let label = '';
            if (el.labels && el.labels.length > 0) label = el.labels[0].innerText;
            if (!label) label = el.getAttribute('aria-label') || el.getAttribute('placeholder') || '';
            result.push({
              selector: '[data-jh-id="' + id + '"]',
              tag: el.tagName.toLowerCase(),
              type: (el.getAttribute('type') || '').toLowerCase(),
              name: el.getAttribute('name') || '',
              label: label.replace(/\s+/g, ' ').trim(),
              required: el.required || el.getAttribute('aria-required') === 'true',
              maxLength: el.maxLength > 0 ? el.maxLength : null,
              options: el.tagName === 'SELECT' ? Array.from(el.options).map(o => o.text.trim()).filter(t => t) : []
            });
          }
          return result;
        }
        """;

    public string CurrentUrl => page.Url;

    public async Task NavigateAsync(Uri url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await page.GotoAsync(url.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    }

    public async Task<IReadOnlyList<RawFormField>> ReadFormFieldsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await page.EvaluateAsync<List<RawFormField>>(ReadFieldsScript) ?? [];
    }

    public Task FillTextAsync(string selector, string value, CancellationToken cancellationToken = default) =>
        page.FillAsync(Guard(selector), value);

    public Task SelectOptionAsync(string selector, string option, CancellationToken cancellationToken = default) =>
        page.SelectOptionAsync(Guard(selector), new SelectOptionValue { Label = option });

    public Task SetCheckedAsync(string selector, bool isChecked, CancellationToken cancellationToken = default) =>
        page.SetCheckedAsync(Guard(selector), isChecked);

    public Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default) =>
        page.SetInputFilesAsync(Guard(selector), filePath);

    public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default) =>
        page.ScreenshotAsync(new PageScreenshotOptions { FullPage = true });

    public Task ClickSubmitAsync(CancellationToken cancellationToken = default) =>
        page.ClickAsync("button[type=submit], input[type=submit]");

    public async ValueTask DisposeAsync() => await context.CloseAsync();

    private static string Guard(string selector) =>
        selector.StartsWith($"[{Attribute}=\"", StringComparison.Ordinal)
            ? selector : throw new ArgumentException("Only detected form controls can be addressed.", nameof(selector));
}
