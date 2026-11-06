using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using JobHunterAI.Web;
using JobHunterAI.Web.Components.Dashboard;
using JobHunterAI.Web.Components.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;

namespace JobHunterAI.UnitTests;

public sealed class CandidateSelectionTests
{
    [Fact]
    public async Task SelectAndSwitch_UsesSelectedCandidateForSubsequentRequests()
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            foreach (CandidateDto candidate in harness.Factory.Candidates)
            {
                int page = await harness.Renderer.RenderAsync<Candidates>();
                MudButton[] buttons = harness.Renderer.Find<MudButton>(page)
                    .Where(button => button.Color == Color.Primary && button.ButtonType != ButtonType.Submit).ToArray();
                int index = Array.IndexOf(harness.Factory.Candidates, candidate);
                await buttons[index].OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
                Assert.Equal(candidate.Id, harness.Preferences.CandidateId);
                Assert.Equal(candidate, harness.Preferences.Candidate);
                await AssertRequestsAsync(harness, candidate.Id);
            }
        });
    }

    [Fact]
    public async Task Discovery_StartHistoryDetailsAndCandidateSwitchUseSelectedContext()
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            foreach (CandidateDto candidate in harness.Factory.Candidates)
            {
                harness.Preferences.SelectCandidate(candidate);
                int page = await harness.Renderer.RenderAsync<Discovery>();
                Assert.Equal($"/api/candidates/{candidate.Id}/discovery", harness.Factory.Requests[^1].AbsolutePath);
                MudButton start = Assert.Single(harness.Renderer.Find<MudButton>(page), x => x.Variant == Variant.Filled);
                await start.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
                Assert.Equal(candidate.Id, harness.Factory.DiscoveryCandidate);
                MudTable<DiscoveryRunSummary> table = Assert.Single(harness.Renderer.Find<MudTable<DiscoveryRunSummary>>(page));
                Assert.Single(table.Items!);
                MudButton refresh = harness.Renderer.Find<MudButton>(page).First(x => x.Href is null && x.Variant == Variant.Text);
                await refresh.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            }
        });
    }

    [Theory]
    [InlineData("https://example.invalid/job", true)]
    [InlineData("http://example.invalid/job", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:password@example.invalid/", false)]
    [InlineData("/relative", false)]
    public void Discovery_OnlyUsesAbsoluteCredentialFreeHttpLinks(string link, bool expected) => Assert.Equal(expected, Discovery.SafeLink(link));

    [Fact]
    public async Task Settings_ClearSelection_RemovesCandidateContext()
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            harness.Preferences.SelectCandidate(harness.Factory.Candidates[0]);
            int settings = await harness.Renderer.RenderAsync<Settings>();
            MudButton clear = Assert.Single(harness.Renderer.Find<MudButton>(settings)
                .Where(button => button.Href is null && button.Variant != Variant.Filled));
            await clear.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            Assert.Equal(Guid.Empty, harness.Preferences.CandidateId);
            Assert.Null(harness.Preferences.Candidate);
            Assert.Empty(harness.Factory.Requests);
        });
    }

    [Fact]
    public async Task Preferences_AreSharedWithinScopeAndIsolatedAcrossScopes()
    {
        ServiceCollection services = new();
        services.AddScoped<DashboardPreferences>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope first = provider.CreateAsyncScope();
        await using AsyncServiceScope second = provider.CreateAsyncScope();
        DashboardPreferences preferences = first.ServiceProvider.GetRequiredService<DashboardPreferences>();
        preferences.CandidateId = Guid.NewGuid();
        Assert.Same(preferences, first.ServiceProvider.GetRequiredService<DashboardPreferences>());
        Assert.Equal(Guid.Empty, second.ServiceProvider.GetRequiredService<DashboardPreferences>().CandidateId);
    }

    [Fact]
    public async Task Package_SelectSourceGenerateReviewAndDownloadUseCandidateContext()
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            harness.Preferences.SelectCandidate(harness.Factory.Candidates[0]);
            Guid job = Guid.NewGuid();
            int page = await harness.Renderer.RenderAsync<Package>(new Dictionary<string, object?> { [nameof(Package.JobId)] = job });
            MudSelect<Guid> select = Assert.Single(harness.Renderer.Find<MudSelect<Guid>>(page));
            await select.ValueChanged.InvokeAsync(harness.Factory.Source.Id);
            MudButton generate = Assert.Single(harness.Renderer.Find<MudButton>(page), x => x.Variant == Variant.Filled && x.Color == Color.Primary);
            await generate.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            MudButton download = Assert.Single(harness.Renderer.Find<MudButton>(page), x => x.Color == Color.Success);
            Assert.True(download.Disabled);
            MudCheckBox<bool> review = Assert.Single(harness.Renderer.Find<MudCheckBox<bool>>(page));
            await review.ValueChanged.InvokeAsync(true);
            Assert.False(download.Disabled);
            Assert.StartsWith($"/packages/{harness.Preferences.CandidateId}/", download.Href);
            Assert.EndsWith("/download", download.Href);
            Assert.Equal("AI is not configured. Set AI:Endpoint, AI:ApiKey and AI:DeploymentName on the API.", Package.FailureMessage("AiUnavailable"));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Package_ContextChangeDuringGenerationDiscardsOldResponse(bool changeJob)
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            harness.Preferences.SelectCandidate(harness.Factory.Candidates[0]);
            Guid job = Guid.NewGuid();
            int page = await harness.Renderer.RenderAsync<Package>(new Dictionary<string, object?> { [nameof(Package.JobId)] = job });
            var select = Assert.Single(harness.Renderer.Find<MudSelect<Guid>>(page));
            await select.ValueChanged.InvokeAsync(harness.Factory.Source.Id);
            TaskCompletionSource<HttpResponseMessage> delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Factory.Override = request =>
            {
                if (request.Method != HttpMethod.Post) return null;
                started.TrySetResult();
                return delayed.Task;
            };
            var generate = Assert.Single(harness.Renderer.Find<MudButton>(page), x => x.Variant == Variant.Filled && x.Color == Color.Primary);
            Task oldRequest = generate.OnClick.InvokeAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            await started.Task;
            if (changeJob)
            {
                await harness.Renderer.UpdateAsync(page, new Dictionary<string, object?> { [nameof(Package.JobId)] = Guid.NewGuid() });
                Assert.EndsWith("/resumes", harness.Factory.Requests[^1].AbsolutePath);
            }
            else
            {
                await select.ValueChanged.InvokeAsync(Guid.NewGuid());
                Assert.EndsWith("/packages/selection", harness.Factory.Requests[^1].AbsolutePath);
            }
            Assert.False(Assert.Single(harness.Renderer.Find<MudSelect<Guid>>(page)).Disabled);
            PackageView stale = new(Guid.NewGuid(), harness.Preferences.CandidateId, job, harness.Factory.Source.Id,
                harness.Factory.Source.SourceVersionId!.Value, PackageStatus.Completed, "Old company", "Old job", "Old snapshot",
                "https://example.invalid/job", "Old resume", "Old letter", null, null);
            delayed.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(stale) });
            await oldRequest;
            Assert.False(Assert.Single(harness.Renderer.Find<MudSelect<Guid>>(page)).Disabled);
            if (changeJob) Assert.DoesNotContain(harness.Renderer.Find<MudButton>(page), button => button.Color == Color.Success);
            else Assert.DoesNotContain(harness.Renderer.Find<MudButton>(page), button => button.Href?.Contains(stale.Id.ToString(), StringComparison.Ordinal) == true);
        });
    }

    private static async Task AssertRequestsAsync(Harness harness, Guid candidate)
    {
        await harness.Renderer.RenderAsync<DashboardOverview>();
        Assert.Equal($"?candidateId={candidate}", harness.Factory.Requests[^1].Query);
        Assert.Equal("/api/dashboard/summary", harness.Factory.Requests[^1].AbsolutePath);

        foreach (DashboardResource resource in Enum.GetValues<DashboardResource>())
        {
            int table = await harness.Renderer.RenderAsync<ResourceTable>(new Dictionary<string, object?>
            {
                [nameof(ResourceTable.Resource)] = resource
            });
            MudTable<DashboardRow> component = Assert.Single(harness.Renderer.Find<MudTable<DashboardRow>>(table));
            await component.ServerData!(new TableState { Page = 2, PageSize = 25 }, CancellationToken.None);
            Assert.Equal($"/api/dashboard/{resource}", harness.Factory.Requests[^1].AbsolutePath);
            Assert.Contains($"candidateId={candidate}&page=2&pageSize=25", harness.Factory.Requests[^1].Query);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        public RecordingFactory Factory { get; } = new();
        public TestRenderer Renderer { get; }
        public DashboardPreferences Preferences => _scope.ServiceProvider.GetRequiredService<DashboardPreferences>();

        public Harness()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddMudServices();
            services.AddSingleton<IJSRuntime, NoJavaScript>();
            services.AddSingleton<NavigationManager, TestNavigation>();
            services.AddSingleton<IHttpClientFactory>(Factory);
            services.AddScoped<DashboardPreferences>();
            services.AddScoped<DashboardClient>();
            services.AddScoped<CandidateClient>();
            services.AddScoped<OnboardingClient>();
            services.AddScoped<DiscoveryClient>();
            services.AddScoped<PackageClient>();
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            Renderer = new TestRenderer(_scope.ServiceProvider, _scope.ServiceProvider.GetRequiredService<ILoggerFactory>());
        }

        public async ValueTask DisposeAsync()
        {
            await Renderer.DisposeAsync();
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

#pragma warning disable BL0006 // Inspect compiled component bindings without adding a browser-test dependency.
    private sealed class TestRenderer(IServiceProvider services, ILoggerFactory loggerFactory) : Renderer(services, loggerFactory)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override RendererInfo RendererInfo { get; } = new("Server", true);
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component rendering failed.", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.FromCanceled(new CancellationToken(true));

        public async Task<int> RenderAsync<T>(Dictionary<string, object?>? parameters = null) where T : IComponent
        {
            int id = AssignRootComponentId(InstantiateComponent(typeof(T)));
            await RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters ?? []));
            return id;
        }

        public Task UpdateAsync(int componentId, Dictionary<string, object?> parameters) =>
            RenderRootComponentAsync(componentId, ParameterView.FromDictionary(parameters));

        public IEnumerable<T> Find<T>(int componentId) where T : IComponent
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            for (int i = 0; i < frames.Count; i++)
            {
                RenderTreeFrame frame = frames.Array[i];
                if (frame.FrameType != RenderTreeFrameType.Component) continue;
                if (frame.Component is T match) yield return match;
                foreach (T child in Find<T>(frame.ComponentId)) yield return child;
            }
        }

        private ulong EventId(int componentId, string name)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            for (int i = 0; i < frames.Count; i++)
            {
                RenderTreeFrame frame = frames.Array[i];
                if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == name && frame.AttributeEventHandlerId != 0)
                    return frame.AttributeEventHandlerId;
                if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    ulong child = EventId(frame.ComponentId, name);
                    if (child != 0) return child;
                }
            }
            return 0;
        }

        public Task EnterCandidateAsync(int settings, string value)
        {
            ulong id = EventId(settings, "oninput");
            Assert.True(id != 0, "No oninput event found. " + Describe(settings));
            return DispatchEventAsync(id, null, new ChangeEventArgs { Value = value });
        }

        private string Describe(int componentId)
        {
            var frames = GetCurrentRenderTreeFrames(componentId);
            return string.Join("; ", Enumerable.Range(0, frames.Count).Select(index => frames.Array[index])
                .Select(frame => frame.FrameType == RenderTreeFrameType.Component
                    ? frame.Component.GetType().Name + " [" + Describe(frame.ComponentId) + "]"
                    : frame.FrameType == RenderTreeFrameType.Attribute ? frame.AttributeName + "=" + frame.AttributeEventHandlerId : ""));
        }

        public Task ClickSaveAsync(int settings)
        {
            var frames = GetCurrentRenderTreeFrames(settings);
            int button = Enumerable.Range(0, frames.Count)
                .Select(index => frames.Array[index])
                .Single(frame => frame.FrameType == RenderTreeFrameType.Component && frame.Component is MudButton).ComponentId;
            ulong id = EventId(button, "onclick");
            Assert.NotEqual(0UL, id);
            return DispatchEventAsync(id, null, new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        }
    }
