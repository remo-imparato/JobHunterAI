using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure;

internal sealed class JobPostingLookup(JobHunterDbContext context) : IJobPostingLookup
{
    public Task<JobPosting?> FindByProviderAsync(
        string providerName, string externalId, CancellationToken cancellationToken = default) =>
        context.JobPostings.SingleOrDefaultAsync(
            posting => posting.ProviderName == providerName && posting.ExternalId == externalId,
            cancellationToken);
}
