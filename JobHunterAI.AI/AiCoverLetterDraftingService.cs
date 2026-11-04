using JobHunterAI.Application.AI;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.Extensions.Options;

namespace JobHunterAI.AI;

/// <summary>Drafts a cover letter for a job's company using the typed AI cover letter service and the configured tone.</summary>
public sealed class AiCoverLetterDraftingService(
    ICoverLetterService letters, IRepository<Company> companies, IOptions<AiOptions> options) : ICoverLetterDraftingService
{
    public async Task<CoverLetterDraft> DraftAsync(CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(job);
        Company company = await companies.GetByIdAsync(job.CompanyId, cancellationToken)
            ?? throw new InvalidOperationException("The company for the job posting was not found.");
        CoverLetterResult result = await letters.WriteAsync(candidate, job, company, options.Value.CoverLetterTone, cancellationToken);
        return new CoverLetterDraft(result.ContentMarkdown);
    }
}
