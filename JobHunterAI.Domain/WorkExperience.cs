namespace JobHunterAI.Domain;

public sealed class WorkExperience
{
    private WorkExperience() { }

    internal WorkExperience(Guid id, Guid candidateProfileId, string employer, string title, DateOnly start, DateOnly? end, string description)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        Employer = DomainGuard.Required(employer, nameof(employer));
        Title = DomainGuard.Required(title, nameof(title));
        DomainGuard.DateRange(start, end);
        Start = start;
        End = end;
        Description = DomainGuard.Required(description, nameof(description));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Employer { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public DateOnly Start { get; private set; }
    public DateOnly? End { get; private set; }
    public string Description { get; private set; } = string.Empty;
}
