using System.Net;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Web;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobHunterAI.UnitTests;

public sealed class WebClientTests
{
    [Fact]
    public async Task Read_EncodesSearchAndPreservesPaging()
    {
        Factory factory = new("{\"items\":[],\"totalCount\":23}");
        DashboardClient client = new(factory);
        Guid candidateId = Guid.NewGuid();
        DashboardPage page = await client.ReadAsync(DashboardResource.Jobs,
            new DashboardQuery(candidateId, 2, 25, "C# & .NET", "Lever"), CancellationToken.None);
        Assert.Equal(23, page.TotalCount);
        Assert.Contains($"candidateId={candidateId}", factory.Uri!.Query);
        Assert.Contains("page=2&pageSize=25", factory.Uri!.Query);
        Assert.Contains("C%23%20%26%20.NET", factory.Uri.Query);
        Assert.Equal("JobHunterAI.Api", factory.Name);
    }

    [Fact]
    public async Task Read_RejectsInvalidQueryBeforeHttp()
    {
        Factory factory = new("{}");
        await Assert.ThrowsAsync<ArgumentException>(() => new DashboardClient(factory).ReadAsync(DashboardResource.Jobs,
            new DashboardQuery(Guid.Empty), CancellationToken.None));
        Assert.Null(factory.Uri);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullResponses_ThrowRatherThanInventData(bool summary)
    {
        DashboardClient client = new(new Factory("null"));
        if (summary) await Assert.ThrowsAsync<HttpRequestException>(() => client.SummarizeAsync(Guid.NewGuid(), CancellationToken.None));
        else await Assert.ThrowsAsync<HttpRequestException>(() => client.ReadAsync(DashboardResource.Jobs, new(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Summary_ReturnsTypedCounts()
    {
        Factory factory = new("{\"jobs\":3,\"applications\":1,\"resumeVersions\":2,\"coverLetters\":0,\"runningExecutions\":0,\"applicationStatuses\":[]}");
        Guid candidateId = Guid.NewGuid();
        var result = await new DashboardClient(factory).SummarizeAsync(candidateId, CancellationToken.None);
        Assert.Equal(3, result.Jobs);
        Assert.Equal(1, result.Applications);
        Assert.Equal($"?candidateId={candidateId}", factory.Uri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, HealthStatus.Healthy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HealthStatus.Unhealthy)]
    public async Task Readiness_UsesApiReadinessAndDoesNotExposeErrorBodies(HttpStatusCode status, HealthStatus expected)
    {
        Factory factory = new("private response", status);
        HealthCheckResult result = await new ApiReadinessCheck(factory).CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(expected, result.Status);
        Assert.Equal("/health/ready", factory.Uri!.AbsolutePath);
        Assert.DoesNotContain("private", result.Description ?? "");
    }

    [Fact]
    public void Preferences_AreIndependentAndHaveBoundedDefault()
    {
        DashboardPreferences first = new() { CandidateId = Guid.NewGuid(), PageSize = 25 };
        DashboardPreferences second = new();
        Assert.Equal(Guid.Empty, second.CandidateId);
        Assert.Equal(10, second.PageSize);
        Assert.NotEqual(first.CandidateId, second.CandidateId);
    }

    private sealed class Factory(string json, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        public Uri? Uri { get; private set; }
        public string? Name { get; private set; }
        public HttpClient CreateClient(string name)
        {
            Name = name;
            return new HttpClient(new Handler(request =>
            {
                Uri = request.RequestUri;
                return new HttpResponseMessage(status) { Content = new StringContent(json) };
            })) { BaseAddress = new Uri("https://example.com/") };
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
