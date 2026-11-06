using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobHunterAI.IntegrationTests;

public sealed class DashboardTests
{
    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, 101)]
    [InlineData(100001, 10)]
    public async Task Reader_RejectsInvalidPagingBeforeDatabaseAccess(int page, int pageSize)
    {
        await using var db = new JobHunterDbContext(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql("Host=localhost;Database=tests;Username=tests").Options);
        var reader = new DashboardReader(db);
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadAsync(DashboardResource.Jobs,
            new DashboardQuery(Guid.NewGuid(), page, pageSize)));
    }

    [Theory]
    [InlineData("Jobs", "page=-1", HttpStatusCode.BadRequest)]
    [InlineData("Jobs", "pageSize=101", HttpStatusCode.BadRequest)]
    [InlineData("Applications", "filter=NotAStatus", HttpStatusCode.BadRequest)]
    [InlineData("AutomationQueue", "filter=99", HttpStatusCode.BadRequest)]
    [InlineData("ResumeVersions", "filter=Unknown", HttpStatusCode.BadRequest)]
    [InlineData("Unknown", "page=0", HttpStatusCode.NotFound)]
    public async Task Api_RejectsInvalidQueries(string resource, string query, HttpStatusCode expected)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/dashboard/{resource}?candidateId={Guid.NewGuid()}&{query}");
        Assert.Equal(expected, response.StatusCode);
        Assert.Null(factory.Reader.LastQuery);
    }

    [Fact]
    public async Task Api_ForwardsPagingSearchFilterAndCandidate()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Guid candidate = Guid.NewGuid();
        var page = await client.GetFromJsonAsync<DashboardPage>($"/api/dashboard/Jobs?candidateId={candidate}&page=2&pageSize=25&search=Architect&filter=Greenhouse");
        Assert.Equal(new DashboardQuery(candidate, 2, 25, "Architect", "Greenhouse"), factory.Reader.LastQuery);
        Assert.Equal(51, page!.TotalCount);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Api_SummaryValidatesCandidateAndReturnsTypedMetrics()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var invalid = await client.GetAsync($"/api/dashboard/summary?candidateId={Guid.Empty}");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var summary = await client.GetFromJsonAsync<DashboardSummary>($"/api/dashboard/summary?candidateId={Guid.NewGuid()}");
        Assert.Equal(4, summary!.Jobs);
        Assert.Equal("Draft", Assert.Single(summary.ApplicationStatuses).Label);
    }

    [Fact]
    public void Query_RejectsUnboundedSearchAndInvalidCandidate()
    {
        Assert.Throws<ArgumentException>(() => new DashboardQuery(Guid.Empty).Validate());
        Assert.Throws<ArgumentException>(() => new DashboardQuery(Guid.NewGuid(), Search: new string('x', 201)).Validate());
        Assert.Throws<ArgumentException>(() => new DashboardQuery(Guid.NewGuid(), Filter: new string('x', 101)).Validate());
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public Reader Reader { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.UseSetting("Background:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDashboardReader>();
                services.AddSingleton<IDashboardReader>(Reader);
            });
        }
    }

    private sealed class Reader : IDashboardReader
    {
        public DashboardQuery? LastQuery { get; private set; }
        public Task<DashboardPage> ReadAsync(DashboardResource resource, DashboardQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(new DashboardPage([new(Guid.NewGuid(), "Architect", "Greenhouse", "Remote", null)], 51));
        }
        public Task<DashboardSummary> SummarizeAsync(Guid candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardSummary(4, 1, 2, 1, 0, [new("Draft", 1)]));
    }
}
