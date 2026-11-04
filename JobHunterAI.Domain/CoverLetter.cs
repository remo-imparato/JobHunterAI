namespace JobHunterAI.Domain;

public sealed class CoverLetter
{
    private CoverLetter() { }

    public CoverLetter(Guid id, Guid candidateProfileId, Guid jobPostingId, string contentMarkdown, DateTimeOffset createdAt)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        JobPostingId = DomainGuard.Id(jobPostingId, nameof(jobPostingId));
        ContentMarkdown = DomainGuard.Required(contentMarkdown, nameof(contentMarkdown));
        CreatedAt = DomainGuard.Timestamp(createdAt, nameof(createdAt));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public Guid JobPostingId { get; private set; }
    public string ContentMarkdown { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateContent(string contentMarkdown)
    {
        ContentMarkdown = DomainGuard.Required(contentMarkdown, nameof(contentMarkdown));
    }
}
