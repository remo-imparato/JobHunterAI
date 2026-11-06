using System.Text.Json;
using FluentValidation;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace JobHunterAI.Application.Jobs;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";
    public int StoredJobsLimit { get; set; } = 200;
    public int JobsPerBoard { get; set; } = 100;
    public int MaxMatches { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 90;
    public int SourceTimeoutSeconds { get; set; } = 15;
    public bool IsValid() => StoredJobsLimit is >= 1 and <= 1000 && JobsPerBoard is >= 1 and <= 500 &&
        MaxMatches is >= 1 and <= 100 && TimeoutSeconds is >= 1 and <= 120 && SourceTimeoutSeconds is >= 1 and <= 30;
}
public sealed record DiscoverySource(string Provider, string Board, Guid CompanyId);
public interface IDiscoverySources { IReadOnlyList<DiscoverySource> Boards { get; } }
public sealed record DiscoveryRunSummary(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    DiscoveryStatus Status, int ExaminedJobs, int MatchCount, string[] Warnings);
public sealed record DiscoveryJob(Guid JobId, string Company, string Title, string Description, string ApplicationUrl,
    string? City, string? Country, WorkArrangement? Arrangement);
public sealed record DiscoveryRunDetails(DiscoveryRunSummary Run, string CriteriaSnapshot, IReadOnlyList<DiscoveryJob> Jobs);
public interface IDiscoveryStore
{
    Task CreateAsync(JobDiscoveryRun run, CancellationToken token);
    Task SaveAsync(JobDiscoveryRun run, CancellationToken token);
    Task<IReadOnlyList<JobPosting>> StoredJobsAsync(int limit, CancellationToken token);
    Task<bool> CompanyExistsAsync(Guid id, CancellationToken token);
    Task<JobPosting> UpsertJobAsync(JobPosting job, CancellationToken token);
    Task<IReadOnlyList<DiscoveryRunSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token);
    Task<DiscoveryRunDetails?> DetailsAsync(Guid candidateId, Guid runId, CancellationToken token);
}
public sealed record StartDiscoveryCommand(Guid CandidateId, Guid RunId) : IRequest<Result<DiscoveryRunDetails>>;
public sealed record QueuedDiscovery(Guid RunId, string WorkerJobId);
public sealed record ListDiscoveryQuery(Guid CandidateId, int Page = 0, int PageSize = 10) : IRequest<Result<IReadOnlyList<DiscoveryRunSummary>>>;
public sealed record GetDiscoveryQuery(Guid CandidateId, Guid RunId) : IRequest<Result<DiscoveryRunDetails>>;
public sealed class StartDiscoveryValidator : AbstractValidator<StartDiscoveryCommand>
{
    public StartDiscoveryValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.RunId).NotEmpty(); }
}
public sealed class ListDiscoveryValidator : AbstractValidator<ListDiscoveryQuery>
{
    public ListDiscoveryValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.Page).InclusiveBetween(0, 100000); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); }
}
public sealed class GetDiscoveryValidator : AbstractValidator<GetDiscoveryQuery>
{
    public GetDiscoveryValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.RunId).NotEmpty(); }
}
public sealed class ListDiscoveryHandler(IRepository<CandidateProfile> candidates, IDiscoveryStore store)
    : IRequestHandler<ListDiscoveryQuery, Result<IReadOnlyList<DiscoveryRunSummary>>>
{
    public async Task<Result<IReadOnlyList<DiscoveryRunSummary>>> Handle(ListDiscoveryQuery request, CancellationToken token) =>
        await candidates.GetByIdAsync(request.CandidateId, token) is null
            ? Result<IReadOnlyList<DiscoveryRunSummary>>.NotFound("Candidate not found.")
            : Result<IReadOnlyList<DiscoveryRunSummary>>.Success(await store.ListAsync(request.CandidateId, request.Page, request.PageSize, token));
}
public sealed class GetDiscoveryHandler(IDiscoveryStore store) : IRequestHandler<GetDiscoveryQuery, Result<DiscoveryRunDetails>>
{
    public async Task<Result<DiscoveryRunDetails>> Handle(GetDiscoveryQuery request, CancellationToken token)
    {
        var details = await store.DetailsAsync(request.CandidateId, request.RunId, token);
        return details is null ? Result<DiscoveryRunDetails>.NotFound("Candidate search run not found.") : Result<DiscoveryRunDetails>.Success(details);
    }
}

