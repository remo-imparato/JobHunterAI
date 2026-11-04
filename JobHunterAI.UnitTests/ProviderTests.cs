using System.Net;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure.JobProviders;

namespace JobHunterAI.UnitTests;

public sealed class ProviderTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Greenhouse_SearchAndDetails_NormalizeContentAndUseBoardScopedIdentity()
    {
        const string payload = """
            {"id":123,"title":"Engineering Manager","content":"<p>Lead &amp; mentor</p><script>hidden</script>","absolute_url":"https://boards.greenhouse.io/acme/jobs/123","location":{"name":"Boston, USA"},"first_published":"2025-01-02T12:00:00Z","updated_at":"2025-02-02T12:00:00Z"}
            """;
        using HttpClient client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/jobs/123")
            ? payload : "{\"jobs\":[" + payload + "]}");
        IJobProvider provider = new GreenhouseProvider(client);

        JobPosting found = Assert.Single(await provider.SearchJobsAsync("acme", CompanyId));
        JobPosting? details = await provider.GetJobDetailsAsync("acme", "123", CompanyId);

        Assert.NotNull(details);
        Assert.Equal(found.Id, details.Id);
        Assert.Equal("acme:123", found.ExternalId);
        Assert.Equal("Lead & mentor", found.Description);
        Assert.Equal("Boston", found.Location.City);
        Assert.Equal("USA", found.Location.Country);
        Assert.Equal("Greenhouse", found.ProviderName);
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 12, 0, 0, TimeSpan.Zero), found.PostedAt);
    }

    [Fact]
    public async Task Lever_SearchAndDetails_NormalizeRemoteAndUnixTimestamp()
    {
        const string payload = """
            {"id":"abc-123","text":"Director","descriptionPlain":"Lead teams","hostedUrl":"https://jobs.lever.co/acme/abc-123","categories":{"location":"New York, USA"},"workplaceType":"remote","createdAt":1735776000000}
            """;
        using HttpClient client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("/abc-123")
            ? payload : "[" + payload + "]");
        IJobProvider provider = new LeverProvider(client);

        JobPosting found = Assert.Single(await provider.SearchJobsAsync("acme", CompanyId));
        JobPosting? details = await provider.GetJobDetailsAsync("acme", "abc-123", CompanyId);

        Assert.NotNull(details);
        Assert.Equal(found.Id, details.Id);
        Assert.True(found.Location.IsRemote);
        Assert.Equal("New York", found.Location.City);
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero), found.PostedAt);
        Assert.Equal("Lever", found.ProviderName);
    }

    [Fact]
    public async Task Ashby_SearchAndDetails_UseBoardFeedAndReturnNullForMissingJob()
    {
        const string payload = """
            {"jobs":[{"id":"job_1","title":"Architect","descriptionPlain":"Build systems","location":"Remote","isRemote":true,"applyUrl":"https://jobs.ashbyhq.com/acme/job_1/application","publishedAt":"2025-01-02T12:00:00Z"}]}
            """;
        using HttpClient client = Client(_ => payload);
        IJobProvider provider = new AshbyProvider(client);

        JobPosting found = Assert.Single(await provider.SearchJobsAsync("acme", CompanyId));
        JobPosting? details = await provider.GetJobDetailsAsync("acme", "job_1", CompanyId);
        JobPosting? missing = await provider.GetJobDetailsAsync("acme", "missing", CompanyId);

        Assert.Equal(found.Id, details!.Id);
        Assert.Null(missing);
        Assert.True(found.Location.IsRemote);
        Assert.Equal("Ashby", found.ProviderName);
        Assert.Equal("acme:job_1", found.ExternalId);
    }

    [Fact]
    public async Task Providers_RejectInvalidIdentifiersAndPropagateFailures()
    {
        using HttpClient client = new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("https://example.com/")
        };
        IJobProvider provider = new GreenhouseProvider(client);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SearchJobsAsync("../other", CompanyId));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetJobDetailsAsync("acme", "../123", CompanyId));
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.SearchJobsAsync("acme", CompanyId));
    }

    [Fact]
    public async Task Providers_ReturnNullForMissingDetails()
    {
        using HttpClient client = new(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
        {
            BaseAddress = new Uri("https://example.com/")
        };
        Assert.Null(await new GreenhouseProvider(client).GetJobDetailsAsync("acme", "123", CompanyId));
        Assert.Null(await new LeverProvider(client).GetJobDetailsAsync("acme", "123", CompanyId));
    }

    private static HttpClient Client(Func<HttpRequestMessage, string> response) => new(
        new StubHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response(request))
        }))
    {
        BaseAddress = new Uri("https://example.com/")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
