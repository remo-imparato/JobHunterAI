using JobHunterAI.Domain;

namespace JobHunterAI.Application.Jobs;

public interface IJobProvider
{
	string Name { get; }

	Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId, CancellationToken cancellationToken = default);

	Task<JobPosting?> GetJobDetailsAsync(string board, string jobId, Guid companyId, CancellationToken cancellationToken = default);
}
