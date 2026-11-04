namespace JobHunterAI.Application.Dashboard;

public enum DashboardResource
{
	Jobs,
	Applications,
	ResumeVersions,
	CoverLetters,
	AutomationQueue
}

public sealed record DashboardQuery(Guid CandidateId, int Page = 0, int PageSize = 10, string? Search = null, string? Filter = null)
{
	public void Validate(DashboardResource? resource = null)
	{
		if (CandidateId == Guid.Empty || Page < 0 || Page > 100000 || PageSize < 1 || PageSize > 100 ||
			Search?.Length > 200 || Filter?.Length > 100)
		{
			throw new ArgumentException("A candidate ID, page 0–100000, page size 1–100, and bounded search/filter are required.");
		}

		if (!string.IsNullOrEmpty(Filter) && resource is DashboardResource.Applications &&
			(!Enum.TryParse<Domain.ApplicationStatus>(Filter, out var status) || !Enum.IsDefined(status)))
		{
			throw new ArgumentException("Invalid application status filter.");
		}

		if (!string.IsNullOrEmpty(Filter) && resource is DashboardResource.AutomationQueue &&
			(!Enum.TryParse<Domain.AgentExecutionStatus>(Filter, out var execution) || !Enum.IsDefined(execution)))
		{
			throw new ArgumentException("Invalid execution status filter.");
		}

		if (!string.IsNullOrEmpty(Filter) && resource is DashboardResource.ResumeVersions && Filter is not ("Base" or "Tailored"))
		{
			throw new ArgumentException("Invalid resume version filter.");
		}
	}
}

public sealed record DashboardRow(Guid Id, string Title, string Detail, string Status, DateTimeOffset? Date);
public sealed record DashboardPage(IReadOnlyList<DashboardRow> Items, int TotalCount);
public sealed record DashboardMetric(string Label, int Count);
public sealed record DashboardSummary(int Jobs, int Applications, int ResumeVersions, int CoverLetters,
	int RunningExecutions, IReadOnlyList<DashboardMetric> ApplicationStatuses);

public interface IDashboardReader
{
	Task<DashboardPage> ReadAsync(DashboardResource resource, DashboardQuery query, CancellationToken cancellationToken = default);
	Task<DashboardSummary> SummarizeAsync(Guid candidateId, CancellationToken cancellationToken = default);
}
