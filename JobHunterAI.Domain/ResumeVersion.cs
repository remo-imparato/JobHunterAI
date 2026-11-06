namespace JobHunterAI.Domain;

public sealed class ResumeVersion
{
    private ResumeVersion() { }

    internal ResumeVersion(Guid id, Guid resumeId, int versionNumber, string contentMarkdown, DateTimeOffset createdAt, Guid? jobPostingId, bool isSource)
    {
        Id = DomainGuard.Id(id, nameof(id));
        ResumeId = DomainGuard.Id(resumeId, nameof(resumeId));
        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber));
        }

        VersionNumber = versionNumber;
        ContentMarkdown = DomainGuard.Required(contentMarkdown, nameof(contentMarkdown));
        CreatedAt = DomainGuard.Timestamp(createdAt, nameof(createdAt));
        JobPostingId = jobPostingId.HasValue ? DomainGuard.Id(jobPostingId.Value, nameof(jobPostingId)) : null;
        IsSource = isSource;
    }

    public Guid Id { get; private set; }
    public Guid ResumeId { get; private set; }
    public int VersionNumber { get; private set; }
    public string ContentMarkdown { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? JobPostingId { get; private set; }
    public bool IsSource { get; private set; }
}
