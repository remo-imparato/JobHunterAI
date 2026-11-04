namespace JobHunterAI.Domain;

public sealed class Skill
{
    private Skill() { }

    internal Skill(Guid id, Guid candidateProfileId, string name)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        Name = DomainGuard.Required(name, nameof(name));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Name { get; private set; } = string.Empty;
}
