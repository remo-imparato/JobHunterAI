using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Application.Applications;

public sealed record CreateApplicationCommand(
	Guid Id, Guid CandidateProfileId, Guid JobPostingId, DateTimeOffset CreatedAt)
	: IRequest<Result<ApplicationDto>>;

public sealed class CreateApplicationValidator : AbstractValidator<CreateApplicationCommand>
{
	public CreateApplicationValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
		RuleFor(x => x.CandidateProfileId).NotEmpty();
		RuleFor(x => x.JobPostingId).NotEmpty();
		RuleFor(x => x.CreatedAt).NotEqual(default(DateTimeOffset));
	}
}

public sealed class CreateApplicationHandler(
	IRepository<DomainApplication> applications, IRepository<CandidateProfile> candidates,
	IRepository<JobPosting> jobs, IApplicationLookup lookup, IUnitOfWork unitOfWork, IMapper mapper)
	: IRequestHandler<CreateApplicationCommand, Result<ApplicationDto>>
{
	public async Task<Result<ApplicationDto>> Handle(CreateApplicationCommand request, CancellationToken cancellationToken)
	{
		if (await candidates.GetByIdAsync(request.CandidateProfileId, cancellationToken) is null ||
			await jobs.GetByIdAsync(request.JobPostingId, cancellationToken) is null)
		{
			return Result<ApplicationDto>.NotFound("Candidate profile or job posting not found.");
		}

		if (await applications.GetByIdAsync(request.Id, cancellationToken) is not null ||
			await lookup.FindByCandidateAndJobAsync(request.CandidateProfileId, request.JobPostingId, cancellationToken) is not null)
		{
			return Result<ApplicationDto>.Conflict("Application already exists for this candidate and job.");
		}

		DomainApplication application = new(request.Id, request.CandidateProfileId, request.JobPostingId, request.CreatedAt);
		applications.Add(application);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<ApplicationDto>.Success(mapper.Map<ApplicationDto>(application));
	}
}
