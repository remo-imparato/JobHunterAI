using JobHunterAI.Application.AI;
using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;

namespace JobHunterAI.AI;

/// <summary>Drafts a tailored resume from the base resume version using the typed AI tailoring service.</summary>
public sealed class AiResumeDraftingService(IResumeTailoringService tailoring) : IResumeDraftingService
{
    public async Task<ResumeDraft> DraftAsync(
        CandidateProfile candidate, JobPosting job, ResumeVersion baseVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(baseVersion);
        TailoredResumeResult result = await tailoring.TailorAsync(candidate, job, baseVersion.ContentMarkdown, cancellationToken);
        return new ResumeDraft(result.ContentMarkdown);
    }
}
