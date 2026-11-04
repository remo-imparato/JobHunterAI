using System.Net;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Web;
using JobHunterAI.Web.Components.Dashboard;
using JobHunterAI.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;

namespace JobHunterAI.UnitTests;

public sealed class WebRenderingTests
{
    [Theory]
    [InlineData(typeof(Jobs), "Jobs")]
    [InlineData(typeof(Applications), "Applications")]
    [InlineData(typeof(ResumeVersions), "Resume Versions")]
    [InlineData(typeof(CoverLetters), "Cover Letters")]
    [InlineData(typeof(AutomationQueue), "Automation Queue")]
    [InlineData(typeof(Analytics), "Analytics")]
    [InlineData(typeof(Home), "Dashboard")]
    [InlineData(typeof(Settings), "Settings")]
    public async Task Pages_RenderTitlesAndSafeUnconfiguredState(Type component, string title)
    {
        string html = await RenderAsync(component, false);
        Assert.Contains(title, html);
        if (component != typeof(Settings)) Assert.Contains("Settings", html);
        else Assert.Contains("not authentication", html);
    }

    [Fact]
    public async Task Overview_RendersPersistedCountsAndPipeline()
    {
        string html = await RenderAsync(typeof(DashboardOverview), true);
        Assert.Contains("Application pipeline", html);
        Assert.Contains("Draft", html);
        Assert.Contains("Discovered jobs", html);
        Assert.DoesNotContain("Unable to load", html);
    }

    [Fact]
    public async Task Overview_ShowsRecoverableErrorOnApiFailure()
    {
        string html = await RenderAsync(typeof(DashboardOverview), true, HttpStatusCode.ServiceUnavailable);
        Assert.Contains("Unable to load summary", html);
    }

    [Fact]
    public async Task Table_RendersSearchAndFilterControls()
    {
        string html = await RenderAsync(typeof(ResourceTable), true, parameters: new Dictionary<string, object?>
        {
            [nameof(ResourceTable.Resource)] = DashboardResource.Jobs,
            [nameof(ResourceTable.Filters)] = new[] { "Lever" }
        });
        Assert.Contains("Search", html);
        Assert.Contains("Filter", html);
        Assert.Contains("Title", html);
    }

    private static async Task<string> RenderAsync(Type component, bool configured, HttpStatusCode status = HttpStatusCode.OK,
        Dictionary<string, object?>? parameters = null)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddMudServices();
        services.AddSingleton<IJSRuntime, NoJavaScript>();
        services.AddSingleton<NavigationManager, TestNavigation>();
        services.AddSingleton(new DashboardPreferences { CandidateId = configured ? Guid.NewGuid() : Guid.Empty });
        services.AddSingleton<IHttpClientFactory>(new Factory(status));
        services.AddSingleton<DashboardClient>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync(component,
                ParameterView.FromDictionary(parameters ?? new Dictionary<string, object?>()));
            return output.ToHtmlString();
        });
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
    private sealed class Factory(HttpStatusCode status) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(status)) { BaseAddress = new Uri("https://localhost/") };
    }
    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string json = request.RequestUri!.AbsolutePath.EndsWith("summary", StringComparison.Ordinal)
                ? "{\"jobs\":3,\"applications\":1,\"resumeVersions\":2,\"coverLetters\":1,\"runningExecutions\":0,\"applicationStatuses\":[{\"label\":\"Draft\",\"count\":1}]}"
                : "{\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"<script>unsafe</script>\",\"detail\":\"Lever\",\"status\":\"Remote\",\"date\":null}],\"totalCount\":1}";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
