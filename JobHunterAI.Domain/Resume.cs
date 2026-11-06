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
    public string? SourceFileName { get; private set; }
    public string? SourceSha256 { get; private set; }
    public string? SourceText { get; private set; }
    public IReadOnlyCollection<ResumeVersion> Versions => _versions.AsReadOnly();
    public ResumeVersion? SourceVersion => _versions.SingleOrDefault(x => x.IsSource);

    public void AddUploadedSource(Guid versionId, string text, string fileName, string sha256, DateTimeOffset createdAt)
    {
        if (_versions.Count != 0) throw new InvalidOperationException("The uploaded source cannot be replaced. Upload a new resume instead.");
        string validatedFile = DomainGuard.Required(fileName, nameof(fileName));
        if (validatedFile.Length > 200 || validatedFile.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A filename without a path is required.", nameof(fileName));
        if (sha256 is null || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("A SHA-256 hash is required.", nameof(sha256));
        AddVersion(versionId, text, createdAt);
        SourceFileName = validatedFile;
        SourceSha256 = sha256.ToLowerInvariant();
        SourceText = text;
    }

    public ResumeVersion AddVersion(Guid id, string contentMarkdown, DateTimeOffset createdAt, Guid? jobPostingId = null)
    {
        foreach (ResumeVersion existing in _versions)
        {
            if (existing.Id == id)
            {
                throw new InvalidOperationException("The resume version already exists.");
            }
        }

        ResumeVersion version = new(id, Id, _versions.Count + 1, contentMarkdown, createdAt, jobPostingId,
            jobPostingId is null && SourceVersion is null);
        _versions.Add(version);
        return version;
    }
}