public sealed class StartDiscoveryHandler(IRepository<CandidateProfile> candidates, IRepository<CandidateSearchCriteria> criteria,
    IEnumerable<IJobProvider> providers, IDiscoverySources sources, IDiscoveryStore store, IOptions<DiscoveryOptions> options,
    IEnumerable<IPublicJobFeed>? feeds = null, IPublicJobStore? publicJobs = null, ILogger<StartDiscoveryHandler>? logger = null)
    : IRequestHandler<StartDiscoveryCommand, Result<DiscoveryRunDetails>>
{
    public async Task<Result<DiscoveryRunDetails>> Handle(StartDiscoveryCommand request, CancellationToken token)
    {
        if (await candidates.GetByIdAsync(request.CandidateId, token) is null)
            return Result<DiscoveryRunDetails>.NotFound("Candidate not found.");
        CandidateSearchCriteria? saved = await criteria.GetByIdAsync(request.CandidateId, token);
        if (saved is null) return Result<DiscoveryRunDetails>.Conflict("Save search criteria before starting discovery.");
        using IDisposable? scope = logger?.BeginScope(new Dictionary<string, object?> { ["RunId"] = request.RunId, ["CandidateId"] = request.CandidateId });
        DiscoveryOptions limits = options.Value;
        if (!limits.IsValid()) throw new InvalidOperationException("Invalid discovery limits.");
        JobProviderSearchCriteria providerCriteria = JobProviderSearchCriteria.From(saved);
        JobDiscoveryRun run = new(request.RunId, request.CandidateId, JsonSerializer.Serialize(SearchCriteriaInput.From(saved)), DateTimeOffset.UtcNow);
        await store.CreateAsync(run, token);
        logger?.LogInformation("Discovery started; criteria are applied locally and passed to provider requests where supported. Per-source limit {SourceLimit}, stored limit {StoredLimit}, match limit {MatchLimit}",
            limits.JobsPerBoard, limits.StoredJobsLimit, limits.MaxMatches);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(limits.TimeoutSeconds));
        CancellationToken ct = timeout.Token;
        HashSet<Guid> seen = [];
        Dictionary<Guid, JobFilterMismatch> assessments = [];
        try
        {
            IReadOnlyList<JobPosting> stored = await store.StoredJobsAsync(limits.StoredJobsLimit, ct);
            foreach (JobPosting job in stored)
            {
                ct.ThrowIfCancellationRequested();
                Match(job);
                if (run.Results.Count >= limits.MaxMatches) break;
            }
            if (stored.Count == limits.StoredJobsLimit) run.Warn("Stored-job examination limit reached; older postings may not have been examined.");
            IPublicJobFeed[] enabledFeeds = (feeds ?? []).Where(x => x.Enabled).ToArray();
            if (sources.Boards.Count == 0 && enabledFeeds.Length == 0)
                run.Warn("No provider boards or public feeds configured. Only stored postings were searched; configure PublicFeeds or Background:Boards for live discovery.");
            foreach (DiscoverySource board in sources.Boards.Take(20))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    IJobProvider? provider = providers.FirstOrDefault(x => x.Name == board.Provider);
                    if (provider is null || !await store.CompanyExistsAsync(board.CompanyId, ct))
                    { run.Warn($"{board.Provider}: provider unavailable or configured company missing; source skipped."); continue; }
                    using CancellationTokenSource sourceTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    sourceTimeout.CancelAfter(TimeSpan.FromSeconds(limits.SourceTimeoutSeconds));
                    IReadOnlyList<JobPosting> jobs;
                    try
                    {
                        jobs = await provider.SearchJobsAsync(board.Board, board.CompanyId, sourceTimeout.Token);
                    }
                    catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException)
                    {
                        throw new JsonException("Provider returned an unexpected payload.", exception);
                    }
                    logger?.LogInformation("Discovery source {Provider}, board {Board}: returned {Returned}; examining up to {SourceLimit}", board.Provider, board.Board, jobs.Count, limits.JobsPerBoard);
                    foreach (JobPosting posting in jobs.Take(limits.JobsPerBoard))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (posting.ProviderName != board.Provider || posting.CompanyId != board.CompanyId)
                            throw new ArgumentException("Provider returned a posting for another source.");
                        JobPosting canonical = await store.UpsertJobAsync(posting, ct);
                        Match(canonical);
                    }
                    if (jobs.Count > limits.JobsPerBoard) run.Warn($"{board.Provider}: per-board examination limit reached.");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or ArgumentException)
                { run.Warn($"{board.Provider}: source failed or returned invalid data; other results remain available."); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { run.Warn($"{board.Provider}: provider request timed out; source skipped."); }
            }
            foreach (IPublicJobFeed feed in enabledFeeds)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (publicJobs is null) throw new InvalidOperationException("Public job persistence is not registered.");
                    PublicJobFeedResult result = await feed.SearchAsync(limits.JobsPerBoard, ct);
                    logger?.LogInformation("Discovery source {Provider}: returned {Returned} unique postings, {WarningCount} warnings; filtering with saved criteria locally", feed.Name, result.Jobs.Count, result.Warnings.Length);
                    foreach (string warning in result.Warnings) run.Warn(warning);
                    foreach (PublicJobListing listing in result.Jobs.Take(limits.JobsPerBoard))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (listing.Job.ProviderName != feed.Name) throw new ArgumentException("Feed returned another provider's job.");
                        Match(await publicJobs.UpsertAsync(listing, ct));
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or ArgumentException)
                { run.Warn($"{feed.Name}: source failed or returned invalid data; other results remain available."); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { run.Warn($"{feed.Name}: provider request timed out; source skipped."); }
            }
            if (run.Results.Count == 0 && assessments.Count > 0)
                run.Warn($"No postings passed all saved filters. Rejections (a posting can fail multiple filters): title={Rejected(JobFilterMismatch.Title)}, location={Rejected(JobFilterMismatch.Location)}, work arrangement={Rejected(JobFilterMismatch.WorkArrangement)}, required keywords={Rejected(JobFilterMismatch.RequiredKeywords)}, excluded keywords={Rejected(JobFilterMismatch.ExcludedKeywords)}. Expand 'Saved criteria used for this run' to review the exact criteria.");
            if (run.Results.Count >= limits.MaxMatches) run.Warn("Match limit reached; all available sources were attempted within the time limit, but additional matches were not saved.");
            run.Finish(run.Warnings.Length == 0 ? DiscoveryStatus.Completed : DiscoveryStatus.CompletedWithWarnings, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            run.Warn(token.IsCancellationRequested ? "Search cancelled by the caller; any completed results are retained." : "Search time limit reached; any completed results are retained.");
            run.Finish(DiscoveryStatus.Cancelled, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            run.Warn("Search failed while accessing persisted data. Check database connectivity and migrations.");
            run.Finish(DiscoveryStatus.Failed, DateTimeOffset.UtcNow);
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            await store.SaveAsync(run, cleanup.Token);
            throw;
        }
        using CancellationTokenSource finish = new(TimeSpan.FromSeconds(10));
        await store.SaveAsync(run, finish.Token);
        logger?.LogInformation("Discovery finished with {DiscoveryStatus}; examined {Examined}, saved matches {MatchCount}. Rejected by title {TitleRejected}, location {LocationRejected}, arrangement {ArrangementRejected}, required keywords {RequiredRejected}, excluded keywords {ExcludedRejected}",
            run.Status, run.ExaminedJobs, run.Results.Count, Rejected(JobFilterMismatch.Title), Rejected(JobFilterMismatch.Location), Rejected(JobFilterMismatch.WorkArrangement), Rejected(JobFilterMismatch.RequiredKeywords), Rejected(JobFilterMismatch.ExcludedKeywords));
        foreach (string warning in run.Warnings) logger?.LogWarning("Discovery warning: {DiscoveryWarning}", warning);
        DiscoveryRunDetails? details = await store.DetailsAsync(request.CandidateId, run.Id, finish.Token);
        return Result<DiscoveryRunDetails>.Success(details ?? throw new InvalidOperationException("Saved search run is unavailable."));

        void Match(JobPosting job)
        {
            if (seen.Add(job.Id)) run.Examine();
            JobFilterMismatch reasons = saved.MismatchReasons(job.Title, job.Location.City, job.Location.Country, job.Location.Arrangement, job.Description);
            assessments[job.Id] = reasons;
            if (reasons == JobFilterMismatch.None)
            {
                if (run.Results.Count < limits.MaxMatches) run.AddMatch(job.Id);
            }
            else run.RemoveMatch(job.Id);
        }
        int Rejected(JobFilterMismatch reason) => assessments.Values.Count(value => (value & reason) != 0);
    }
}
