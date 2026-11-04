using JobHunterAI.Domain;

namespace JobHunterAI.Application.Generation;

public sealed record ResumeDraft(string ContentMarkdown);
public sealed record CoverLetterDraft(string ContentMarkdown);

public interface IResumeDraftingService
{
	Task<ResumeDraft> DraftAsync(
		CandidateProfile candidate, JobPosting job, ResumeVersion baseVersion, CancellationToken cancellationToken);
}

public interface ICoverLetterDraftingService
{
	Task<CoverLetterDraft> DraftAsync(
		CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken);
}
