using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Generation;

public sealed record GenerateResumeCommand(Guid ResumeId, Guid JobPostingId, Guid VersionId, DateTimeOffset CreatedAt)
	: IRequest<Result<ResumeVersionDto>>;

public sealed class GenerateResumeValidator : AbstractValidator<GenerateResumeCommand>
{
	public GenerateResumeValidator()
	{
		RuleFor(x => x.ResumeId).NotEmpty();
		RuleFor(x => x.JobPostingId).NotEmpty();
		RuleFor(x => x.VersionId).NotEmpty();
		RuleFor(x => x.CreatedAt).NotEqual(default(DateTimeOffset));
	}
}

public sealed class GenerateResumeHandler(
	IRepository<Resume> resumes, IRepository<CandidateProfile> candidates, IRepository<JobPosting> jobs,
	IResumeDraftingService drafts, IUnitOfWork unitOfWork, IMapper mapper)
	: IRequestHandler<GenerateResumeCommand, Result<ResumeVersionDto>>
{
	public async Task<Result<ResumeVersionDto>> Handle(GenerateResumeCommand request, CancellationToken cancellationToken)
	{
		Resume? resume = await resumes.GetByIdAsync(request.ResumeId, cancellationToken);
		if (resume is null)
		{
			return Result<ResumeVersionDto>.NotFound("Resume not found.");
		}

		CandidateProfile? candidate = await candidates.GetByIdAsync(resume.CandidateProfileId, cancellationToken);
		JobPosting? job = await jobs.GetByIdAsync(request.JobPostingId, cancellationToken);
		if (candidate is null || job is null)
		{
			return Result<ResumeVersionDto>.NotFound("Candidate profile or job posting not found.");
		}

		ResumeVersion? baseVersion = null;
		foreach (ResumeVersion version in resume.Versions)
		{
			if (version.Id == request.VersionId)
			{
				return Result<ResumeVersionDto>.Conflict("Resume version already exists.");
			}

			if (baseVersion is null || version.VersionNumber > baseVersion.VersionNumber)
			{
				baseVersion = version;
			}
		}

		if (baseVersion is null)
		{
			return Result<ResumeVersionDto>.Conflict("A base resume version is required.");
		}

		ResumeDraft draft = await drafts.DraftAsync(candidate, job, baseVersion, cancellationToken);
		if (draft is null || string.IsNullOrWhiteSpace(draft.ContentMarkdown))
		{
			return Result<ResumeVersionDto>.Conflict("No resume draft was generated.");
		}

		ResumeVersion tailored = resume.AddVersion(request.VersionId, draft.ContentMarkdown, request.CreatedAt, job.Id);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<ResumeVersionDto>.Success(mapper.Map<ResumeVersionDto>(tailored));
	}
}
