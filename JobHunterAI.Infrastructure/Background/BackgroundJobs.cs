using JobHunterAI.Application.Dashboard;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Application.Matching;
using JobHunterAI.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Infrastructure.Background;

public sealed class BackgroundJobs(
    JobHunterDbContext db, IEnumerable<IJobProvider> providers, ISender sender, IDashboardReader dashboard,
    IOptions<BackgroundOptions> options, ILogger<BackgroundJobs> logger)
{
    private BackgroundOptions Settings => options.Value;

    public async Task DiscoverJobsAsync(CancellationToken token)
    {
        if (!Settings.Enabled) return;
        foreach (DiscoveryBoard board in Settings.Boards)
        {
            token.ThrowIfCancellationRequested();
            if (!await db.Companies.AnyAsync(x => x.Id == board.CompanyId, token))
                throw new InvalidOperationException("Configured discovery company does not exist.");
            IJobProvider? provider = null;
            foreach (IJobProvider item in providers)
            {
                if (item.Name == board.Provider) { provider = item; break; }
            }

            if (provider is null) throw new InvalidOperationException("Configured discovery provider is unavailable.");
            IReadOnlyList<JobPosting> jobs = await provider.SearchJobsAsync(board.Board, board.CompanyId, token);
            int saved = 0;
            foreach (JobPosting job in jobs)
            {
                token.ThrowIfCancellationRequested();
                if (await db.JobPostings.AnyAsync(x => x.ProviderName == job.ProviderName && x.ExternalId == job.ExternalId, token)) continue;
                var result = await sender.Send(new SaveJobCommand(job.Id, job.CompanyId, job.ProviderName, job.ExternalId,
                    job.Title, job.Description, job.Location.City, job.Location.Country, job.Location.IsRemote,
                    job.ApplicationUrl, job.PostedAt), token);
                if (!result.IsSuccess) throw new InvalidOperationException("Discovered job could not be saved.");
                if (++saved >= Settings.BatchSize) break;
            }

            logger.LogInformation("Discovery provider {Provider} saved {Count} jobs", board.Provider, saved);
        }
    }

    public Task RefreshMatchScoresAsync(CancellationToken token) => ForCandidatesAsync("RefreshMatchScores", async (target, ct) =>
    {
        if (!Settings.EnableAiJobs) return;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddHours(-Settings.ScoreRefreshHours);
        var jobs = await db.JobPostings.AsNoTracking().Where(j => !db.Set<JobMatchScore>().Any(s =>
                s.CandidateProfileId == target.CandidateId && s.JobPostingId == j.Id && s.ScoredAt >= cutoff))
            .OrderByDescending(j => j.PostedAt).ThenBy(j => j.Id).Take(Settings.BatchSize).Select(j => j.Id).ToListAsync(ct);
        foreach (Guid jobId in jobs)
        {
            var result = await sender.Send(new MatchJobCommand(Guid.NewGuid(), target.CandidateId, jobId, DateTimeOffset.UtcNow), ct);
            if (!result.IsSuccess) throw new InvalidOperationException("Scheduled match scoring failed.");
        }
    }, token, requiresAi: true);

    public Task GenerateResumeVariantsAsync(CancellationToken token) => ForCandidatesAsync("GenerateResumeVariants", async (target, ct) =>
    {
        if (target.ResumeId is not Guid resumeId) return;
        Resume? resume = await db.Resumes.SingleOrDefaultAsync(r => r.Id == resumeId && r.CandidateProfileId == target.CandidateId, ct);
        if (resume is null || resume.Versions.Count == 0) throw new InvalidOperationException("Configured resume is missing or has no base version.");
        var jobs = await db.JobPostings.AsNoTracking().Where(j => !db.Resumes.Where(r => r.Id == resumeId)
                .SelectMany(r => r.Versions).Any(v => v.JobPostingId == j.Id))
            .OrderByDescending(j => j.PostedAt).ThenBy(j => j.Id).Take(Settings.BatchSize).Select(j => j.Id).ToListAsync(ct);
        foreach (Guid jobId in jobs)
        {
            var result = await sender.Send(new GenerateResumeCommand(resumeId, jobId, Guid.NewGuid(), DateTimeOffset.UtcNow), ct);
            if (!result.IsSuccess) throw new InvalidOperationException("Scheduled resume generation failed.");
        }
    }, token, requiresAi: true);

    public Task MonitorApplicationsAsync(CancellationToken token) => ForCandidatesAsync("ApplicationStatusMonitoring", async (target, ct) =>
    {
        var statuses = await db.Applications.AsNoTracking().Where(a => a.CandidateProfileId == target.CandidateId)
            .GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        foreach (var status in statuses)
        {
            logger.LogInformation("Recorded application status for candidate {CandidateId}: {Status} = {Count}; no external status check performed",
                target.CandidateId, status.Status, status.Count);
        }
    }, token);

    public Task DailySummaryAsync(CancellationToken token) => ForCandidatesAsync("DailySummary", async (target, ct) =>
    {
        DashboardSummary summary = await dashboard.SummarizeAsync(target.CandidateId, ct);
        logger.LogInformation("Daily summary for candidate {CandidateId}: jobs {Jobs}, applications {Applications}, resume versions {ResumeVersions}, cover letters {CoverLetters}",
            target.CandidateId, summary.Jobs, summary.Applications, summary.ResumeVersions, summary.CoverLetters);
    }, token);

    public Task CleanupLogsAsync(CancellationToken token)
    {
        if (!Settings.Enabled || Settings.LogDirectory is null) return Task.CompletedTask;
        int count = LogRetention.Cleanup(Settings.LogDirectory, DateTime.UtcNow.AddDays(-Settings.LogRetentionDays), Settings.BatchSize, token);
        logger.LogInformation("Log cleanup removed {Count} expired JobHunterAI log files", count);
        return Task.CompletedTask;
    }

    private async Task ForCandidatesAsync(string name, Func<BackgroundCandidate, CancellationToken, Task> action,
        CancellationToken token, bool requiresAi = false)
    {
        if (!Settings.Enabled || (requiresAi && !Settings.EnableAiJobs)) return;
        foreach (BackgroundCandidate target in Settings.Candidates)
        {
            token.ThrowIfCancellationRequested();
            if (!await db.CandidateProfiles.AnyAsync(x => x.Id == target.CandidateId, token))
                throw new InvalidOperationException("Configured background candidate does not exist.");
            AgentExecution execution = new(Guid.NewGuid(), target.CandidateId, name, DateTimeOffset.UtcNow);
            db.AgentExecutions.Add(execution);
            await db.SaveChangesAsync(token);
            try
            {
                await action(target, token);
                execution.Complete(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                // Discard partially modified aggregates before recording the independent execution failure.
                db.ChangeTracker.Clear();
                using CancellationTokenSource failureTimeout = new(TimeSpan.FromSeconds(5));
                try
                {
                    AgentExecution? persisted = await db.AgentExecutions.SingleOrDefaultAsync(x => x.Id == execution.Id, failureTimeout.Token);
                    if (persisted?.Status == AgentExecutionStatus.Running)
                    {
                        persisted.Fail(ex is OperationCanceledException ? "Cancelled" : "BackgroundJobFailed", DateTimeOffset.UtcNow);
                        await db.SaveChangesAsync(failureTimeout.Token);
                    }
                }
                catch (Exception saveError) { logger.LogError(saveError, "Unable to record failed execution {ExecutionId}", execution.Id); }
                throw;
            }
        }
    }
}

public static class LogRetention
{
    public static int Cleanup(string directory, DateTime cutoffUtc, int limit, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory) || limit is < 1 or > 100)
            throw new ArgumentException("An absolute owned log directory and limit 1–100 are required.");
        DirectoryInfo root = new(directory);
        if (!root.Exists) return 0;
        // Do not follow junctions or symlinks, including those in ancestor directories.
        for (DirectoryInfo? ancestor = root; ancestor is not null; ancestor = ancestor.Parent)
        {
            if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0) return 0;
        }

        int removed = 0;
        foreach (FileInfo file in root.EnumerateFiles("jobhunterai-*.log", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LastWriteTimeUtc >= cutoffUtc) continue;
            file.Delete();
            if (++removed >= limit) break;
        }

        return removed;
    }
}
