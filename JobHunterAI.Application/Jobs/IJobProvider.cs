using JobHunterAI.Domain;

namespace JobHunterAI.Application.Jobs;

public sealed record JobProviderSearchCriteria(string[] Terms, string[] Locations)
{
	public static JobProviderSearchCriteria From(CandidateSearchCriteria criteria) => new(
		criteria.JobTitles.Concat(criteria.RequiredKeywords).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
		criteria.Locations);
}

public interface IJobProvider
{
	string Name { get; }

	Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId,
		JobProviderSearchCriteria? criteria = null, CancellationToken cancellationToken = default);

	Task<JobPosting?> GetJobDetailsAsync(string board, string jobId, Guid companyId, CancellationToken cancellationToken = default);
}
