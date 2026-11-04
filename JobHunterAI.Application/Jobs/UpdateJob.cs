using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Jobs;

public sealed record UpdateJobCommand(
	Guid Id, string Title, string Description, string? City, string? Country, bool IsRemote, string ApplicationUrl)
	: IRequest<Result<JobDto>>;

public sealed class UpdateJobValidator : AbstractValidator<UpdateJobCommand>
{
	public UpdateJobValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
		RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
		RuleFor(x => x.Description).NotEmpty();
		RuleFor(x => x.ApplicationUrl).NotEmpty().MaximumLength(2048).Must(JobInputRules.IsHttpUrl);
		RuleFor(x => x).Must(x => JobInputRules.HasValidLocation(x.City, x.Country, x.IsRemote))
			.WithMessage("An on-site job requires a city and country; a city requires a country.");
		RuleFor(x => x.City).MaximumLength(200);
		RuleFor(x => x.Country).MaximumLength(200);
	}
}

public sealed class UpdateJobHandler(IRepository<JobPosting> jobs, IUnitOfWork unitOfWork, IMapper mapper)
	: IRequestHandler<UpdateJobCommand, Result<JobDto>>
{
	public async Task<Result<JobDto>> Handle(UpdateJobCommand request, CancellationToken cancellationToken)
	{
		JobPosting? job = await jobs.GetByIdAsync(request.Id, cancellationToken);
		if (job is null)
		{
			return Result<JobDto>.NotFound("Job posting not found.");
		}

		job.UpdateDetails(request.Title, request.Description,
			new JobLocation(request.City, request.Country, request.IsRemote), request.ApplicationUrl);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<JobDto>.Success(mapper.Map<JobDto>(job));
	}
}
