using FluentValidation;
using JobHunterAI.Application.AI;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Matching;

public sealed record JobMatchScoreDto(
	Guid Id,
	Guid CandidateProfileId,
	Guid JobPostingId,
	int MatchScore,
	IReadOnlyList<string> Strengths,
	IReadOnlyList<string> Weaknesses,
	IReadOnlyList<string> RecommendedKeywords,
	IReadOnlyList<string> RiskFactors,
	DateTimeOffset ScoredAt);

public sealed record MatchJobCommand(Guid Id, Guid CandidateProfileId, Guid JobPostingId, DateTimeOffset ScoredAt)
	: IRequest<Result<JobMatchScoreDto>>;

public sealed class MatchJobValidator : AbstractValidator<MatchJobCommand>
{
	public MatchJobValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
		RuleFor(x => x.CandidateProfileId).NotEmpty();
		RuleFor(x => x.JobPostingId).NotEmpty();
		RuleFor(x => x.ScoredAt).NotEqual(default(DateTimeOffset));
	}
}

public sealed class MatchJobHandler(
	IRepository<JobMatchScore> scores, IRepository<CandidateProfile> candidates, IRepository<JobPosting> jobs,
	IJobMatchingService matching, IUnitOfWork unitOfWork)
	: IRequestHandler<MatchJobCommand, Result<JobMatchScoreDto>>
{
	public async Task<Result<JobMatchScoreDto>> Handle(MatchJobCommand request, CancellationToken cancellationToken)
	{
		CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateProfileId, cancellationToken);
		JobPosting? job = await jobs.GetByIdAsync(request.JobPostingId, cancellationToken);
		if (candidate is null || job is null)
		{
			return Result<JobMatchScoreDto>.NotFound("Candidate profile or job posting not found.");
		}

		if (await scores.GetByIdAsync(request.Id, cancellationToken) is not null)
		{
			return Result<JobMatchScoreDto>.Conflict("Match score already exists.");
		}

		JobMatchResult match = await matching.MatchAsync(candidate, job, cancellationToken);
		JobMatchScore score = new(request.Id, candidate.Id, job.Id, match.MatchScore, match.Strengths, match.Weaknesses,
			match.RecommendedKeywords, match.RiskFactors, request.ScoredAt);
		scores.Add(score);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<JobMatchScoreDto>.Success(new JobMatchScoreDto(score.Id, score.CandidateProfileId, score.JobPostingId,
			score.Score, score.Strengths, score.Weaknesses, score.RecommendedKeywords, score.RiskFactors, score.ScoredAt));
	}
}
