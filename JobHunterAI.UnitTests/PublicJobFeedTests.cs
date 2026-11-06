using System.Net;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure.JobProviders;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class PublicJobFeedTests
{
    [Fact]
    public async Task Arbeitnow_MapsRealEmployerAndUsesBoundedFixedPagination()
    {
        List<string> requests = [];
        using HttpClient client = Client(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            Assert.Contains("JobHunterAI", request.Headers.UserAgent.ToString());
            return """
                {"data":[{"slug":"engineer-1","company_name":"Real Employer","title":"Engineer","description":"<p>Build &amp; ship</p><script>hidden</script>","location":"Berlin","remote":true,"url":"https://example.invalid/job","created_at":1735776000}],"links":{"next":"http://untrusted.invalid/steal"}}
                """;
        });
        ArbeitnowFeed feed = new(client, Options.Create(new PublicFeedOptions { MaxPages = 2 }));

        PublicJobFeedResult result = await feed.SearchAsync(10, default);
        PublicJobListing found = Assert.Single(result.Jobs);

        Assert.Equal("Real Employer", found.CompanyName);
        Assert.Equal(PublicFeedMapping.CompanyId("Arbeitnow", "real employer"), found.Job.CompanyId);
        Assert.Equal("Build & ship", found.Job.Description);
        Assert.Equal(WorkArrangement.Remote, found.Job.Location.Arrangement);
        Assert.Null(found.Job.Location.Country);
        Assert.Equal("public:engineer-1", found.Job.ExternalId);
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), found.Job.PostedAt);
        Assert.Equal(new[] { "/job-board-api?page=1", "/job-board-api?page=2" }, requests);
        Assert.Contains(result.Warnings, warning => warning.Contains("limit"));
    }

    [Fact]
    public async Task Arbeitnow_ContinuesToNextPageAndDoesNotWarnForExactlyExhaustedFeed()
    {
        int calls = 0;
        using HttpClient client = Client(request =>
        {
            calls++;
            bool first = request.RequestUri!.Query == "?page=1";
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                data = new[] { new { slug = first ? "one" : "two", company_name = "Employer", title = "Engineer", description = "Build", location = "Berlin", remote = true, url = "https://example.invalid/job", created_at = 1735776000 } },
                links = new { next = first ? "https://www.arbeitnow.com/api/job-board-api?page=2" : null }
            });
        });

        PublicJobFeedResult result = await new ArbeitnowFeed(client, Options.Create(new PublicFeedOptions())).SearchAsync(2, default);

        Assert.Equal(2, calls);
        Assert.Equal(2, result.Jobs.Count);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RemoteOk_SkipsLegalMetadataAndInvalidRecordsAndKeepsAttributionUrl()
    {
        using HttpClient client = Client(_ => """
            [{"legal":"Source attribution required"},null,{"id":"broken"},
             {"id":123,"company":"Employer","position":"Engineer","description":"<p>Build systems</p>","location":"Worldwide","url":"https://remoteok.com/remote-jobs/123","date":"2025-01-02T00:00:00Z"}]
            """);
        RemoteOkFeed feed = new(client, Options.Create(new PublicFeedOptions()));

        PublicJobFeedResult result = await feed.SearchAsync(10, default);
        PublicJobListing found = Assert.Single(result.Jobs);

        Assert.Equal("https://remoteok.com/remote-jobs/123", found.Job.ApplicationUrl);
        Assert.Equal("RemoteOK", found.Job.ProviderName);
        Assert.True(found.Job.Location.IsRemote);
        Assert.Contains(result.Warnings, warning => warning.Contains("2 invalid"));
    }

    [Fact]
    public async Task Feeds_DisabledFeedsMakeNoRequestsAndRejectUnboundedLimits()
    {
        using HttpClient client = Client(_ => throw new InvalidOperationException("Must not contact source"));
        ArbeitnowFeed feed = new(client, Options.Create(new PublicFeedOptions { Arbeitnow = false }));
        Assert.Empty((await feed.SearchAsync(100, default)).Jobs);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => feed.SearchAsync(501, default));
        Assert.False(new PublicFeedOptions { MaxPages = 11 }.IsValid());
        Assert.False(new PublicFeedOptions { RequestTimeoutSeconds = 0 }.IsValid());
    }

    [Fact]
    public async Task Feeds_PropagateHttpFailuresAndRejectUnexpectedPayloads()
    {
        using HttpClient client = Client(_ => "{}");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() =>
            new ArbeitnowFeed(client, Options.Create(new PublicFeedOptions())).SearchAsync(10, default));
        using HttpClient unavailable = new(new Handler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)))
            { BaseAddress = new Uri("https://example.invalid/") };
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new RemoteOkFeed(unavailable, Options.Create(new PublicFeedOptions())).SearchAsync(10, default));
    }

    private static HttpClient Client(Func<HttpRequestMessage, string> payload) => new(new Handler(request =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload(request), System.Text.Encoding.UTF8, "application/json") }))
        { BaseAddress = new Uri("https://example.invalid/") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
