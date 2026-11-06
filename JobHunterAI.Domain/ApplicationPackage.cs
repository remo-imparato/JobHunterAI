namespace JobHunterAI.Domain;

public enum PackageStatus { Pending, Generating, Failed, Completed }

public sealed class ApplicationPackage
{
    public const int MaxDocumentCharacters = 100000;
    private ApplicationPackage() { }
    public ApplicationPackage(Guid id, Guid candidateId, Guid resumeId, Guid sourceVersionId, JobPosting job, string company, string sourceText)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateId, nameof(candidateId));
        ResumeId = DomainGuard.Id(resumeId, nameof(resumeId));
        SourceVersionId = DomainGuard.Id(sourceVersionId, nameof(sourceVersionId));
        ArgumentNullException.ThrowIfNull(job);
        JobPostingId = job.Id;
        CompanyId = job.CompanyId;
        CompanyName = DomainGuard.Required(company, nameof(company));
        JobTitle = job.Title;
        JobDescription = job.Description;
        ApplicationUrl = job.ApplicationUrl;
        City = job.Location.City;
        Country = job.Location.Country;
        Arrangement = job.Location.Arrangement;
        DomainGuard.Required(sourceText, nameof(sourceText));
        SourceText = sourceText;
        if (sourceText.Length > 500000 || JobDescription.Length > 500000) throw new ArgumentException("Source or job text exceeds package limits.");
        CreatedAt = DateTimeOffset.UtcNow;
    }
    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public Guid ResumeId { get; private set; }
    public Guid SourceVersionId { get; private set; }
    public Guid JobPostingId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CompanyName { get; private set; } = "";
    public string JobTitle { get; private set; } = "";
    public string JobDescription { get; private set; } = "";
    public string ApplicationUrl { get; private set; } = "";
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public WorkArrangement? Arrangement { get; private set; }
    public string SourceText { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public PackageStatus Status { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public Guid? ResumeVersionId { get; private set; }
    public Guid? CoverLetterId { get; private set; }
    public string? ResumeMarkdown { get; private set; }
    public string? CoverLetterMarkdown { get; private set; }
    public string? FailureCode { get; private set; }
    public void Begin(Guid leaseId, DateTimeOffset expiresAt)
    {
        if (Status == PackageStatus.Completed || (LeaseExpiresAt > DateTimeOffset.UtcNow && Status == PackageStatus.Generating))
            throw new InvalidOperationException("Package is complete or already being generated.");
        LeaseId = DomainGuard.Id(leaseId, nameof(leaseId));
        if (expiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("A future lease expiry is required.");
        LeaseExpiresAt = expiresAt;
        Status = PackageStatus.Generating;
        FailureCode = null;
    }
    public void SaveResume(Guid leaseId, Guid versionId, string markdown)
    {
        RequireLease(leaseId);
        if (ResumeVersionId.HasValue) throw new InvalidOperationException("Resume output already exists.");
        ResumeMarkdown = ValidateDocument(markdown);
        ResumeVersionId = DomainGuard.Id(versionId, nameof(versionId));
    }
    public void SaveCoverLetter(Guid leaseId, Guid letterId, string markdown)
    {
        RequireLease(leaseId);
        if (CoverLetterId.HasValue) throw new InvalidOperationException("Cover letter output already exists.");
        CoverLetterMarkdown = ValidateDocument(markdown);
        CoverLetterId = DomainGuard.Id(letterId, nameof(letterId));
    }
    public void Complete(Guid leaseId)
    {
        RequireLease(leaseId);
        if (ResumeVersionId is null || CoverLetterId is null) throw new InvalidOperationException("Both documents must succeed.");
        Status = PackageStatus.Completed;
        LeaseExpiresAt = null;
    }
    public void Fail(Guid leaseId, string code)
    {
        RequireLease(leaseId);
        FailureCode = code is "AiUnavailable" or "Cancelled" or "GenerationFailed" ? code : "GenerationFailed";
        Status = PackageStatus.Failed;
        LeaseExpiresAt = null;
    }
    public JobPosting SnapshotJob() => new(JobPostingId, CompanyId, "PackageSnapshot", JobPostingId.ToString(), JobTitle,
        JobDescription, new JobLocation(City, Country, Arrangement), ApplicationUrl);
    public static string ValidateDocument(string markdown)
    {
        string text = DomainGuard.Required(markdown, nameof(markdown));
        if (text.Length > MaxDocumentCharacters) throw new ArgumentException("Generated document exceeds the package limit.");
        return text;
    }
    private void RequireLease(Guid leaseId)
    {
        if (Status != PackageStatus.Generating || LeaseId != leaseId || LeaseExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Generation lease is not active.");
    }
}
