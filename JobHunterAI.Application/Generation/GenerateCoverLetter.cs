using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Generation;

public sealed record GenerateCoverLetterCommand(
	Guid Id, Guid CandidateProfileId, Guid JobPostingId, DateTimeOffset CreatedAt)
	: IRequest<Result<CoverLetterDto>>;

public sealed class GenerateCoverLetterValidator : AbstractValidator<GenerateCoverLetterCommand>
{
	public GenerateCoverLetterValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
		RuleFor(x => x.CandidateProfileId).NotEmpty();
		RuleFor(x => x.JobPostingId).NotEmpty();
		RuleFor(x => x.CreatedAt).NotEqual(default(DateTimeOffset));
	}
}

public sealed class GenerateCoverLetterHandler(
	IRepository<CoverLetter> letters, IRepository<CandidateProfile> candidates, IRepository<JobPosting> jobs,
	ICoverLetterDraftingService drafts, IUnitOfWork unitOfWork, IMapper mapper)
	: IRequestHandler<GenerateCoverLetterCommand, Result<CoverLetterDto>>
{
	public async Task<Result<CoverLetterDto>> Handle(GenerateCoverLetterCommand request, CancellationToken cancellationToken)
	{
		CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateProfileId, cancellationToken);
		JobPosting? job = await jobs.GetByIdAsync(request.JobPostingId, cancellationToken);
		if (candidate is null || job is null)
		{
			return Result<CoverLetterDto>.NotFound("Candidate profile or job posting not found.");
		}

		if (await letters.GetByIdAsync(request.Id, cancellationToken) is not null)
		{
			return Result<CoverLetterDto>.Conflict("Cover letter already exists.");
		}

		CoverLetterDraft draft = await drafts.DraftAsync(candidate, job, cancellationToken);
		if (draft is null || string.IsNullOrWhiteSpace(draft.ContentMarkdown))
		{
			return Result<CoverLetterDto>.Conflict("No cover letter draft was generated.");
		}

		CoverLetter letter = new(request.Id, candidate.Id, job.Id, draft.ContentMarkdown, request.CreatedAt);
		letters.Add(letter);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<CoverLetterDto>.Success(mapper.Map<CoverLetterDto>(letter));
	}
}
