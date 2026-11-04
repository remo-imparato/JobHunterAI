using AutoMapper;
using FluentValidation;
using JobHunterAI.Application.Applications;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.UnitTests;

public sealed class ApplicationHandlerTests
{
    private static readonly DateTimeOffset CreatedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly IMapper Mapper = ApplicationTestDoubles.Mapper;

    private static JobPosting NewJob(Guid companyId) => new(Guid.NewGuid(), companyId, "Greenhouse", "job-123",
        "Engineering Manager", "Lead engineers", new JobLocation(null, null, true), "https://example.com/apply");

    [Fact]
    public void Mappings_AreValidAndPreserveOwnedLocation()
    {
        MapperConfiguration configuration = new(config => config.AddProfile<ApplicationMappingProfile>(),
            NullLoggerFactory.Instance);
        JobPosting job = NewJob(Guid.NewGuid());

        configuration.AssertConfigurationIsValid();
        JobDto mapped = Mapper.Map<JobDto>(job);

        Assert.Equal(job.Id, mapped.Id);
        Assert.True(mapped.Location.IsRemote);
    }

    [Fact]
    public async Task JobHandlers_SaveQueryUpdateAndRejectDuplicateProviderKey()
    {
        Company company = new(Guid.NewGuid(), "Example Company");
        FakeRepository<Company> companies = new(x => x.Id, company);
        FakeRepository<JobPosting> jobs = new(x => x.Id);
        FakeUnitOfWork unitOfWork = new();
        SaveJobHandler save = new(jobs, companies, new FakeJobPostingLookup(jobs), unitOfWork, Mapper);
        SaveJobCommand command = new(Guid.NewGuid(), company.Id, "Greenhouse", "job-123", "Engineering Manager",
            "Lead engineers", null, null, true, "https://example.com/apply", null);

        Result<JobDto> saved = await save.Handle(command, CancellationToken.None);
        Result<JobDto> duplicate = await save.Handle(command with { Id = Guid.NewGuid() }, CancellationToken.None);
        Result<IReadOnlyList<JobDto>> page = await new GetJobsHandler(jobs, Mapper)
            .Handle(new GetJobsQuery(0, 10), CancellationToken.None);
        Result<JobDto> updated = await new UpdateJobHandler(jobs, unitOfWork, Mapper)
            .Handle(new UpdateJobCommand(command.Id, "Principal Architect", "Lead platforms", null, null, true,
                "https://example.com/new"), CancellationToken.None);

        Assert.True(saved.IsSuccess);
        Assert.Equal(ErrorKind.Conflict, duplicate.Error);
        Assert.Single(page.Value!);
        Assert.Equal("Principal Architect", updated.Value!.Title);
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public async Task ApplicationHandlers_OnlyReturnCandidateDataAndRequireApprovalForSubmission()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        Company company = new(Guid.NewGuid(), "Example Company");
        JobPosting job = NewJob(company.Id);
        FakeRepository<CandidateProfile> candidates = new(x => x.Id, candidate);
        FakeRepository<JobPosting> jobs = new(x => x.Id, job);
        FakeRepository<DomainApplication> applications = new(x => x.Id);
        FakeUnitOfWork unitOfWork = new();
        FakeApplicationLookup lookup = new(applications);
        CreateApplicationHandler create = new(applications, candidates, jobs, lookup, unitOfWork, Mapper);
        Guid applicationId = Guid.NewGuid();

        Result<ApplicationDto> created = await create.Handle(
            new CreateApplicationCommand(applicationId, candidate.Id, job.Id, CreatedAt), CancellationToken.None);
        Result<ApplicationDto> duplicate = await create.Handle(
            new CreateApplicationCommand(Guid.NewGuid(), candidate.Id, job.Id, CreatedAt), CancellationToken.None);
        Result<IReadOnlyList<ApplicationDto>> page = await new GetApplicationsHandler(lookup, Mapper)
            .Handle(new GetApplicationsQuery(candidate.Id, 0, 10), CancellationToken.None);
        Result<IReadOnlyList<ApplicationDto>> anotherCandidate = await new GetApplicationsHandler(lookup, Mapper)
            .Handle(new GetApplicationsQuery(Guid.NewGuid(), 0, 10), CancellationToken.None);
        UpdateApplicationStatusHandler update = new(applications, unitOfWork, Mapper);
        Result<ApplicationDto> premature = await update.Handle(
            new UpdateApplicationStatusCommand(applicationId, Guid.NewGuid(), ApplicationStatus.Submitted, CreatedAt.AddMinutes(1)),
            CancellationToken.None);
        await update.Handle(new UpdateApplicationStatusCommand(applicationId, Guid.NewGuid(),
            ApplicationStatus.ReadyForReview, CreatedAt.AddMinutes(1)), CancellationToken.None);
        Guid approver = Guid.NewGuid();
        await update.Handle(new UpdateApplicationStatusCommand(applicationId, Guid.NewGuid(),
            ApplicationStatus.Approved, CreatedAt.AddMinutes(2), approver), CancellationToken.None);
        Result<ApplicationDto> submitted = await update.Handle(new UpdateApplicationStatusCommand(applicationId,
            Guid.NewGuid(), ApplicationStatus.Submitted, CreatedAt.AddMinutes(3)), CancellationToken.None);

        Assert.Equal(ApplicationStatus.Draft, created.Value!.Status);
        Assert.Equal(ErrorKind.Conflict, duplicate.Error);
        Assert.Single(page.Value!);
        Assert.Empty(anotherCandidate.Value!);
        Assert.Equal(ErrorKind.Conflict, premature.Error);
        Assert.Equal(ApplicationStatus.Submitted, submitted.Value!.Status);
        Assert.Equal(approver, submitted.Value.ApprovedByUserId);
        Assert.Equal(4, unitOfWork.Saves);
    }

