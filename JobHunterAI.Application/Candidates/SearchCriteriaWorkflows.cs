using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Candidates;

public sealed record SearchCriteriaInput(string[] JobTitles, string[] Locations, WorkArrangement Arrangements,
    bool IncludeUnknownArrangement, string[] RequiredKeywords, string[] ExcludedKeywords)
{
    public static SearchCriteriaInput From(CandidateSearchCriteria criteria) => new(criteria.JobTitles, criteria.Locations,
        criteria.Arrangements, criteria.IncludeUnknownArrangement, criteria.RequiredKeywords, criteria.ExcludedKeywords);
}

public sealed record GetSearchCriteriaQuery(Guid CandidateId) : IRequest<Result<SearchCriteriaInput?>>;
public sealed record SaveSearchCriteriaCommand(Guid CandidateId, SearchCriteriaInput Input) : IRequest<Result<SearchCriteriaInput>>;

public sealed class SearchCriteriaInputValidator : AbstractValidator<SearchCriteriaInput>
{
    public SearchCriteriaInputValidator()
    {
        RuleFor(x => x).Custom((input, context) =>
        {
            try
            {
                _ = new CandidateSearchCriteria(Guid.NewGuid(), input.JobTitles, input.Locations, input.Arrangements,
                    input.IncludeUnknownArrangement, input.RequiredKeywords, input.ExcludedKeywords);
            }
            catch (ArgumentException exception) { context.AddFailure(exception.Message); }
        });
    }
}
public sealed class GetSearchCriteriaValidator : AbstractValidator<GetSearchCriteriaQuery>
{
    public GetSearchCriteriaValidator() => RuleFor(x => x.CandidateId).NotEmpty();
}
public sealed class SaveSearchCriteriaValidator : AbstractValidator<SaveSearchCriteriaCommand>
{
    public SaveSearchCriteriaValidator()
    {
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.Input).NotNull().SetValidator(new SearchCriteriaInputValidator());
    }
}

public sealed class GetSearchCriteriaHandler(IRepository<CandidateProfile> candidates, IRepository<CandidateSearchCriteria> criteria)
    : IRequestHandler<GetSearchCriteriaQuery, Result<SearchCriteriaInput?>>
{
    public async Task<Result<SearchCriteriaInput?>> Handle(GetSearchCriteriaQuery request, CancellationToken token)
    {
        if (await candidates.GetByIdAsync(request.CandidateId, token) is null)
            return Result<SearchCriteriaInput?>.NotFound("Candidate not found.");
        CandidateSearchCriteria? saved = await criteria.GetByIdAsync(request.CandidateId, token);
        return saved is null ? Result<SearchCriteriaInput?>.NotFound("Search criteria have not been saved for this candidate.")
            : Result<SearchCriteriaInput?>.Success(SearchCriteriaInput.From(saved));
    }
}

public sealed class SaveSearchCriteriaHandler(IRepository<CandidateProfile> candidates,
    IRepository<CandidateSearchCriteria> criteria, IUnitOfWork work)
    : IRequestHandler<SaveSearchCriteriaCommand, Result<SearchCriteriaInput>>
{
    public async Task<Result<SearchCriteriaInput>> Handle(SaveSearchCriteriaCommand request, CancellationToken token)
    {
        if (await candidates.GetByIdAsync(request.CandidateId, token) is null)
            return Result<SearchCriteriaInput>.NotFound("Candidate not found.");
        CandidateSearchCriteria? saved = await criteria.GetByIdAsync(request.CandidateId, token);
        SearchCriteriaInput input = request.Input;
        if (saved is null)
        {
            saved = new(request.CandidateId, input.JobTitles, input.Locations, input.Arrangements,
                input.IncludeUnknownArrangement, input.RequiredKeywords, input.ExcludedKeywords);
            criteria.Add(saved);
        }
        else saved.Update(input.JobTitles, input.Locations, input.Arrangements, input.IncludeUnknownArrangement, input.RequiredKeywords, input.ExcludedKeywords);
        await work.SaveChangesAsync(token);
        return Result<SearchCriteriaInput>.Success(SearchCriteriaInput.From(saved));
    }
}
