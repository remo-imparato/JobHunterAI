using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Jobs;

public sealed record GetJobsQuery(int Skip, int Take) : IRequest<Result<IReadOnlyList<JobDto>>>;

public sealed class GetJobsValidator : AbstractValidator<GetJobsQuery>
{
	public GetJobsValidator()
	{
		RuleFor(x => x.Skip).GreaterThanOrEqualTo(0);
		RuleFor(x => x.Take).InclusiveBetween(1, 100);
	}
}

public sealed class GetJobsHandler(IRepository<JobPosting> jobs, IMapper mapper)
	: IRequestHandler<GetJobsQuery, Result<IReadOnlyList<JobDto>>>
{
	public async Task<Result<IReadOnlyList<JobDto>>> Handle(GetJobsQuery request, CancellationToken cancellationToken)
	{
		IReadOnlyList<JobPosting> postings = await jobs.ListAsync(request.Skip, request.Take, cancellationToken);
		List<JobDto> result = new(postings.Count);
		foreach (JobPosting posting in postings)
		{
			result.Add(mapper.Map<JobDto>(posting));
		}

		return Result<IReadOnlyList<JobDto>>.Success(result);
	}
}
