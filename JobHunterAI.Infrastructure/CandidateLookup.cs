using FluentValidation;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Infrastructure;

public sealed class CandidateLookup(JobHunterDbContext db) : ICandidateLookup
{
    public async Task<CandidatePage> SearchAsync(SearchCandidatesQuery query, CancellationToken token)
    {
        await new SearchCandidatesValidator().ValidateAndThrowAsync(query, token);
        IQueryable<CandidateProfile> profiles = db.CandidateProfiles.AsNoTracking().IgnoreAutoIncludes();
        string search = query.Search?.Trim().ToLowerInvariant() ?? string.Empty;
        if (search.Length > 0)
        {
            profiles = profiles.Where(x => x.FullName.ToLower().Contains(search) || x.Username.Contains(search));
        }

        int count = await profiles.CountAsync(token);
        List<CandidateDto> items = await profiles.OrderBy(x => x.FullName).ThenBy(x => x.Username).ThenBy(x => x.Id)
            .Skip(query.Page * query.PageSize).Take(query.PageSize)
            .Select(x => new CandidateDto(x.Id, x.FullName, x.Username, x.Email.Value, x.Headline)).ToListAsync(token);
        return new CandidatePage(items, count);
    }

    public Task<bool> UsernameExistsAsync(string username, Guid exceptId, CancellationToken token)
    {
        string normalized = CandidateProfile.NormalizeUsername(username);
        return db.CandidateProfiles.AnyAsync(x => x.Username == normalized && x.Id != exceptId, token);
    }

    public async Task<bool> HasDependentsAsync(Guid candidateId, CancellationToken token) =>
        await db.Resumes.AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.CoverLetters.AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.Applications.AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.AgentExecutions.AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.Set<JobMatchScore>().AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.Set<JobDiscoveryRun>().AnyAsync(x => x.CandidateProfileId == candidateId, token) ||
        await db.Set<ApplicationPackage>().AnyAsync(x => x.CandidateProfileId == candidateId, token);
}