#pragma warning restore BL0006

    private sealed class RecordingFactory : IHttpClientFactory
    {
        public List<Uri> Requests { get; } = [];
        public Func<HttpRequestMessage, Task<HttpResponseMessage>?>? Override { get; set; }
        public Guid DiscoveryCandidate { get; set; }
        public DiscoveryRunSummary? DiscoveryRun { get; set; }
        public ResumeSummary Source { get; } = new(Guid.NewGuid(), "Uploaded", Guid.NewGuid(), "resume.docx", DateTimeOffset.UtcNow);
        public CandidateDto[] Candidates { get; } =
        [
            new(Guid.NewGuid(), "Same Name", "first", "first@example.invalid", "Engineer"),
            new(Guid.NewGuid(), "Same Name", "second", "second@example.invalid", "Architect")
        ];
        public HttpClient CreateClient(string name) => new(new Handler(this, Requests, Candidates)) { BaseAddress = new Uri("https://localhost/") };
    }

    private sealed class Handler(RecordingFactory factory, List<Uri> requests, CandidateDto[] candidates) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            Task<HttpResponseMessage>? overridden = factory.Override?.Invoke(request);
            if (overridden is not null) return overridden;
            if (request.RequestUri!.AbsolutePath.EndsWith("/resumes", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { factory.Source }) });
            if (request.RequestUri.AbsolutePath.Contains("/packages", StringComparison.Ordinal))
            {
                PackageView package = new(Guid.NewGuid(), Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[3]), Guid.NewGuid(), factory.Source.Id,
                    factory.Source.SourceVersionId!.Value, PackageStatus.Completed, "Acme", "Engineer", "Job snapshot", "https://example.invalid/job", "Resume", "Letter", null, null);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(package) });
            }
            if (request.RequestUri!.AbsolutePath.Contains("/discovery", StringComparison.Ordinal))
            {
                factory.DiscoveryCandidate = Guid.Parse(request.RequestUri.AbsolutePath.Split('/')[3]);
                if (request.Method == HttpMethod.Post)
                    factory.DiscoveryRun = new(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DiscoveryStatus.Completed, 0, 0, []);
                object body = request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath.EndsWith("/discovery", StringComparison.Ordinal)
                    ? (factory.DiscoveryRun is null ? Array.Empty<DiscoveryRunSummary>() : new[] { factory.DiscoveryRun })
                    : new DiscoveryRunDetails(factory.DiscoveryRun!, "{}", []);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
            }
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/candidates", StringComparison.Ordinal))
            {
                string path = request.RequestUri.AbsolutePath;
                object body = path == "/api/candidates" ? new CandidatePage(candidates, candidates.Length)
                    : candidates.Single(candidate => path.EndsWith(candidate.Id.ToString(), StringComparison.Ordinal));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
            }
            string json = request.RequestUri!.AbsolutePath.EndsWith("summary", StringComparison.Ordinal)
                ? "{\"jobs\":0,\"applications\":0,\"resumeVersions\":0,\"coverLetters\":0,\"runningExecutions\":0,\"applicationStatuses\":[]}"
                : "{\"items\":[],\"totalCount\":0}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://localhost/", "https://localhost/settings");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }
}
