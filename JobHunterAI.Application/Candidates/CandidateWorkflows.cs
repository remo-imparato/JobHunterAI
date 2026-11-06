using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Candidates;

public sealed record CandidateDto(Guid Id, string FullName, string Username, string Email, string Headline)
{
    public static CandidateDto From(CandidateProfile profile) =>
        new(profile.Id, profile.FullName, profile.Username, profile.Email.Value, profile.Headline);
}

public sealed record CandidatePage(IReadOnlyList<CandidateDto> Items, int TotalCount);
public sealed record CandidateInput(string FullName, string Username, string Email, string Headline);
public sealed record SearchCandidatesQuery(string? Search = null, int Page = 0, int PageSize = 10)
    : IRequest<Result<CandidatePage>>;
public sealed record GetCandidateQuery(Guid Id) : IRequest<Result<CandidateDto>>;
public sealed record CreateCandidateCommand(CandidateInput Input) : IRequest<Result<CandidateDto>>;
public sealed record UpdateCandidateCommand(Guid Id, CandidateInput Input) : IRequest<Result<CandidateDto>>;
public sealed record DeleteCandidateCommand(Guid Id) : IRequest<Result<bool>>;

public interface ICandidateLookup
{
    Task<CandidatePage> SearchAsync(SearchCandidatesQuery query, CancellationToken token);
    Task<bool> UsernameExistsAsync(string username, Guid exceptId, CancellationToken token);
    Task<bool> HasDependentsAsync(Guid candidateId, CancellationToken token);
}

public sealed class CandidateInputValidator : AbstractValidator<CandidateInput>
{
    public CandidateInputValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Username).NotEmpty().Must(IsUsername)
            .WithMessage("Use 1–64 letters, numbers, dots, underscores or hyphens for the username.");
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).Must(IsEmail)
            .WithMessage("Enter a valid email address.");
        RuleFor(x => x.Headline).NotEmpty().MaximumLength(300);
    }

    private static bool IsUsername(string? value)
    {
        try { CandidateProfile.NormalizeUsername(value!); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsEmail(string? value)
    {
        try { _ = new EmailAddress(value!); return true; }
        catch (ArgumentException) { return false; }
    }
}

public sealed class SearchCandidatesValidator : AbstractValidator<SearchCandidatesQuery>
{
    public SearchCandidatesValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Page).InclusiveBetween(0, 100000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetCandidateValidator : AbstractValidator<GetCandidateQuery>
{
    public GetCandidateValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class CreateCandidateValidator : AbstractValidator<CreateCandidateCommand>
{
    public CreateCandidateValidator() => RuleFor(x => x.Input).NotNull().SetValidator(new CandidateInputValidator());
}

public sealed class UpdateCandidateValidator : AbstractValidator<UpdateCandidateCommand>
{
    public UpdateCandidateValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Input).NotNull().SetValidator(new CandidateInputValidator());
    }
}

public sealed class DeleteCandidateValidator : AbstractValidator<DeleteCandidateCommand>
{
    public DeleteCandidateValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SearchCandidatesHandler(ICandidateLookup lookup)
    : IRequestHandler<SearchCandidatesQuery, Result<CandidatePage>>
{
    public async Task<Result<CandidatePage>> Handle(SearchCandidatesQuery request, CancellationToken token) =>
        Result<CandidatePage>.Success(await lookup.SearchAsync(request, token));
}

public sealed class GetCandidateHandler(IRepository<CandidateProfile> candidates)
    : IRequestHandler<GetCandidateQuery, Result<CandidateDto>>
{
    public async Task<Result<CandidateDto>> Handle(GetCandidateQuery request, CancellationToken token)
    {
        CandidateProfile? profile = await candidates.GetByIdAsync(request.Id, token);
        return profile is null ? Result<CandidateDto>.NotFound("Candidate not found.")
            : Result<CandidateDto>.Success(CandidateDto.From(profile));
    }
}

public sealed class CreateCandidateHandler(
    IRepository<CandidateProfile> candidates, ICandidateLookup lookup, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCandidateCommand, Result<CandidateDto>>
{
    public async Task<Result<CandidateDto>> Handle(CreateCandidateCommand request, CancellationToken token)
    {
        string username = CandidateProfile.NormalizeUsername(request.Input.Username);
        if (await lookup.UsernameExistsAsync(username, Guid.Empty, token))
            return Result<CandidateDto>.Conflict("That username is already in use.");

        CandidateProfile profile = new(Guid.NewGuid(), request.Input.FullName,
            new EmailAddress(request.Input.Email), request.Input.Headline, username);
        candidates.Add(profile);
        await unitOfWork.SaveChangesAsync(token);
        return Result<CandidateDto>.Success(CandidateDto.From(profile));
    }
}

public sealed class UpdateCandidateHandler(
    IRepository<CandidateProfile> candidates, ICandidateLookup lookup, IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCandidateCommand, Result<CandidateDto>>
{
    public async Task<Result<CandidateDto>> Handle(UpdateCandidateCommand request, CancellationToken token)
    {
        CandidateProfile? profile = await candidates.GetByIdAsync(request.Id, token);
        if (profile is null) return Result<CandidateDto>.NotFound("Candidate not found.");
        string username = CandidateProfile.NormalizeUsername(request.Input.Username);
        if (await lookup.UsernameExistsAsync(username, profile.Id, token))
            return Result<CandidateDto>.Conflict("That username is already in use.");

        profile.UpdateProfile(request.Input.FullName, new EmailAddress(request.Input.Email), request.Input.Headline, username);
        await unitOfWork.SaveChangesAsync(token);
        return Result<CandidateDto>.Success(CandidateDto.From(profile));
    }
}

public sealed class DeleteCandidateHandler(
    IRepository<CandidateProfile> candidates, ICandidateLookup lookup, IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCandidateCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteCandidateCommand request, CancellationToken token)
    {
        CandidateProfile? profile = await candidates.GetByIdAsync(request.Id, token);
        if (profile is null) return Result<bool>.NotFound("Candidate not found.");
        if (await lookup.HasDependentsAsync(profile.Id, token))
            return Result<bool>.Conflict("This candidate has resumes, documents, applications or history and cannot be deleted.");

        candidates.Remove(profile);
        await unitOfWork.SaveChangesAsync(token);
        return Result<bool>.Success(true);
    }
}
