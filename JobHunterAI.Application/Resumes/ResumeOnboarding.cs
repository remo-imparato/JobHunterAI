using System.Security.Cryptography;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Resumes;

public sealed record ResumeSummary(Guid Id, string Name, Guid? SourceVersionId, string? SourceFileName, DateTimeOffset? UploadedAt);
public sealed record ResumePreview(ResumeSummary Resume, string SourceText, ParsedResume ProposedAdditions);
public interface IResumeLookup
{
    Task<IReadOnlyList<ResumeSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token);
    Task<Resume?> FindByHashAsync(Guid candidateId, string hash, CancellationToken token);
}

public sealed record ListCandidateResumesQuery(Guid CandidateId, int Page = 0, int PageSize = 10)
    : IRequest<Result<IReadOnlyList<ResumeSummary>>>;
public sealed record GetResumePreviewQuery(Guid CandidateId, Guid ResumeId) : IRequest<Result<ResumePreview>>;
public sealed record UploadResumeCommand(Guid CandidateId, string FileName, Stream Content) : IRequest<Result<ResumePreview>>;
public sealed record ImportResumeProfileCommand(Guid CandidateId, Guid ResumeId) : IRequest<Result<ResumePreview>>;

public sealed class ListCandidateResumesValidator : AbstractValidator<ListCandidateResumesQuery>
{
    public ListCandidateResumesValidator()
    {
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.Page).InclusiveBetween(0, 100000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetResumePreviewValidator : AbstractValidator<GetResumePreviewQuery>
{
    public GetResumePreviewValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.ResumeId).NotEmpty(); }
}
public sealed class UploadResumeValidator : AbstractValidator<UploadResumeCommand>
{
    public UploadResumeValidator()
    {
        RuleFor(x => x.CandidateId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.Content).NotNull().Must(x => x is not null && x.CanRead);
    }
}
public sealed class ImportResumeProfileValidator : AbstractValidator<ImportResumeProfileCommand>
{
    public ImportResumeProfileValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.ResumeId).NotEmpty(); }
}

public static class ResumePreviews
{
    public static ResumeSummary Summary(Resume resume) => new(resume.Id, resume.Name, resume.SourceVersion?.Id,
        resume.SourceFileName, resume.SourceVersion?.CreatedAt);

    public static ResumePreview Create(Resume resume, CandidateProfile candidate, IResumeParser parser)
    {
        string text = resume.SourceText ?? resume.SourceVersion?.ContentMarkdown ?? string.Empty;
        ParsedResume parsed = parser.ParseText(text);
        return new(Summary(resume), text, ResumeProfileImporter.Preview(candidate, parsed));
    }
}

public sealed class ListCandidateResumesHandler(IRepository<CandidateProfile> candidates, IResumeLookup lookup)
    : IRequestHandler<ListCandidateResumesQuery, Result<IReadOnlyList<ResumeSummary>>>
{
    public async Task<Result<IReadOnlyList<ResumeSummary>>> Handle(ListCandidateResumesQuery request, CancellationToken token)
    {
        if (await candidates.GetByIdAsync(request.CandidateId, token) is null)
            return Result<IReadOnlyList<ResumeSummary>>.NotFound("Candidate not found.");
        return Result<IReadOnlyList<ResumeSummary>>.Success(await lookup.ListAsync(request.CandidateId, request.Page, request.PageSize, token));
    }
}

public sealed class GetResumePreviewHandler(IRepository<CandidateProfile> candidates, IRepository<Resume> resumes, IResumeParser parser)
    : IRequestHandler<GetResumePreviewQuery, Result<ResumePreview>>
{
    public async Task<Result<ResumePreview>> Handle(GetResumePreviewQuery request, CancellationToken token)
    {
        CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateId, token);
        Resume? resume = await resumes.GetByIdAsync(request.ResumeId, token);
        if (candidate is null || resume is null || resume.CandidateProfileId != candidate.Id)
            return Result<ResumePreview>.NotFound("Candidate resume not found.");
        if (resume.SourceVersion is null) return Result<ResumePreview>.Conflict("This resume has no source version. Upload a PDF or DOCX source.");
        return Result<ResumePreview>.Success(ResumePreviews.Create(resume, candidate, parser));
    }
}

public sealed class UploadResumeHandler(IRepository<CandidateProfile> candidates, IRepository<Resume> resumes,
    IResumeLookup lookup, IResumeParser parser, IUnitOfWork work)
    : IRequestHandler<UploadResumeCommand, Result<ResumePreview>>
{
    public async Task<Result<ResumePreview>> Handle(UploadResumeCommand request, CancellationToken token)
    {
        CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateId, token);
        if (candidate is null) return Result<ResumePreview>.NotFound("Candidate not found.");
        string fileName = request.FileName.Replace('\\', '/').Split('/')[^1].Trim();
        if (Path.GetExtension(fileName).ToLowerInvariant() is not (".pdf" or ".docx"))
            throw new UnsupportedResumeFormatException("Only PDF and DOCX resumes are supported.");
        if (fileName.Length > 200) fileName = fileName[^200..];
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await request.Content.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > ResumeUploadLimits.MaxFileBytes)
                throw new ResumeFileTooLargeException("The resume file exceeds 10 MB.");
            buffer.Write(chunk, 0, read);
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
        Resume? existing = await lookup.FindByHashAsync(candidate.Id, hash, token);
        if (existing is not null) return Result<ResumePreview>.Success(ResumePreviews.Create(existing, candidate, parser));
        buffer.Position = 0;
        ParsedResume parsed = await parser.ParseAsync(buffer, fileName, token);
        if (string.IsNullOrWhiteSpace(parsed.SourceText))
            throw new UnsupportedResumeFormatException("The resume has no readable text. Upload a text-based PDF or DOCX.");
        Resume resume = new(Guid.NewGuid(), candidate.Id, fileName);
        resume.AddUploadedSource(Guid.NewGuid(), parsed.SourceText, fileName, hash, DateTimeOffset.UtcNow);
        resumes.Add(resume);
        await work.SaveChangesAsync(token);
        return Result<ResumePreview>.Success(new(ResumePreviews.Summary(resume), parsed.SourceText, ResumeProfileImporter.Preview(candidate, parsed)));
    }
}

public sealed class ImportResumeProfileHandler(IRepository<CandidateProfile> candidates, IRepository<Resume> resumes,
    IResumeParser parser, IUnitOfWork work) : IRequestHandler<ImportResumeProfileCommand, Result<ResumePreview>>
{
    public async Task<Result<ResumePreview>> Handle(ImportResumeProfileCommand request, CancellationToken token)
    {
        CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateId, token);
        Resume? resume = await resumes.GetByIdAsync(request.ResumeId, token);
        if (candidate is null || resume is null || resume.CandidateProfileId != candidate.Id)
            return Result<ResumePreview>.NotFound("Candidate resume not found.");
        if (resume.SourceVersion is null) return Result<ResumePreview>.Conflict("A source resume is required.");
        ParsedResume additions = ResumePreviews.Create(resume, candidate, parser).ProposedAdditions;
        ResumeProfileImporter.Apply(candidate, additions);
        await work.SaveChangesAsync(token);
        return Result<ResumePreview>.Success(ResumePreviews.Create(resume, candidate, parser));
    }
}
