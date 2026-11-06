using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Web;
using JobHunterAI.Web.Components;
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
        if (component != typeof(Settings)) Assert.Contains("Select a candidate", html);
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

    [Fact]
    public async Task CandidateEntry_RendersSearchAndDistinctUsernamesAndEncodesNames()
    {
        string html = await RenderAsync(typeof(Candidates), false);
        Assert.Contains("Full name or username", html);
        Assert.Contains("first-user", html);
        Assert.Contains("second-user", html);
        Assert.Contains("Create candidate", html);
        Assert.Contains("not authentication", html);
        Assert.DoesNotContain("<script>unsafe</script>", html);
    }

    [Fact]
    public async Task CandidateEditor_RendersCreateAndExistingProfile()
    {
        string create = await RenderAsync(typeof(CandidateEdit), false);
        Assert.Contains("Create and select candidate", create);
        Assert.Contains("Username", create);
        Assert.DoesNotContain("Delete candidate", create);
        string edit = await RenderAsync(typeof(CandidateEdit), false, parameters: new()
        {
            [nameof(CandidateEdit.Id)] = Guid.Parse("11111111-1111-1111-1111-111111111111")
        });
        Assert.Contains("first-user", edit);
        Assert.Contains("Delete candidate", edit);
        Assert.Contains("cannot be deleted", edit);
    }

    [Theory]
    [InlineData(typeof(Candidates))]
    [InlineData(typeof(CandidateEdit))]
    public async Task CandidatePages_ShowRecoverableApiErrors(Type component)
    {
        string html = await RenderAsync(component, false, HttpStatusCode.ServiceUnavailable,
            component == typeof(CandidateEdit) ? new() { [nameof(CandidateEdit.Id)] = Guid.NewGuid() } : null);
        Assert.Contains("migrations are applied", html);
    }

    [Fact]
    public async Task CandidateGate_DoesNotRenderDashboardWithoutSelection()
    {
        string html = await RenderAsync(typeof(CandidateRouteView), false, parameters: new()
        {
            [nameof(CandidateRouteView.RouteData)] = new RouteData(typeof(Home), new Dictionary<string, object?>())
        });
        Assert.Contains("Select a candidate to continue", html);
        Assert.DoesNotContain("Applications are never submitted automatically", html);
        Assert.True(CandidateRouteView.AllowsUnselectedCandidate(typeof(Candidates)));
        Assert.True(CandidateRouteView.AllowsUnselectedCandidate(typeof(CandidateEdit)));
        Assert.True(CandidateRouteView.AllowsUnselectedCandidate(typeof(Error)));
        Assert.True(CandidateRouteView.AllowsUnselectedCandidate(typeof(NotFound)));
        Assert.False(CandidateRouteView.AllowsUnselectedCandidate(typeof(Home)));
        Assert.False(CandidateRouteView.AllowsUnselectedCandidate(typeof(Settings)));
    }

    [Theory]
    [InlineData(typeof(ResumeUpload), "Source resumes", "Original file bytes are not retained")]
    [InlineData(typeof(SearchCriteria), "Search criteria", "unknown work arrangement")]
    public async Task Phase2Pages_RenderCandidateOnboardingControls(Type component, string title, string detail)
    {
        string html = await RenderAsync(component, true);
        Assert.Contains(title, html);
        Assert.Contains(detail, html);
    }

    [Theory]
    [InlineData(typeof(ResumeUpload))]
    [InlineData(typeof(SearchCriteria))]
    public async Task Phase2Pages_ShowRecoverableApiErrors(Type component)
    {
        string html = await RenderAsync(component, true, HttpStatusCode.ServiceUnavailable);
        Assert.Contains(component == typeof(ResumeUpload) ? "onboarding service is unavailable" : "Unable to", html);
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
        services.AddSingleton<CandidateClient>();
        services.AddSingleton<OnboardingClient>();
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
            if (request.RequestUri!.AbsolutePath.EndsWith("/resumes", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(Array.Empty<JobHunterAI.Application.Resumes.ResumeSummary>()) });
            if (request.RequestUri.AbsolutePath.EndsWith("/search-criteria", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(new SearchCriteriaInput(["Engineer"], [], JobHunterAI.Domain.WorkArrangement.Remote, true, [], [])) });
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/candidates", StringComparison.Ordinal))
            {
                CandidateDto first = new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "<script>unsafe</script>", "first-user", "first@example.invalid", "Engineer");
                CandidateDto second = first with { Id = Guid.NewGuid(), Username = "second-user" };
                object body = request.RequestUri.AbsolutePath == "/api/candidates" ? new CandidatePage([first, second], 2) : first;
                return Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(body) });
            }
            string json = request.RequestUri!.AbsolutePath.EndsWith("summary", StringComparison.Ordinal)
                ? "{\"jobs\":3,\"applications\":1,\"resumeVersions\":2,\"coverLetters\":1,\"runningExecutions\":0,\"applicationStatuses\":[{\"label\":\"Draft\",\"count\":1}]}"
                : "{\"items\":[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"title\":\"<script>unsafe</script>\",\"detail\":\"Lever\",\"status\":\"Remote\",\"date\":null}],\"totalCount\":1}";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }
}
