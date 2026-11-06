using System.Net;
using JobHunterAI.AI;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Infrastructure.JobProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class ExternalServiceBoundaryTests
{
    [Theory]
    [InlineData("Greenhouse")]
    [InlineData("Lever")]
    [InlineData("Ashby")]
    public async Task Providers_PropagateCancellationAndRejectMissingCompany(string name)
    {
        using HttpClient client = new(new CancelHandler()) { BaseAddress = new Uri("https://example.com/") };
        IJobProvider provider = name switch
        {
            "Greenhouse" => new GreenhouseProvider(client),
            "Lever" => new LeverProvider(client),
            _ => new AshbyProvider(client)
        };
        await Assert.ThrowsAsync<ArgumentException>(() => provider.SearchJobsAsync("acme", Guid.Empty));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.SearchJobsAsync("acme", Guid.NewGuid(), cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("Greenhouse", "{\"jobs\":[]}")]
    [InlineData("Lever", "[]")]
    [InlineData("Ashby", "{\"jobs\":[]}")]
    public async Task Providers_EmptyFeedsReturnNoFabricatedJobs(string name, string json)
    {
        using HttpClient client = new(new JsonHandler(json)) { BaseAddress = new Uri("https://example.com/") };
        IJobProvider provider = name switch
        {
            "Greenhouse" => new GreenhouseProvider(client),
            "Lever" => new LeverProvider(client),
            _ => new AshbyProvider(client)
        };
        Assert.Empty(await provider.SearchJobsAsync("acme", Guid.NewGuid()));
    }

    [Fact]
    public async Task Ai_CancellationIsNeverRetried()
    {
        Completion completion = new() { Fail = true };
        StructuredAiClient client = Client(completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RequestAsync<object>("JobMatching", "system", "candidate",
            value => value, CancellationToken.None));
        Assert.Equal(1, completion.Calls);
    }

    [Fact]
    public async Task Ai_NullJsonRetriesAndFailsWithTypedError()
    {
        Completion completion = new();
        await Assert.ThrowsAsync<AiResponseException>(() => Client(completion).RequestAsync<object>("JobMatching", "system", "candidate",
            value => value, CancellationToken.None));
        Assert.Equal(2, completion.Calls);
    }

    private static StructuredAiClient Client(Completion completion) => new(completion,
        Options.Create(new AiOptions { MaxAttempts = 2, RetryBaseDelay = TimeSpan.Zero }), NullLogger<StructuredAiClient>.Instance);

    private sealed class Completion : IAiTextCompletion
    {
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? Task.FromException<string>(new OperationCanceledException()) : Task.FromResult("null");
        }
    }
    private sealed class CancelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}
