using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using MediatR;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Application.Applications;

public sealed record GetApplicationsQuery(Guid CandidateProfileId, int Skip, int Take)
	: IRequest<Result<IReadOnlyList<ApplicationDto>>>;

public sealed class GetApplicationsValidator : AbstractValidator<GetApplicationsQuery>
{
	public GetApplicationsValidator()
	{
		RuleFor(x => x.CandidateProfileId).NotEmpty();
		RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
		RuleFor(x => x.Take).InclusiveBetween(1, 100);
	}
}

public sealed class GetApplicationsHandler(IApplicationLookup applications, IMapper mapper)
	: IRequestHandler<GetApplicationsQuery, Result<IReadOnlyList<ApplicationDto>>>
{
	public async Task<Result<IReadOnlyList<ApplicationDto>>> Handle(
		GetApplicationsQuery request, CancellationToken cancellationToken)
	{
		IReadOnlyList<DomainApplication> matches = await applications.ListByCandidateAsync(
			request.CandidateProfileId, request.Skip, request.Take, cancellationToken);
		List<ApplicationDto> result = new(matches.Count);
		foreach (DomainApplication application in matches)
		{
			result.Add(mapper.Map<ApplicationDto>(application));
		}

		return Result<IReadOnlyList<ApplicationDto>>.Success(result);
	}
}
