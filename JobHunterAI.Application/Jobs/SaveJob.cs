using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Jobs;

public sealed record SaveJobCommand(
	Guid Id, Guid CompanyId, string ProviderName, string ExternalId, string Title, string Description,
	string? City, string? Country, bool IsRemote, string ApplicationUrl, DateTimeOffset? PostedAt)
	: IRequest<Result<JobDto>>;

public sealed class SaveJobValidator : AbstractValidator<SaveJobCommand>
{
	public SaveJobValidator()
	{
		RuleFor(x => x.Id).NotEmpty();
		RuleFor(x => x.CompanyId).NotEmpty();
		RuleFor(x => x.ProviderName).NotEmpty().MaximumLength(100);
		RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(300);
		RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
		RuleFor(x => x.Description).NotEmpty();
		RuleFor(x => x.ApplicationUrl).NotEmpty().MaximumLength(2048).Must(JobInputRules.IsHttpUrl);
		RuleFor(x => x).Must(x => JobInputRules.HasValidLocation(x.City, x.Country, x.IsRemote))
			.WithMessage("An on-site job requires a city and country; a city requires a country.");
		RuleFor(x => x.City).MaximumLength(200);
		RuleFor(x => x.Country).MaximumLength(200);
		RuleFor(x => x.PostedAt).Must(x => !x.HasValue || x.Value != default);
	}
}

public sealed class SaveJobHandler(
	IRepository<JobPosting> jobs, IRepository<Company> companies, IJobPostingLookup lookup,
	IUnitOfWork unitOfWork, IMapper mapper) : IRequestHandler<SaveJobCommand, Result<JobDto>>
{
	public async Task<Result<JobDto>> Handle(SaveJobCommand request, CancellationToken cancellationToken)
	{
		if (await companies.GetByIdAsync(request.CompanyId, cancellationToken) is null)
		{
			return Result<JobDto>.NotFound("Company not found.");
		}

		if (await jobs.GetByIdAsync(request.Id, cancellationToken) is not null ||
			await lookup.FindByProviderAsync(request.ProviderName.Trim(), request.ExternalId.Trim(), cancellationToken) is not null)
		{
			return Result<JobDto>.Conflict("Job posting already exists.");
		}

		JobPosting job = new(request.Id, request.CompanyId, request.ProviderName, request.ExternalId,
			request.Title, request.Description, new JobLocation(request.City, request.Country, request.IsRemote),
			request.ApplicationUrl, request.PostedAt);
		jobs.Add(job);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		return Result<JobDto>.Success(mapper.Map<JobDto>(job));
	}
}
