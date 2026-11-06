using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Infrastructure.Background;

/// <summary>Runs in the durable worker, never submits applications or contacts employers.</summary>
public sealed class CandidateSearchWorkflow(JobHunterDbContext db, ISender sender,
    IOptions<BackgroundOptions> options, IPackageDraftingService drafting, IPackageStore packages,
    ILogger<CandidateSearchWorkflow> logger, IDiscoveryStore discovery)
{
    public async Task RunAsync(Guid candidateId, Guid runId, CancellationToken token)
    {
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?> { ["RunId"] = runId, ["CandidateId"] = candidateId });
        logger.LogInformation("Background pipeline started; discovering jobs before package generation");
        // A worker restarted after search completion can resume drafting without inserting the run twice.
        DiscoveryRunDetails? details = await discovery.DetailsAsync(candidateId, runId, token);
        if (details is null)
        {
            var result = await sender.Send(new StartDiscoveryCommand(candidateId, runId), token);
            if (!result.IsSuccess || result.Value is null)
                throw new InvalidOperationException(result.Message ?? "Candidate discovery failed.");
            details = result.Value;
        }
        else if (details.Run.Status == DiscoveryStatus.Running)
        {
            // Search itself is bounded, not checkpointed. Preserve an interrupted run rather than duplicating it.
            JobDiscoveryRun interrupted = await db.Set<JobDiscoveryRun>().SingleAsync(x => x.Id == runId && x.CandidateProfileId == candidateId, token);
            interrupted.Warn("Worker restarted during search; start a new search to refresh all sources.");
            interrupted.Finish(DiscoveryStatus.Cancelled, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(token);
            return;
        }
        logger.LogInformation("Background discovery status {DiscoveryStatus}; examined {Examined}, matched {MatchCount}",
            details.Run.Status, details.Run.ExaminedJobs, details.Jobs.Count);
        if (details.Run.Status is DiscoveryStatus.Cancelled or DiscoveryStatus.Failed) return;
        if (details.Jobs.Count == 0)
        {
            logger.LogInformation("Background pipeline completed with zero matches; no resume or cover-letter generation was attempted. Review discovery criteria and rejection counts");
            return;
        }
        if (!options.Value.EnableAiJobs || !drafting.IsAvailable)
        {
            logger.LogWarning("Search {RunId} saved; package drafting skipped because AI is disabled or unconfigured", runId);
            return;
        }
        Guid? configured = options.Value.Candidates.SingleOrDefault(x => x.CandidateId == candidateId)?.ResumeId;
        var sources = await db.Resumes.AsNoTracking().Where(x => x.CandidateProfileId == candidateId && x.SourceText != null &&
            x.Versions.Any(v => v.IsSource)).Select(x => x.Id).ToListAsync(token);
        Guid? resumeId = configured ?? (sources.Count == 1 ? sources[0] : null);
        if (resumeId is null || !sources.Contains(resumeId.Value))
        {
            logger.LogWarning("Search {RunId} saved; upload one source resume or explicitly configure Background:Candidates:ResumeId", runId);
            return;
        }
        int attempted = 0;
        int reused = 0;
        int completed = 0;
        int needsReview = 0;
        int deferred = 0;
        foreach (DiscoveryJob job in details.Jobs)
        {
            token.ThrowIfCancellationRequested();
            // Completed packages are immutable and should not consume each run's AI budget.
            var existing = await packages.FindAsync(candidateId, job.JobId, resumeId.Value, token);
            if (existing?.Status == PackageStatus.Completed) { reused++; continue; }
            if (attempted >= options.Value.BatchSize) { deferred++; continue; }
            attempted++;
            logger.LogInformation("Drafting package for job {JobId} from source resume {ResumeId}", job.JobId, resumeId.Value);
            var generated = await sender.Send(new GeneratePackageCommand(candidateId, job.JobId, resumeId.Value), token);
            if (!generated.IsSuccess || generated.Value?.Status != PackageStatus.Completed)
            {
                needsReview++;
                logger.LogWarning("Package for job {JobId} needs review or retry; status {Status}, failure code {FailureCode}",
                    job.JobId, generated.Value?.Status, generated.Value?.FailureCode);
            }
            else
            {
                completed++;
                logger.LogInformation("Package {PackageId} completed for job {JobId}; review/download from {PackagePath}", generated.Value!.Id, job.JobId, $"/package/{job.JobId}");
            }
            // One failed AI draft does not prevent attempts for the other matched jobs.
        }
        logger.LogInformation("Background pipeline completed; attempted {Attempted}, completed {Completed}, reused {Reused}, need review {NeedsReview}, deferred by batch limit {Deferred}",
            attempted, completed, reused, needsReview, deferred);
    }

    public async Task RunScheduledAsync(CancellationToken token)
    {
        if (!options.Value.Enabled || !options.Value.EnableSearchPipeline) return;
        // Explicit opt-in list: do not send every user's resume to an AI service automatically.
        foreach (BackgroundCandidate target in options.Value.Candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await RunAsync(target.CandidateId, Guid.NewGuid(), token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Scheduled search failed for candidate {CandidateId}; continuing other candidates", target.CandidateId);
            }
            finally { db.ChangeTracker.Clear(); }
        }
    }
}
