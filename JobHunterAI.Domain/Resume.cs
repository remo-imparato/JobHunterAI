namespace JobHunterAI.Domain;

public sealed class Resume
{
    private readonly List<ResumeVersion> _versions = [];

    private Resume() { }

    public Resume(Guid id, Guid candidateProfileId, string name)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        Name = DomainGuard.Required(name, nameof(name));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<ResumeVersion> Versions => _versions.AsReadOnly();

    public ResumeVersion AddVersion(Guid id, string contentMarkdown, DateTimeOffset createdAt, Guid? jobPostingId = null)
    {
        foreach (ResumeVersion existing in _versions)
        {
            if (existing.Id == id)
            {
                throw new InvalidOperationException("The resume version already exists.");
            }
        }

        ResumeVersion version = new(id, Id, _versions.Count + 1, contentMarkdown, createdAt, jobPostingId);
        _versions.Add(version);
        return version;
    }
}
