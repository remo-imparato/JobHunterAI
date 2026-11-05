using System.Net;
using JobHunterAI.Application.Dashboard;
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
    public async Task Save_WithoutBlur_UsesSelectedCandidateForSubsequentRequests()
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            int settings = await harness.Renderer.RenderAsync<Settings>();
            Guid first = Guid.Parse("d9e89b8f-bea9-4b17-8d97-453b9611d74f");
            await harness.Renderer.EnterCandidateAsync(settings, first.ToString());
            await harness.Renderer.ClickSaveAsync(settings);
            Assert.Equal(first, harness.Preferences.CandidateId);
            await AssertRequestsAsync(harness, first);

            Guid second = Guid.NewGuid();
            await harness.Renderer.EnterCandidateAsync(settings, second.ToString());
            await harness.Renderer.ClickSaveAsync(settings);
            Assert.Equal(second, harness.Preferences.CandidateId);
            await AssertRequestsAsync(harness, second);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Save_InvalidCandidate_PreservesExistingSelection(string input)
    {
        await using Harness harness = new();
        await harness.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            Guid existing = Guid.NewGuid();
            harness.Preferences.CandidateId = existing;
            int settings = await harness.Renderer.RenderAsync<Settings>();
            await harness.Renderer.EnterCandidateAsync(settings, input);
            await harness.Renderer.ClickSaveAsync(settings);
            Assert.Equal(existing, harness.Preferences.CandidateId);
            Assert.Contains(harness.Renderer.Find<MudAlert>(settings), alert => alert.Severity == Severity.Error);
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
        public HttpClient CreateClient(string name) => new(new Handler(Requests)) { BaseAddress = new Uri("https://localhost/") };
    }

    private sealed class Handler(List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
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
