using AutoMapper;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.UnitTests;

internal static class ApplicationTestDoubles
{
    public static IMapper Mapper => new MapperConfiguration(
        config => config.AddProfile<ApplicationMappingProfile>(), NullLoggerFactory.Instance).CreateMapper();
}

internal sealed class FakeRepository<TEntity>(Func<TEntity, Guid> getId, params TEntity[] initial) : IRepository<TEntity>
    where TEntity : class
{
    public List<TEntity> Items { get; } = [.. initial];

    public Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        foreach (TEntity item in Items)
        {
            if (getId(item) == id)
            {
                return Task.FromResult<TEntity?>(item);
            }
        }

        return Task.FromResult<TEntity?>(null);
    }

    public Task<IReadOnlyList<TEntity>> ListAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        List<TEntity> page = [];
        for (int i = skip; i < Items.Count && page.Count < take; i++)
        {
            page.Add(Items[i]);
        }

        return Task.FromResult<IReadOnlyList<TEntity>>(page);
    }

    public void Add(TEntity entity) => Items.Add(entity);
    public void Remove(TEntity entity) => Items.Remove(entity);
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

internal sealed class FakeJobPostingLookup(FakeRepository<JobPosting> jobs) : IJobPostingLookup
{
    public Task<JobPosting?> FindByProviderAsync(string providerName, string externalId, CancellationToken cancellationToken = default)
    {
        foreach (JobPosting job in jobs.Items)
        {
            if (job.ProviderName == providerName && job.ExternalId == externalId)
            {
                return Task.FromResult<JobPosting?>(job);
            }
        }

        return Task.FromResult<JobPosting?>(null);
    }
}

internal sealed class FakeApplicationLookup(FakeRepository<DomainApplication> applications) : IApplicationLookup
{
    public Task<IReadOnlyList<DomainApplication>> ListByCandidateAsync(
        Guid candidateProfileId, int skip, int take, CancellationToken cancellationToken = default)
    {
        List<DomainApplication> page = [];
        int matched = 0;
        foreach (DomainApplication application in applications.Items)
        {
            if (application.CandidateProfileId != candidateProfileId)
            {
                continue;
            }

            if (matched++ >= skip && page.Count < take)
            {
                page.Add(application);
            }
        }

        return Task.FromResult<IReadOnlyList<DomainApplication>>(page);
    }

    public Task<DomainApplication?> FindByCandidateAndJobAsync(
        Guid candidateProfileId, Guid jobPostingId, CancellationToken cancellationToken = default)
    {
        foreach (DomainApplication application in applications.Items)
        {
            if (application.CandidateProfileId == candidateProfileId && application.JobPostingId == jobPostingId)
            {
                return Task.FromResult<DomainApplication?>(application);
            }
        }

        return Task.FromResult<DomainApplication?>(null);
    }
}

internal sealed class FakeResumeDraftingService : IResumeDraftingService
{
    public ResumeVersion? ReceivedBaseVersion { get; private set; }

    public Task<ResumeDraft> DraftAsync(
        CandidateProfile candidate, JobPosting job, ResumeVersion baseVersion, CancellationToken cancellationToken)
    {
        ReceivedBaseVersion = baseVersion;
        return Task.FromResult(new ResumeDraft(baseVersion.ContentMarkdown + "\n## Tailored for " + job.Title));
    }
}

internal sealed class FakeCoverLetterDraftingService : ICoverLetterDraftingService
{
    public Task<CoverLetterDraft> DraftAsync(CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken) =>
        Task.FromResult(new CoverLetterDraft("Dear " + job.Title + " team"));
}
