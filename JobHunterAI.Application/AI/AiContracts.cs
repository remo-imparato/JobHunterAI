using JobHunterAI.Domain;

namespace JobHunterAI.Application.AI;

public sealed record JobMatchResult(
	int MatchScore,
	IReadOnlyList<string> Strengths,
	IReadOnlyList<string> Weaknesses,
	IReadOnlyList<string> RecommendedKeywords,
	IReadOnlyList<string> RiskFactors);

public sealed record TailoredResumeResult(string ContentMarkdown, IReadOnlyList<string> KeywordsUsed);

public sealed record CoverLetterResult(string ContentMarkdown);

public enum CoverLetterTone
{
	Professional,
	Executive,
	Warm,
	Direct
}

public sealed record ApplicationQuestion(string Question, int? MaxLength = null);

public sealed record SuggestedAnswer(string Question, string Answer, bool NeedsUserInput);

public interface IJobMatchingService
{
	Task<JobMatchResult> MatchAsync(CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken = default);
}

public interface IResumeTailoringService
{
	Task<TailoredResumeResult> TailorAsync(
		CandidateProfile candidate, JobPosting job, string baseResumeMarkdown, CancellationToken cancellationToken = default);
}

public interface ICoverLetterService
{
	Task<CoverLetterResult> WriteAsync(
		CandidateProfile candidate, JobPosting job, Company company, CoverLetterTone tone, CancellationToken cancellationToken = default);
}

public interface IApplicationQuestionService
{
	Task<IReadOnlyList<SuggestedAnswer>> SuggestAnswersAsync(
		CandidateProfile candidate, JobPosting job, IReadOnlyList<ApplicationQuestion> questions, CancellationToken cancellationToken = default);
}
