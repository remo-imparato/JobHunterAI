using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;

namespace JobHunterAI.AI;

public sealed class UnavailableResumeDraftingService : IResumeDraftingService
{
    public Task<ResumeDraft> DraftAsync(
        CandidateProfile candidate, JobPosting job, ResumeVersion baseVersion, CancellationToken cancellationToken) =>
        Task.FromException<ResumeDraft>(new NotSupportedException("Resume drafting is not configured."));
}

public sealed class UnavailableCoverLetterDraftingService : ICoverLetterDraftingService
{
    public Task<CoverLetterDraft> DraftAsync(CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken) =>
        Task.FromException<CoverLetterDraft>(new NotSupportedException("Cover letter drafting is not configured."));
}
