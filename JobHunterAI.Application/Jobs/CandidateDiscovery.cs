using System.Text.Json;
using FluentValidation;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Application.Jobs;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";
    public int StoredJobsLimit { get; set; } = 200;
    public int JobsPerBoard { get; set; } = 100;
    public int MaxMatches { get; set; } = 100;
    public int TimeoutSeconds { get; set; } = 90;
    public bool IsValid() => StoredJobsLimit is >= 1 and <= 1000 && JobsPerBoard is >= 1 and <= 500 &&
        MaxMatches is >= 1 and <= 100 && TimeoutSeconds is >= 1 and <= 120;
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
    IEnumerable<IJobProvider> providers, IDiscoverySources sources, IDiscoveryStore store, IOptions<DiscoveryOptions> options)
    : IRequestHandler<StartDiscoveryCommand, Result<DiscoveryRunDetails>>
{
    public async Task<Result<DiscoveryRunDetails>> Handle(StartDiscoveryCommand request, CancellationToken token)
    {
        if (await candidates.GetByIdAsync(request.CandidateId, token) is null)
            return Result<DiscoveryRunDetails>.NotFound("Candidate not found.");
        CandidateSearchCriteria? saved = await criteria.GetByIdAsync(request.CandidateId, token);
        if (saved is null) return Result<DiscoveryRunDetails>.Conflict("Save search criteria before starting discovery.");
        DiscoveryOptions limits = options.Value;
        if (!limits.IsValid()) throw new InvalidOperationException("Invalid discovery limits.");
        JobDiscoveryRun run = new(request.RunId, request.CandidateId, JsonSerializer.Serialize(SearchCriteriaInput.From(saved)), DateTimeOffset.UtcNow);
        await store.CreateAsync(run, token);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(limits.TimeoutSeconds));
        CancellationToken ct = timeout.Token;
        HashSet<Guid> seen = [];
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
            if (sources.Boards.Count == 0) run.Warn("No provider boards configured. Only stored postings were searched; configure Background:Boards for live discovery.");
            foreach (DiscoverySource board in sources.Boards.Take(20))
            {
                ct.ThrowIfCancellationRequested();
                if (run.Results.Count >= limits.MaxMatches) break;
                try
                {
                    IJobProvider? provider = providers.FirstOrDefault(x => x.Name == board.Provider);
                    if (provider is null || !await store.CompanyExistsAsync(board.CompanyId, ct))
                    { run.Warn($"{board.Provider}: provider unavailable or configured company missing; source skipped."); continue; }
                    IReadOnlyList<JobPosting> jobs = await provider.SearchJobsAsync(board.Board, board.CompanyId, ct);
                    foreach (JobPosting posting in jobs.Take(limits.JobsPerBoard))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (posting.ProviderName != board.Provider || posting.CompanyId != board.CompanyId)
                            throw new ArgumentException("Provider returned a posting for another source.");
                        JobPosting canonical = await store.UpsertJobAsync(posting, ct);
                        Match(canonical);
                        if (run.Results.Count >= limits.MaxMatches) break;
                    }
                    if (jobs.Count > limits.JobsPerBoard) run.Warn($"{board.Provider}: per-board examination limit reached.");
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or ArgumentException)
                { run.Warn($"{board.Provider}: source failed or returned invalid data; other results remain available."); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { run.Warn($"{board.Provider}: provider request timed out; source skipped."); }
            }
            if (run.Results.Count >= limits.MaxMatches) run.Warn("Match limit reached; additional sources or postings may not have been examined.");
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
        DiscoveryRunDetails? details = await store.DetailsAsync(request.CandidateId, run.Id, finish.Token);
        return Result<DiscoveryRunDetails>.Success(details ?? throw new InvalidOperationException("Saved search run is unavailable."));

        void Match(JobPosting job)
        {
            if (seen.Add(job.Id)) run.Examine();
            if (saved.Matches(job.Title, job.Location.City, job.Location.Country, job.Location.Arrangement, job.Description)) run.AddMatch(job.Id);
            else run.RemoveMatch(job.Id);
        }
    }
}
