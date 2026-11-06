using JobHunterAI.Application.Common;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Application.Generation;

public sealed class GeneratePackageHandler(IRepository<CandidateProfile> candidates, IRepository<JobPosting> jobs,
    IRepository<Company> companies, IRepository<Resume> resumes, IRepository<CoverLetter> letters,
    IPackageStore store, IPackageDraftingService drafting) : IRequestHandler<GeneratePackageCommand, Result<PackageView>>
{
    public async Task<Result<PackageView>> Handle(GeneratePackageCommand request, CancellationToken token)
    {
        CandidateProfile? candidate = await candidates.GetByIdAsync(request.CandidateId, token);
        if (candidate is null) return Result<PackageView>.NotFound("Candidate not found.");
        Resume? resume = await resumes.GetByIdAsync(request.ResumeId, token);
        if (resume is null || resume.CandidateProfileId != candidate.Id) return Result<PackageView>.NotFound("Source resume not found for this candidate.");
        if (resume.SourceVersion is not { } source || string.IsNullOrWhiteSpace(resume.SourceText))
            return Result<PackageView>.Conflict("Upload an immutable source resume before generating a package.");
        ApplicationPackage? package = await store.FindAsync(candidate.Id, request.JobId, resume.Id, token);
        if (package is null)
        {
            if (!await store.HasMatchAsync(candidate.Id, request.JobId, token)) return Result<PackageView>.NotFound("Job is not a discovery result for this candidate.");
            JobPosting? job = await jobs.GetByIdAsync(request.JobId, token);
            if (job is null) return Result<PackageView>.NotFound("Job not found.");
            Company? company = await companies.GetByIdAsync(job.CompanyId, token);
            if (company is null) return Result<PackageView>.NotFound("Company not found.");
            if (job.Description.Length > 500000) return Result<PackageView>.Conflict("Job description exceeds package limits.");
            package = new(Guid.NewGuid(), candidate.Id, resume.Id, source.Id, job, company.Name, resume.SourceText);
            await store.CreateAsync(package, token);
        }
        if (package.Status == PackageStatus.Completed) return Result<PackageView>.Success(PackageView.From(package));
        Guid lease = Guid.NewGuid();
        if (!await store.AcquireAsync(package, lease, DateTimeOffset.UtcNow.AddMinutes(3), token))
            return Result<PackageView>.Conflict("Package is already generating. Refresh its status before retrying.");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            if (!drafting.IsAvailable)
            {
                using CancellationTokenSource unavailableCleanup = new(TimeSpan.FromSeconds(10));
                await store.FailAsync(package.Id, candidate.Id, lease, "AiUnavailable", unavailableCleanup.Token);
                return Result<PackageView>.Success(PackageView.From((await store.GetAsync(candidate.Id, package.Id, unavailableCleanup.Token))!));
            }
            JobPosting snapshot = package.SnapshotJob();
            if (package.ResumeVersionId is null)
            {
                string markdown = ApplicationPackage.ValidateDocument(await drafting.ResumeAsync(candidate, snapshot, package.SourceText, deadline.Token));
                deadline.Token.ThrowIfCancellationRequested();
                Guid versionId = Guid.NewGuid();
                package.SaveResume(lease, versionId, markdown);
                resume.AddVersion(versionId, markdown, DateTimeOffset.UtcNow, package.JobPostingId);
                await store.SaveAsync(package, deadline.Token);
            }
            if (package.CoverLetterId is null)
            {
                string markdown = ApplicationPackage.ValidateDocument(await drafting.CoverLetterAsync(candidate, snapshot, package.CompanyName, package.SourceText, deadline.Token));
                deadline.Token.ThrowIfCancellationRequested();
                Guid letterId = Guid.NewGuid();
                package.SaveCoverLetter(lease, letterId, markdown);
                letters.Add(new CoverLetter(letterId, candidate.Id, package.JobPostingId, markdown, DateTimeOffset.UtcNow));
                await store.SaveAsync(package, deadline.Token);
            }
            package.Complete(lease);
            await store.SaveAsync(package, deadline.Token);
            return Result<PackageView>.Success(PackageView.From(package));
        }
        catch (Exception exception)
        {
            string code = exception is OperationCanceledException ? "Cancelled" : "GenerationFailed";
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
            await store.FailAsync(package.Id, candidate.Id, lease, code, cleanup.Token);
            ApplicationPackage? saved = await store.GetAsync(candidate.Id, package.Id, cleanup.Token);
            return saved is null ? Result<PackageView>.NotFound("Package not found.") : Result<PackageView>.Success(PackageView.From(saved));
        }
    }
}
