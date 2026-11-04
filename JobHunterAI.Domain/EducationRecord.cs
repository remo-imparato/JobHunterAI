namespace JobHunterAI.Domain;

public sealed class EducationRecord
{
    private EducationRecord() { }

    internal EducationRecord(Guid id, Guid candidateProfileId, string institution, string qualification, DateOnly start, DateOnly? end)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        Institution = DomainGuard.Required(institution, nameof(institution));
        Qualification = DomainGuard.Required(qualification, nameof(qualification));
        DomainGuard.DateRange(start, end);
        Start = start;
        End = end;
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string Institution { get; private set; } = string.Empty;
    public string Qualification { get; private set; } = string.Empty;
    public DateOnly Start { get; private set; }
    public DateOnly? End { get; private set; }
}
