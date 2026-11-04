using JobHunterAI.Domain;

namespace JobHunterAI.Application.Persistence;

public interface IJobPostingLookup
{
	Task<JobPosting?> FindByProviderAsync(
		string providerName, string externalId, CancellationToken cancellationToken = default);
}
