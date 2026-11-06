using System.IO.Compression;
using System.Text;
using FluentValidation;
using JobHunterAI.Application.Common;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Generation;

public sealed record PackageView(Guid Id, Guid CandidateId, Guid JobId, Guid ResumeId, Guid SourceVersionId,
    PackageStatus Status, string Company, string JobTitle, string JobDescription, string ApplicationUrl,
    string? ResumeMarkdown, string? CoverLetterMarkdown, string? FailureCode, DateTimeOffset? LeaseExpiresAt)
{
    public static PackageView From(ApplicationPackage p) => new(p.Id, p.CandidateProfileId, p.JobPostingId, p.ResumeId, p.SourceVersionId,
        p.Status, p.CompanyName, p.JobTitle, p.JobDescription, p.ApplicationUrl, p.ResumeMarkdown, p.CoverLetterMarkdown, p.FailureCode, p.LeaseExpiresAt);
}
public interface IPackageDraftingService
{
    bool IsAvailable { get; }
    Task<string> ResumeAsync(CandidateProfile candidate, JobPosting snapshot, string sourceText, CancellationToken token);
    Task<string> CoverLetterAsync(CandidateProfile candidate, JobPosting snapshot, string company, string sourceText, CancellationToken token);
}
public interface IPackageStore
{
    Task<ApplicationPackage?> FindAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token);
    Task<ApplicationPackage?> GetAsync(Guid candidateId, Guid packageId, CancellationToken token);
    Task<bool> HasMatchAsync(Guid candidateId, Guid jobId, CancellationToken token);
    Task CreateAsync(ApplicationPackage package, CancellationToken token);
    Task<bool> AcquireAsync(ApplicationPackage package, Guid leaseId, DateTimeOffset expiresAt, CancellationToken token);
    Task SaveAsync(ApplicationPackage package, CancellationToken token);
    Task FailAsync(Guid packageId, Guid candidateId, Guid leaseId, string code, CancellationToken token);
}
public sealed record GeneratePackageCommand(Guid CandidateId, Guid JobId, Guid ResumeId) : IRequest<Result<PackageView>>;
public sealed record GetPackageQuery(Guid CandidateId, Guid PackageId) : IRequest<Result<PackageView>>;
public sealed record FindPackageQuery(Guid CandidateId, Guid JobId, Guid ResumeId) : IRequest<Result<PackageView>>;
public sealed class GeneratePackageValidator : AbstractValidator<GeneratePackageCommand>
{
    public GeneratePackageValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.JobId).NotEmpty(); RuleFor(x => x.ResumeId).NotEmpty(); }
}
public sealed class GetPackageValidator : AbstractValidator<GetPackageQuery>
{
    public GetPackageValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.PackageId).NotEmpty(); }
}
public sealed class FindPackageValidator : AbstractValidator<FindPackageQuery>
{
    public FindPackageValidator() { RuleFor(x => x.CandidateId).NotEmpty(); RuleFor(x => x.JobId).NotEmpty(); RuleFor(x => x.ResumeId).NotEmpty(); }
}
public sealed class GetPackageHandler(IPackageStore store) : IRequestHandler<GetPackageQuery, Result<PackageView>>
{
    public async Task<Result<PackageView>> Handle(GetPackageQuery request, CancellationToken token)
    {
        var package = await store.GetAsync(request.CandidateId, request.PackageId, token);
        return package is null ? Result<PackageView>.NotFound("Package not found for this candidate.") : Result<PackageView>.Success(PackageView.From(package));
    }
}
public sealed class FindPackageHandler(IPackageStore store) : IRequestHandler<FindPackageQuery, Result<PackageView>>
{
    public async Task<Result<PackageView>> Handle(FindPackageQuery request, CancellationToken token)
    {
        var package = await store.FindAsync(request.CandidateId, request.JobId, request.ResumeId, token);
        return package is null ? Result<PackageView>.NotFound("No package exists for this selection.") : Result<PackageView>.Success(PackageView.From(package));
    }
}
public sealed record PackageArchive(byte[] Bytes, string FileName);
public static class PackageZip
{
    public const int MaxArchiveBytes = 4 * 1024 * 1024;
    public static PackageArchive Create(PackageView package)
    {
        if (package.Status != PackageStatus.Completed || package.ResumeMarkdown is null || package.CoverLetterMarkdown is null)
            throw new InvalidOperationException("Both documents must be complete before download.");
        ApplicationPackage.ValidateDocument(package.ResumeMarkdown);
        ApplicationPackage.ValidateDocument(package.CoverLetterMarkdown);
        if (package.JobDescription.Length > 500000 || package.ApplicationUrl.Length > 2048) throw new ArgumentException("Package exceeds archive limits.");
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
        {
            Add("resume.md", package.ResumeMarkdown);
            Add("cover-letter.md", package.CoverLetterMarkdown);
            Add("job-description.txt", package.JobDescription);
            Add("job-link.txt", package.ApplicationUrl);
            void Add(string name, string content)
            {
                using StreamWriter writer = new(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        if (stream.Length > MaxArchiveBytes) throw new InvalidOperationException("Archive exceeds download limits.");
        return new(stream.ToArray(), FileName(package.Company, package.JobTitle));
    }
    public static string FileName(string company, string title)
    {
        string name = Clean(company) + "-" + Clean(title);
        return name[..Math.Min(120, name.Length)].TrimEnd('.', ' ', '-') + ".zip";
        static string Clean(string value)
        {
            string text = new(value.Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) || char.IsSurrogate(c) ? '-' : c).ToArray());
            text = text.Trim(' ', '.', '-');
            if (text.Length == 0) return "application";
            string stem = text.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')) text = "application-" + text;
            return text[..Math.Min(60, text.Length)].TrimEnd(' ', '.');
        }
    }
}
