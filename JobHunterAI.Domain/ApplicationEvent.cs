namespace JobHunterAI.Domain;

public sealed class ApplicationEvent
{
    private ApplicationEvent() { }

    internal ApplicationEvent(
        Guid id, Guid applicationId, ApplicationStatus fromStatus, ApplicationStatus toStatus,
        string description, DateTimeOffset occurredAt, Guid? actorId)
    {
        Id = DomainGuard.Id(id, nameof(id));
        ApplicationId = DomainGuard.Id(applicationId, nameof(applicationId));
        if (!Enum.IsDefined(fromStatus) || !Enum.IsDefined(toStatus) || fromStatus == toStatus)
        {
            throw new ArgumentException("The application transition is invalid.", nameof(toStatus));
        }

        FromStatus = fromStatus;
        ToStatus = toStatus;
        Description = DomainGuard.Required(description, nameof(description));
        OccurredAt = DomainGuard.Timestamp(occurredAt, nameof(occurredAt));
        ActorId = actorId.HasValue ? DomainGuard.Id(actorId.Value, nameof(actorId)) : null;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public ApplicationStatus FromStatus { get; private set; }
    public ApplicationStatus ToStatus { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? ActorId { get; private set; }
}