    [Fact]
    public async Task GenerationHandlers_UseTypedDraftsAndPersistDocuments()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        JobPosting job = NewJob(Guid.NewGuid());
        Resume resume = new(Guid.NewGuid(), candidate.Id, "Base resume");
        ResumeVersion baseVersion = resume.AddVersion(Guid.NewGuid(), "# Verified experience", CreatedAt);
        FakeRepository<CandidateProfile> candidates = new(x => x.Id, candidate);
        FakeRepository<JobPosting> jobs = new(x => x.Id, job);
        FakeRepository<Resume> resumes = new(x => x.Id, resume);
        FakeRepository<CoverLetter> letters = new(x => x.Id);
        FakeUnitOfWork unitOfWork = new();
        FakeResumeDraftingService resumeDrafts = new();
        GenerateResumeHandler generateResume = new(resumes, candidates, jobs, resumeDrafts, unitOfWork, Mapper);
        GenerateCoverLetterHandler generateLetter = new(letters, candidates, jobs,
            new FakeCoverLetterDraftingService(), unitOfWork, Mapper);

        Result<ResumeVersionDto> tailored = await generateResume.Handle(new GenerateResumeCommand(
            resume.Id, job.Id, Guid.NewGuid(), CreatedAt.AddDays(1)), CancellationToken.None);
        Result<CoverLetterDto> letter = await generateLetter.Handle(new GenerateCoverLetterCommand(
            Guid.NewGuid(), candidate.Id, job.Id, CreatedAt.AddDays(1)), CancellationToken.None);

        Assert.Same(baseVersion, resumeDrafts.ReceivedBaseVersion);
        Assert.Equal(2, tailored.Value!.VersionNumber);
        Assert.Contains("Verified experience", tailored.Value.ContentMarkdown);
        Assert.Equal(job.Id, letter.Value!.JobPostingId);
        Assert.Single(letters.Items);
        Assert.Equal(2, unitOfWork.Saves);
    }

    [Fact]
    public void Validators_RejectUntrustedOrInvalidRequests()
    {
        Assert.False(new GetJobsValidator().Validate(new GetJobsQuery(-1, 101)).IsValid);
        Assert.False(new GetApplicationsValidator().Validate(new GetApplicationsQuery(Guid.Empty, 0, 10)).IsValid);
        Assert.False(new SaveJobValidator().Validate(new SaveJobCommand(Guid.NewGuid(), Guid.NewGuid(),
            "Greenhouse", "job-123", "Engineer", "Description", null, null, false,
            "javascript:alert(1)", null)).IsValid);
        Assert.False(new UpdateApplicationStatusValidator().Validate(new UpdateApplicationStatusCommand(
            Guid.NewGuid(), Guid.NewGuid(), ApplicationStatus.Approved, CreatedAt)).IsValid);
    }
}
