using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobHunterAI.IntegrationTests;

public sealed class DiscoveryApiTests
{
    [Fact]
    public async Task Discovery_StartHistoryDetailsAndIsolation_WorkWithoutWorkersOrAi()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        Guid runId = Guid.NewGuid();
        string path = $"/api/candidates/{factory.Candidate.Id}/discovery";
        using var start = await client.PostAsJsonAsync(path, new { runId });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var details = (await start.Content.ReadFromJsonAsync<DiscoveryRunDetails>())!;
        Assert.Equal(runId, details.Run.Id);
        Assert.Equal(DiscoveryStatus.CompletedWithWarnings, details.Run.Status);
        Assert.Single(details.Jobs);
        Assert.Contains(details.Run.Warnings, x => x.Contains("No provider boards"));
        Assert.Equal(runId, Assert.Single((await client.GetFromJsonAsync<DiscoveryRunSummary[]>(path))!).Id);
        Assert.Equal(runId, (await client.GetFromJsonAsync<DiscoveryRunDetails>($"{path}/{runId}"))!.Run.Id);
        using var wrong = await client.GetAsync($"/api/candidates/{Guid.NewGuid()}/discovery/{runId}");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var next = await client.GetAsync(path + "?page=1");
        Assert.Empty((await next.Content.ReadFromJsonAsync<DiscoveryRunSummary[]>())!);
        using var missing = await client.GetAsync($"/api/candidates/{Guid.NewGuid()}/discovery");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Discovery_RejectsMissingCandidateCriteriaAndInvalidInputs()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = $"/api/candidates/{factory.Candidate.Id}/discovery";
        using var invalidRun = await client.PostAsJsonAsync(path, new { runId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRun.StatusCode);
        using var invalidPage = await client.GetAsync(path + "?page=-1&pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        using var missing = await client.PostAsJsonAsync($"/api/candidates/{Guid.NewGuid()}/discovery", new { runId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        factory.Criteria.Item = null;
        using var criteria = await client.PostAsJsonAsync(path, new { runId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, criteria.StatusCode);
        Assert.Empty(factory.Store.Runs);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public CandidateProfile Candidate { get; } = new(Guid.NewGuid(), "Candidate", new EmailAddress("candidate@example.invalid"), "Engineer");
        public Repository<CandidateSearchCriteria> Criteria { get; } = new();
        public MemoryStore Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Background:Enabled", "false");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            Criteria.Item = new CandidateSearchCriteria(Candidate.Id, ["Engineer"], [], WorkArrangement.Remote, false, [], []);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRepository<CandidateProfile>>();
                services.RemoveAll<IRepository<CandidateSearchCriteria>>();
                services.RemoveAll<IDiscoveryStore>();
                services.RemoveAll<IDiscoverySources>();
                services.AddSingleton<IRepository<CandidateProfile>>(new Repository<CandidateProfile> { Id = Candidate.Id, Item = Candidate });
                Criteria.Id = Candidate.Id;
                services.AddSingleton<IRepository<CandidateSearchCriteria>>(Criteria);
                services.AddSingleton<IDiscoveryStore>(Store);
                services.AddSingleton<IDiscoverySources, Sources>();
            });
        }
    }
    private sealed class Sources : IDiscoverySources { public IReadOnlyList<DiscoverySource> Boards => []; }
    private sealed class Repository<T> : IRepository<T> where T : class
    {
        public Guid Id { get; set; }
        public T? Item { get; set; }
        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == Id ? Item : null);
        public Task<IReadOnlyList<T>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Item is null ? [] : new[] { Item }.Skip(skip).Take(take).ToArray());
        public void Add(T entity) => Item = entity;
        public void Remove(T entity) => Item = null;
    }
    private sealed class MemoryStore : IDiscoveryStore
    {
        public List<JobDiscoveryRun> Runs { get; } = [];
        private readonly JobPosting _job = new(Guid.NewGuid(), Guid.NewGuid(), "Lever", "acme:1", "Engineer", "Build", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
        public Task CreateAsync(JobDiscoveryRun run, CancellationToken token) { Runs.Add(run); return Task.CompletedTask; }
        public Task SaveAsync(JobDiscoveryRun run, CancellationToken token) => Task.CompletedTask;
        public Task<bool> CompanyExistsAsync(Guid id, CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<JobPosting>> StoredJobsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<JobPosting>>([_job]);
        public Task<JobPosting> UpsertJobAsync(JobPosting job, CancellationToken token) => Task.FromResult(job);
        public Task<IReadOnlyList<DiscoveryRunSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token) => Task.FromResult<IReadOnlyList<DiscoveryRunSummary>>(Runs.Where(x => x.CandidateProfileId == candidateId).Skip(page * pageSize).Take(pageSize).Select(Summary).ToArray());
        public Task<DiscoveryRunDetails?> DetailsAsync(Guid candidateId, Guid runId, CancellationToken token)
        {
            var run = Runs.SingleOrDefault(x => x.Id == runId && x.CandidateProfileId == candidateId);
            return Task.FromResult(run is null ? null : new DiscoveryRunDetails(Summary(run), run.CriteriaSnapshot, [new(_job.Id, "Acme", _job.Title, _job.Description, _job.ApplicationUrl, null, null, _job.Location.Arrangement)]));
        }
        private static DiscoveryRunSummary Summary(JobDiscoveryRun run) => new(run.Id, run.StartedAt, run.CompletedAt, run.Status, run.ExaminedJobs, run.Results.Count, run.Warnings);
    }
}
