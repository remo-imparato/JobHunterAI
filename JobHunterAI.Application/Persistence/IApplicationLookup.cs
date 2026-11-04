using JobHunterAI.Domain;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Application.Persistence;

public interface IApplicationLookup
{
	Task<IReadOnlyList<DomainApplication>> ListByCandidateAsync(
		Guid candidateProfileId, int skip, int take, CancellationToken cancellationToken = default);

	Task<DomainApplication?> FindByCandidateAndJobAsync(
		Guid candidateProfileId, Guid jobPostingId, CancellationToken cancellationToken = default);
}
