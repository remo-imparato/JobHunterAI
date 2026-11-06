using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure;

public sealed class PackageStore(JobHunterDbContext db) : IPackageStore
{
    public Task<ApplicationPackage?> FindAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token) =>
        db.Set<ApplicationPackage>().SingleOrDefaultAsync(x => x.CandidateProfileId == candidateId && x.JobPostingId == jobId && x.ResumeId == resumeId, token);
    public Task<ApplicationPackage?> GetAsync(Guid candidateId, Guid packageId, CancellationToken token) =>
        db.Set<ApplicationPackage>().SingleOrDefaultAsync(x => x.CandidateProfileId == candidateId && x.Id == packageId, token);
    public Task<bool> HasMatchAsync(Guid candidateId, Guid jobId, CancellationToken token) => db.Set<JobDiscoveryResult>().AnyAsync(x =>
        x.JobPostingId == jobId && db.Set<JobDiscoveryRun>().Any(run => run.Id == x.RunId && run.CandidateProfileId == candidateId &&
            (run.Status == DiscoveryStatus.Completed || run.Status == DiscoveryStatus.CompletedWithWarnings || run.Status == DiscoveryStatus.Cancelled)), token);
    public async Task CreateAsync(ApplicationPackage package, CancellationToken token)
    {
        db.Set<ApplicationPackage>().Add(package);
        await db.SaveChangesAsync(token);
    }
    public async Task<bool> AcquireAsync(ApplicationPackage package, Guid leaseId, DateTimeOffset expiresAt, CancellationToken token)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int changed = await db.Set<ApplicationPackage>().Where(x => x.Id == package.Id && x.CandidateProfileId == package.CandidateProfileId &&
            x.Status != PackageStatus.Completed && (x.Status != PackageStatus.Generating || x.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PackageStatus.Generating)
                .SetProperty(x => x.LeaseId, leaseId).SetProperty(x => x.LeaseExpiresAt, expiresAt).SetProperty(x => x.FailureCode, (string?)null), token);
        if (changed == 0) return false;
        await db.Entry(package).ReloadAsync(token);
        return package.LeaseId == leaseId;
    }
    public async Task SaveAsync(ApplicationPackage package, CancellationToken token) => await db.SaveChangesAsync(token);
    public async Task FailAsync(Guid packageId, Guid candidateId, Guid leaseId, string code, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        string safe = code is "AiUnavailable" or "Cancelled" ? code : "GenerationFailed";
        await db.Set<ApplicationPackage>().Where(x => x.Id == packageId && x.CandidateProfileId == candidateId &&
            x.LeaseId == leaseId && x.Status == PackageStatus.Generating)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, PackageStatus.Failed)
                .SetProperty(x => x.FailureCode, safe).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), token);
    }
}
