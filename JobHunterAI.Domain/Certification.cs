namespace JobHunterAI.Domain;

public sealed class Certification
{
    private Certification() { }

    internal Certification(Guid id, Guid candidateProfileId, string name, string issuer, DateOnly obtainedOn, DateOnly? expiresOn)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        Name = DomainGuard.Required(name, nameof(name));
        Issuer = DomainGuard.Required(issuer, nameof(issuer));
        DomainGuard.DateRange(obtainedOn, expiresOn);
        ObtainedOn = obtainedOn;
        ExpiresOn = expiresOn;
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Issuer { get; private set; } = string.Empty;
    public DateOnly ObtainedOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
}
