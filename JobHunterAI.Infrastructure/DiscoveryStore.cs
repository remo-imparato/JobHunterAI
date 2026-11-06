using FluentValidation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure.Background;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Infrastructure;

public sealed class DiscoverySources(IOptions<BackgroundOptions> options) : IDiscoverySources
{
    public IReadOnlyList<DiscoverySource> Boards => options.Value.Boards.Select(x => new DiscoverySource(x.Provider, x.Board, x.CompanyId)).ToArray();
}

public sealed class DiscoveryStore(JobHunterDbContext db) : IDiscoveryStore, IPublicJobStore
{
    public async Task<JobPosting> UpsertAsync(PublicJobListing listing, CancellationToken token)
    {
        string company = listing.CompanyName.Trim();
        if (company.Length is 0 or > 200 || listing.Job.CompanyId !=
            JobProviders.PublicFeedMapping.CompanyId(listing.Job.ProviderName, company))
            throw new ArgumentException("Invalid feed employer identity.", nameof(listing));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.companies ("Id", "Name", "WebsiteUrl")
            VALUES ({listing.Job.CompanyId}, {company}, NULL)
            ON CONFLICT ("Id") DO NOTHING
            """, token);
        return await UpsertJobAsync(listing.Job, token);
    }

    public async Task CreateAsync(JobDiscoveryRun run, CancellationToken token)
    {
        db.Set<JobDiscoveryRun>().Add(run);
        await db.SaveChangesAsync(token);
    }
    public async Task SaveAsync(JobDiscoveryRun run, CancellationToken token) => await db.SaveChangesAsync(token);
    public async Task<IReadOnlyList<JobPosting>> StoredJobsAsync(int limit, CancellationToken token)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return await db.JobPostings.AsNoTracking().OrderByDescending(x => x.PostedAt).ThenBy(x => x.Id).Take(limit).ToListAsync(token);
    }
    public Task<bool> CompanyExistsAsync(Guid id, CancellationToken token) => db.Companies.AnyAsync(x => x.Id == id, token);

    public async Task<int> ClearStoredJobsAsync(CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM public.job_discovery_results", token);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM public.job_discovery_runs", token);
        int deleted = await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM public.job_postings posting
            WHERE NOT EXISTS (SELECT 1 FROM public.applications app WHERE app."JobPostingId" = posting."Id")
              AND NOT EXISTS (SELECT 1 FROM public.cover_letters letter WHERE letter."JobPostingId" = posting."Id")
              AND NOT EXISTS (SELECT 1 FROM public.resume_versions resume WHERE resume."JobPostingId" = posting."Id")
              AND NOT EXISTS (SELECT 1 FROM public.application_packages package WHERE package."JobPostingId" = posting."Id")
            """, token);
        await transaction.CommitAsync(token);
        return deleted;
    }

    public async Task<JobPosting> UpsertJobAsync(JobPosting job, CancellationToken token)
    {
        if (job.Title.Length > 300 || job.ExternalId.Length > 300 || job.ProviderName.Length > 100 ||
            job.ApplicationUrl.Length > 2048 || job.Location.City?.Length > 200 || job.Location.Country?.Length > 200)
            throw new ArgumentException("Provider posting exceeds supported field lengths.", nameof(job));
        int? arrangement = (int?)job.Location.Arrangement;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.job_postings ("Id", "CompanyId", "ProviderName", "ExternalId", "Title", "Description",
                "City", "Country", "IsRemote", "WorkArrangement", "ApplicationUrl", "PostedAt")
            VALUES ({job.Id}, {job.CompanyId}, {job.ProviderName}, {job.ExternalId}, {job.Title}, {job.Description},
                {job.Location.City}, {job.Location.Country}, {job.Location.IsRemote}, {arrangement}, {job.ApplicationUrl}, {job.PostedAt})
            ON CONFLICT ("ProviderName", "ExternalId") DO UPDATE SET
                "CompanyId" = EXCLUDED."CompanyId", "Title" = EXCLUDED."Title", "Description" = EXCLUDED."Description", "City" = EXCLUDED."City",
                "Country" = EXCLUDED."Country", "IsRemote" = EXCLUDED."IsRemote", "WorkArrangement" = EXCLUDED."WorkArrangement",
                "ApplicationUrl" = EXCLUDED."ApplicationUrl"
            """, token);
        return await db.JobPostings.AsNoTracking().SingleAsync(x => x.ProviderName == job.ProviderName && x.ExternalId == job.ExternalId, token);
    }

    public async Task<IReadOnlyList<DiscoveryRunSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token)
    {
        await new ListDiscoveryValidator().ValidateAndThrowAsync(new ListDiscoveryQuery(candidateId, page, pageSize), token);
        return await db.Set<JobDiscoveryRun>().AsNoTracking().Where(x => x.CandidateProfileId == candidateId)
            .OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize)
            .Select(x => new DiscoveryRunSummary(x.Id, x.StartedAt, x.CompletedAt, x.Status, x.ExaminedJobs, x.Results.Count, x.Warnings)).ToListAsync(token);
    }

    public async Task<DiscoveryRunDetails?> DetailsAsync(Guid candidateId, Guid runId, CancellationToken token)
    {
        await new GetDiscoveryValidator().ValidateAndThrowAsync(new GetDiscoveryQuery(candidateId, runId), token);
        JobDiscoveryRun? run = await db.Set<JobDiscoveryRun>().AsNoTracking().Where(x => x.Id == runId && x.CandidateProfileId == candidateId)
            .Include(x => x.Results).AsSplitQuery().SingleOrDefaultAsync(token);
        if (run is null) return null;
        List<DiscoveryJob> jobs = await db.JobPostings.AsNoTracking().Where(x => db.Set<JobDiscoveryResult>()
                .Any(result => result.RunId == runId && result.JobPostingId == x.Id))
            .Join(db.Companies.AsNoTracking(), job => job.CompanyId, company => company.Id, (job, company) => new { job, company })
            .OrderBy(x => x.company.Name).ThenBy(x => x.job.Title).ThenBy(x => x.job.Id)
            .Select(x => new DiscoveryJob(x.job.Id, x.company.Name, x.job.Title, x.job.Description, x.job.ApplicationUrl,
                x.job.Location.City, x.job.Location.Country, x.job.Location.Arrangement)).ToListAsync(token);
        return new(new(run.Id, run.StartedAt, run.CompletedAt, run.Status, run.ExaminedJobs, run.Results.Count, run.Warnings), run.CriteriaSnapshot, jobs);
    }
}
