using JobHunterAI.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Infrastructure;

internal sealed class ApplicationLookup(JobHunterDbContext context) : IApplicationLookup
{
    public async Task<IReadOnlyList<DomainApplication>> ListByCandidateAsync(
        Guid candidateProfileId, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        if (take is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        return await context.Applications.AsSplitQuery()
            .Where(application => application.CandidateProfileId == candidateProfileId)
            .OrderByDescending(application => application.CreatedAt)
            .ThenBy(application => application.Id)
            .Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task<DomainApplication?> FindByCandidateAndJobAsync(
        Guid candidateProfileId, Guid jobPostingId, CancellationToken cancellationToken = default) =>
        context.Applications.AsSplitQuery().SingleOrDefaultAsync(
            application => application.CandidateProfileId == candidateProfileId && application.JobPostingId == jobPostingId,
            cancellationToken);
}
