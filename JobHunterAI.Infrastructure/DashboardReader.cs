using JobHunterAI.Application.Dashboard;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure;

public sealed class DashboardReader(JobHunterDbContext db) : IDashboardReader
{
    public async Task<DashboardPage> ReadAsync(DashboardResource resource, DashboardQuery query, CancellationToken cancellationToken = default)
    {
        query.Validate(resource);
        string search = query.Search?.Trim() ?? string.Empty;
        string filter = query.Filter?.Trim() ?? string.Empty;
        int skip = query.Page * query.PageSize;
        switch (resource)
        {
            case DashboardResource.Jobs:
            {
                var jobs = db.JobPostings.AsNoTracking().Where(j =>
                    (search == "" || j.Title.Contains(search) || j.Description.Contains(search)) &&
                    (filter == "" || j.ProviderName == filter));
                int count = await jobs.CountAsync(cancellationToken);
                var rows = await jobs.OrderByDescending(j => j.PostedAt).ThenBy(j => j.Id).Skip(skip).Take(query.PageSize)
                    .Select(j => new DashboardRow(j.Id, j.Title, j.ProviderName, j.Location.IsRemote ? "Remote" : "On-site", j.PostedAt))
                    .ToListAsync(cancellationToken);
                return new DashboardPage(rows, count);
            }
            case DashboardResource.Applications:
            {
                var applications = from a in db.Applications.AsNoTracking()
                    join j in db.JobPostings on a.JobPostingId equals j.Id
                    where a.CandidateProfileId == query.CandidateId && (search == "" || j.Title.Contains(search))
                    select new { Application = a, Job = j };
                if (filter != "")
                {
                    ApplicationStatus status = Enum.Parse<ApplicationStatus>(filter);
                    applications = applications.Where(x => x.Application.Status == status);
                }

                int count = await applications.CountAsync(cancellationToken);
                var rows = await applications.OrderByDescending(x => x.Application.CreatedAt).ThenBy(x => x.Application.Id)
                    .Skip(skip).Take(query.PageSize).Select(x => new DashboardRow(x.Application.Id, x.Job.Title,
                        x.Job.ProviderName, x.Application.Status.ToString(), x.Application.CreatedAt)).ToListAsync(cancellationToken);
                return new DashboardPage(rows, count);
            }
            case DashboardResource.ResumeVersions:
            {
                var versions = from r in db.Resumes.AsNoTracking()
                    from v in r.Versions
                    where r.CandidateProfileId == query.CandidateId && (search == "" || r.Name.Contains(search)) &&
                        (filter == "" || (filter == "Tailored" ? v.JobPostingId != null : v.JobPostingId == null))
                    select new { Resume = r, Version = v };
                int count = await versions.CountAsync(cancellationToken);
                var rows = await versions.OrderByDescending(x => x.Version.CreatedAt).ThenBy(x => x.Version.Id)
                    .Skip(skip).Take(query.PageSize).Select(x => new DashboardRow(x.Version.Id, x.Resume.Name,
                        "Version " + x.Version.VersionNumber, x.Version.JobPostingId == null ? "Base" : "Tailored", x.Version.CreatedAt))
                    .ToListAsync(cancellationToken);
                return new DashboardPage(rows, count);
            }
            case DashboardResource.CoverLetters:
            {
                var letters = from c in db.CoverLetters.AsNoTracking()
                    join j in db.JobPostings on c.JobPostingId equals j.Id
                    where c.CandidateProfileId == query.CandidateId && (search == "" || j.Title.Contains(search)) &&
                        (filter == "" || j.ProviderName == filter)
                    select new { Letter = c, Job = j };
                int count = await letters.CountAsync(cancellationToken);
                var rows = await letters.OrderByDescending(x => x.Letter.CreatedAt).ThenBy(x => x.Letter.Id).Skip(skip).Take(query.PageSize)
                    .Select(x => new DashboardRow(x.Letter.Id, x.Job.Title, x.Job.ProviderName, "Generated", x.Letter.CreatedAt))
                    .ToListAsync(cancellationToken);
                return new DashboardPage(rows, count);
            }
            case DashboardResource.AutomationQueue:
            {
                var executions = db.AgentExecutions.AsNoTracking().Where(x => x.CandidateProfileId == query.CandidateId &&
                    (search == "" || x.AgentName.Contains(search)));
                if (filter != "")
                {
                    AgentExecutionStatus status = Enum.Parse<AgentExecutionStatus>(filter);
                    executions = executions.Where(x => x.Status == status);
                }

                int count = await executions.CountAsync(cancellationToken);
                var rows = await executions.OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Skip(skip).Take(query.PageSize)
                    .Select(x => new DashboardRow(x.Id, x.AgentName, x.FailureCode ?? "", x.Status.ToString(), x.StartedAt))
                    .ToListAsync(cancellationToken);
                return new DashboardPage(rows, count);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(resource));
        }
    }

    public async Task<DashboardSummary> SummarizeAsync(Guid candidateId, CancellationToken cancellationToken = default)
    {
        new DashboardQuery(candidateId).Validate();
        int jobs = await db.JobPostings.CountAsync(cancellationToken);
        int applications = await db.Applications.CountAsync(x => x.CandidateProfileId == candidateId, cancellationToken);
        int resumes = await db.Resumes.Where(x => x.CandidateProfileId == candidateId).SelectMany(x => x.Versions).CountAsync(cancellationToken);
        int letters = await db.CoverLetters.CountAsync(x => x.CandidateProfileId == candidateId, cancellationToken);
        int running = await db.AgentExecutions.CountAsync(x => x.CandidateProfileId == candidateId && x.Status == AgentExecutionStatus.Running, cancellationToken);
        var groups = await db.Applications.Where(x => x.CandidateProfileId == candidateId).GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        List<DashboardMetric> statuses = [];
        foreach (var group in groups)
        {
            statuses.Add(new DashboardMetric(group.Status.ToString(), group.Count));
        }

        return new DashboardSummary(jobs, applications, resumes, letters, running, statuses);
    }
}
