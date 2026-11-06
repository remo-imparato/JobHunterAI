using JobHunterAI.Application.Common;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class DiscoveryTests
{
    [Fact]
    public void Run_TracksUniqueMatchesSnapshotsWarningsAndFinalState()
    {
        var run = new JobDiscoveryRun(Guid.NewGuid(), Guid.NewGuid(), "criteria", DateTimeOffset.UtcNow);
        Guid job = Guid.NewGuid();
        run.Examine(); run.AddMatch(job); run.AddMatch(job);
        Assert.Single(run.Results);
        run.RemoveMatch(job); Assert.Empty(run.Results);
        run.AddMatch(job);
        for (int i = 0; i < 30; i++) run.Warn("Warning");
        Assert.Equal(24, run.Warnings.Length);
        string[] warnings = run.Warnings; warnings[0] = "Changed";
        Assert.Equal("Warning", run.Warnings[0]);
        Assert.Throws<ArgumentException>(() => run.Finish(DiscoveryStatus.Running, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => run.Finish(DiscoveryStatus.Completed, run.StartedAt.AddSeconds(-1)));
        run.Finish(DiscoveryStatus.CompletedWithWarnings, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(run.Examine);
        Assert.Throws<InvalidOperationException>(() => run.AddMatch(job));
        Assert.Equal(1, run.ExaminedJobs);
        Assert.Equal("criteria", run.CriteriaSnapshot);
    }

    [Fact]
    public async Task Discovery_RequiresCandidateAndSavedCriteria()
    {
        Fixture f = new();
        var missing = await f.Handler.Handle(new(Guid.NewGuid(), Guid.NewGuid()), default);
        Assert.Equal(ErrorKind.NotFound, missing.Error);
        f.Criteria.Items.Clear();
        Assert.Equal(ErrorKind.Conflict, (await f.Start()).Error);
        Assert.Empty(f.Store.Runs);
    }

    [Fact]
    public async Task Discovery_DeduplicatesSourcesAndRechecksRefreshedMetadata()
    {
        Fixture f = new();
        JobPosting job = f.Job();
        f.Store.Jobs.Add(job);
        f.Provider.Jobs = [job, job];
        var result = (await f.Start()).Value!;
        Assert.Single(result.Jobs);
        Assert.Equal(1, result.Run.ExaminedJobs);
        Assert.Equal(DiscoveryStatus.Completed, result.Run.Status);
        Assert.Contains("Engineer", result.CriteriaSnapshot);
        Assert.Single(f.Store.Jobs);
        JobPosting updated = new(job.Id, job.CompanyId, job.ProviderName, job.ExternalId, "Sales", "Sell", job.Location, job.ApplicationUrl);
        f.Provider.Jobs = [updated];
        Assert.Empty((await f.Start()).Value!.Jobs);
        Assert.Equal(2, f.Store.Runs.Count);
    }

    [Fact]
    public async Task Discovery_IsolatesProviderFailuresAndRejectsMismatchedSource()
    {
        Fixture f = new();
        f.Sources.Boards = [new("Missing", "acme", f.Company), new("Lever", "acme", f.Company)];
        f.Provider.Failure = new HttpRequestException("secret backend response");
        var result = (await f.Start()).Value!;
        Assert.Equal(DiscoveryStatus.CompletedWithWarnings, result.Run.Status);
        Assert.Equal(2, result.Run.Warnings.Length);
        Assert.DoesNotContain("secret", string.Join(' ', result.Run.Warnings));
        f.Provider.Failure = null;
        f.Provider.Jobs = [new JobPosting(Guid.NewGuid(), Guid.NewGuid(), "Lever", "acme:other", "Engineer", "Build", new(null, null, WorkArrangement.Remote), "https://example.invalid/job")];
        result = (await f.Start()).Value!;
        Assert.Empty(result.Jobs);
        Assert.Empty(f.Store.Jobs);
        Assert.Contains(result.Run.Warnings, x => x.Contains("invalid data"));
    }

    [Fact]
    public async Task Discovery_BoundsStoredJobsBoardJobsAndMatches()
    {
        Fixture f = new();
        f.Limits.StoredJobsLimit = 1;
        f.Limits.JobsPerBoard = 1;
        f.Limits.MaxMatches = 2;
        f.Store.Jobs.Add(f.Job());
        f.Store.Jobs.Add(f.Job());
        f.Provider.Jobs = [f.Job(), f.Job()];
        var result = (await f.Start()).Value!;
        Assert.Equal(2, result.Jobs.Count);
        Assert.Equal(2, result.Run.ExaminedJobs);
        Assert.Equal(1, f.Store.Upserts);
        Assert.Contains(result.Run.Warnings, x => x.Contains("Stored-job"));
        Assert.Contains(result.Run.Warnings, x => x.Contains("per-board"));
        Assert.Contains(result.Run.Warnings, x => x.Contains("Match limit"));
    }

    [Fact]
    public async Task Discovery_NoBoardsUnknownMetadataAndCandidateIsolation()
    {
        Fixture f = new();
        f.Sources.Boards = [];
        f.Store.Jobs.Add(f.Job(arrangement: null));
        var result = (await f.Start()).Value!;
        Assert.Empty(result.Jobs);
        Assert.Contains(result.Run.Warnings, x => x.Contains("No provider boards"));
        Assert.Contains(result.Run.Warnings, x => x.Contains("title=0") && x.Contains("work arrangement=1"));
        Assert.Null(await f.Store.DetailsAsync(Guid.NewGuid(), result.Run.Id, default));
        Assert.Empty(await f.Store.ListAsync(Guid.NewGuid(), 0, 10, default));
        f.Criteria.Items[0].Update(["Engineer"], [], WorkArrangement.Remote, true, [], []);
        Assert.Single((await f.Start()).Value!.Jobs);
    }

    [Fact]
    public async Task Discovery_CancellationRetainsPartialResultsAndUsesIndependentCleanupToken()
    {
        Fixture f = new();
        f.Store.Jobs.Add(f.Job());
        using CancellationTokenSource cancellation = new();
        f.Provider.Action = () => cancellation.Cancel();
        var result = (await f.Start(cancellation.Token)).Value!;
        Assert.Equal(DiscoveryStatus.Cancelled, result.Run.Status);
        Assert.Single(result.Jobs);
        Assert.True(f.Store.SavedWithUsableToken);
        Assert.Contains(result.Run.Warnings, x => x.Contains("caller"));
    }

    [Fact]
    public async Task Discovery_TimeoutRetainsStatusAndProviderTimeoutIsIsolated()
    {
        Fixture f = new();
        f.Provider.Failure = new TaskCanceledException("provider timeout");
        Assert.Equal(DiscoveryStatus.CompletedWithWarnings, (await f.Start()).Value!.Run.Status);
        f.Provider.Failure = null;
        f.Provider.Delay = true;
        f.Limits.TimeoutSeconds = 1;
        var result = (await f.Start()).Value!;
        Assert.Equal(DiscoveryStatus.Cancelled, result.Run.Status);
        Assert.Contains(result.Run.Warnings, x => x.Contains("time limit"));
    }

    [Fact]
    public async Task Discovery_FatalStorageErrorPersistsFailedRunAndRethrows()
    {
        Fixture f = new();
        f.Store.FailRead = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Start());
        Assert.Equal(DiscoveryStatus.Failed, Assert.Single(f.Store.Runs).Status);
        Assert.True(f.Store.SavedWithUsableToken);
    }

    [Fact]
    public async Task QueriesValidateAndReturnMissingRecords()
    {
        Fixture f = new();
        Assert.Equal(ErrorKind.NotFound, (await new ListDiscoveryHandler(f.Candidates, f.Store).Handle(new(Guid.NewGuid()), default)).Error);
        Assert.Equal(ErrorKind.NotFound, (await new GetDiscoveryHandler(f.Store).Handle(new(f.Candidate.Id, Guid.NewGuid()), default)).Error);
        await f.Start();
        Assert.Single((await new ListDiscoveryHandler(f.Candidates, f.Store).Handle(new(f.Candidate.Id), default)).Value!);
        Assert.False(new StartDiscoveryValidator().Validate(new StartDiscoveryCommand(Guid.Empty, Guid.Empty)).IsValid);
        Assert.False(new GetDiscoveryValidator().Validate(new GetDiscoveryQuery(Guid.Empty, Guid.Empty)).IsValid);
        Assert.False(new ListDiscoveryValidator().Validate(new ListDiscoveryQuery(Guid.NewGuid(), -1, 101)).IsValid);
        Assert.True(new DiscoveryOptions().IsValid());
        Assert.False(new DiscoveryOptions { StoredJobsLimit = 0 }.IsValid());
        Assert.False(new DiscoveryOptions { JobsPerBoard = 501 }.IsValid());
        Assert.False(new DiscoveryOptions { MaxMatches = 101 }.IsValid());
        Assert.False(new DiscoveryOptions { TimeoutSeconds = 121 }.IsValid());
    }

    [Fact]
    public async Task Discovery_AttemptsEveryBoardAndPublicFeedEvenAfterMatchCap()
    {
        Fixture f = new();
        f.Limits.MaxMatches = 1;
        f.Store.Jobs.Add(f.Job());
        f.Sources.Boards = [new("Lever", "acme", f.Company), new("Lever", "acme", f.Company)];
        f.Provider.Jobs = [f.Job()];
        Feed first = new("Arbeitnow", f.Company);
        Feed second = new("RemoteOK", f.Company);
        f.Feeds.AddRange([first, second]);

        var result = (await f.Start()).Value!;

        Assert.Single(result.Jobs);
        Assert.Equal(2, f.Provider.Calls);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Contains(result.Run.Warnings, x => x.Contains("Match limit"));
    }

    [Fact]
    public async Task Discovery_PublicFeedFailureDoesNotPreventOtherFeeds()
    {
        Fixture f = new();
        f.Sources.Boards = [];
        Feed first = new("Arbeitnow", f.Company) { Failure = new HttpRequestException("private error") };
        Feed second = new("RemoteOK", f.Company);
        f.Feeds.AddRange([first, second]);

        var result = (await f.Start()).Value!;

        Assert.Single(result.Jobs);
        Assert.Equal(1, second.Calls);
        Assert.DoesNotContain(result.Run.Warnings, x => x.Contains("No provider boards") || x.Contains("private error"));
        Assert.Contains(result.Run.Warnings, x => x.Contains("Arbeitnow"));
    }

    [Fact]
    public async Task Discovery_MalformedAtsResponseDoesNotPreventPublicFeeds()
    {
        Fixture f = new();
        f.Provider.Failure = new InvalidOperationException("Unexpected JSON shape");
        Feed feed = new("Arbeitnow", f.Company);
        f.Feeds.Add(feed);

        var result = (await f.Start()).Value!;

        Assert.Single(result.Jobs);
        Assert.Equal(1, feed.Calls);
        Assert.Contains(result.Run.Warnings, x => x.Contains("Lever"));
    }

    private sealed class Feed(string name, Guid company) : IPublicJobFeed
    {
        public string Name => name;
        public bool Enabled => true;
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public Task<PublicJobFeedResult> SearchAsync(int limit, CancellationToken token)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            JobPosting job = new(Guid.NewGuid(), company, Name, "public:1", "Engineer", "Build software",
                new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
            return Task.FromResult(new PublicJobFeedResult([new("Employer", job)], []));
        }
    }

    private sealed class Fixture
    {
        public Guid Company { get; } = Guid.NewGuid();
        public CandidateProfile Candidate { get; } = new(Guid.NewGuid(), "Candidate", new EmailAddress("candidate@example.invalid"), "Engineer");
        public FakeRepository<CandidateProfile> Candidates { get; }
        public FakeRepository<CandidateSearchCriteria> Criteria { get; }
        public MemoryStore Store { get; } = new();
        public Sources Sources { get; } = new();
        public Provider Provider { get; } = new();
        public DiscoveryOptions Limits { get; } = new();
        public List<IPublicJobFeed> Feeds { get; } = [];
        public StartDiscoveryHandler Handler => new(Candidates, Criteria, [Provider], Sources, Store, Options.Create(Limits), Feeds, Store);
        public Fixture()
        {
            Candidates = new(x => x.Id, Candidate);
            Criteria = new(x => x.Id, new CandidateSearchCriteria(Candidate.Id, ["Engineer"], [], WorkArrangement.Remote, false, [], []));
            Sources.Boards = [new("Lever", "acme", Company)];
        }
        public Task<Result<DiscoveryRunDetails>> Start(CancellationToken token = default) => Handler.Handle(new(Candidate.Id, Guid.NewGuid()), token);
        public JobPosting Job(WorkArrangement? arrangement = WorkArrangement.Remote) => new(Guid.NewGuid(), Company, "Lever", "acme:" + Guid.NewGuid(), "Engineer", "Build software", new(null, null, arrangement), "https://example.invalid/job");
    }
    private sealed class Sources : IDiscoverySources { public IReadOnlyList<DiscoverySource> Boards { get; set; } = []; }
    private sealed class Provider : IJobProvider
    {
        public string Name => "Lever";
        public IReadOnlyList<JobPosting> Jobs { get; set; } = [];
        public Exception? Failure { get; set; }
        public Action? Action { get; set; }
        public bool Delay { get; set; }
        public int Calls { get; private set; }
        public async Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId, CancellationToken cancellationToken = default)
        {
            Calls++;
            Action?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (Delay) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure is not null) throw Failure;
            return Jobs;
        }
        public Task<JobPosting?> GetJobDetailsAsync(string board, string jobId, Guid companyId, CancellationToken cancellationToken = default) => Task.FromResult<JobPosting?>(null);
    }
    private sealed class MemoryStore : IDiscoveryStore, IPublicJobStore
    {
        public List<JobDiscoveryRun> Runs { get; } = [];
        public List<JobPosting> Jobs { get; } = [];
        public bool FailRead { get; set; }
        public bool SavedWithUsableToken { get; private set; }
        public int Upserts { get; private set; }
        public Task<JobPosting> UpsertAsync(PublicJobListing listing, CancellationToken token) => UpsertJobAsync(listing.Job, token);
        public Task CreateAsync(JobDiscoveryRun run, CancellationToken token) { Runs.Add(run); return Task.CompletedTask; }
        public Task SaveAsync(JobDiscoveryRun run, CancellationToken token) { SavedWithUsableToken = !token.IsCancellationRequested; return Task.CompletedTask; }
        public Task<IReadOnlyList<JobPosting>> StoredJobsAsync(int limit, CancellationToken token) => FailRead ? throw new InvalidOperationException("storage failed") : Task.FromResult<IReadOnlyList<JobPosting>>(Jobs.Take(limit).ToArray());
        public Task<bool> CompanyExistsAsync(Guid id, CancellationToken token) => Task.FromResult(true);
        public Task<JobPosting> UpsertJobAsync(JobPosting job, CancellationToken token)
        {
            Upserts++;
            Jobs.RemoveAll(x => x.Id == job.Id); Jobs.Add(job);
            return Task.FromResult(job);
        }
        public Task<IReadOnlyList<DiscoveryRunSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token) => Task.FromResult<IReadOnlyList<DiscoveryRunSummary>>(Runs.Where(x => x.CandidateProfileId == candidateId).Skip(page * pageSize).Take(pageSize).Select(Summary).ToArray());
        public Task<DiscoveryRunDetails?> DetailsAsync(Guid candidateId, Guid runId, CancellationToken token)
        {
            var run = Runs.SingleOrDefault(x => x.Id == runId && x.CandidateProfileId == candidateId);
            return Task.FromResult(run is null ? null : new DiscoveryRunDetails(Summary(run), run.CriteriaSnapshot, Jobs.Where(x => run.Results.Any(r => r.JobPostingId == x.Id)).Select(x => new DiscoveryJob(x.Id, "Company", x.Title, x.Description, x.ApplicationUrl, x.Location.City, x.Location.Country, x.Location.Arrangement)).ToArray()));
        }
        private static DiscoveryRunSummary Summary(JobDiscoveryRun run) => new(run.Id, run.StartedAt, run.CompletedAt, run.Status, run.ExaminedJobs, run.Results.Count, run.Warnings);
    }
}
