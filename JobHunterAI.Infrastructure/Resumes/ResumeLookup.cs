using FluentValidation;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure.Resumes;

public sealed class ResumeLookup(JobHunterDbContext db) : IResumeLookup
{
    public async Task<IReadOnlyList<ResumeSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token)
    {
        await new ListCandidateResumesValidator().ValidateAndThrowAsync(new ListCandidateResumesQuery(candidateId, page, pageSize), token);
        return await db.Resumes.AsNoTracking().IgnoreAutoIncludes().Where(x => x.CandidateProfileId == candidateId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize)
            .Select(x => new ResumeSummary(x.Id, x.Name,
                x.Versions.Where(v => v.IsSource).Select(v => (Guid?)v.Id).FirstOrDefault(), x.SourceFileName,
                x.Versions.Where(v => v.IsSource).Select(v => (DateTimeOffset?)v.CreatedAt).FirstOrDefault())).ToListAsync(token);
    }

    public Task<Resume?> FindByHashAsync(Guid candidateId, string hash, CancellationToken token) =>
        db.Resumes.AsSplitQuery().SingleOrDefaultAsync(x => x.CandidateProfileId == candidateId && x.SourceSha256 == hash, token);
}
