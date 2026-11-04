namespace JobHunterAI.Domain;

public sealed class Application
{
    private readonly List<ApplicationEvent> _events = [];

    private Application() { }

    public Application(Guid id, Guid candidateProfileId, Guid jobPostingId, DateTimeOffset createdAt)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        JobPostingId = DomainGuard.Id(jobPostingId, nameof(jobPostingId));
        CreatedAt = DomainGuard.Timestamp(createdAt, nameof(createdAt));
        Status = ApplicationStatus.Draft;
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public Guid JobPostingId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public ApplicationStatus Status { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public IReadOnlyCollection<ApplicationEvent> Events => _events.AsReadOnly();

    public void RequestReview(Guid eventId, DateTimeOffset occurredAt)
    {
        if (Status != ApplicationStatus.Draft && Status != ApplicationStatus.Approved)
        {
            throw new InvalidOperationException("Only a draft or approved application can be sent for review.");
        }

        Transition(eventId, ApplicationStatus.ReadyForReview, "Ready for user review", occurredAt);
        ApprovedByUserId = null;
        ApprovedAt = null;
    }

    public void Approve(Guid eventId, Guid approvedByUserId, DateTimeOffset occurredAt)
    {
        if (Status != ApplicationStatus.ReadyForReview)
        {
            throw new InvalidOperationException("Only an application awaiting review can be approved.");
        }

        DomainGuard.Id(approvedByUserId, nameof(approvedByUserId));
        Transition(eventId, ApplicationStatus.Approved, "Approved by user", occurredAt, approvedByUserId);
        ApprovedByUserId = approvedByUserId;
        ApprovedAt = occurredAt;
    }

    public void Submit(Guid eventId, DateTimeOffset occurredAt)
    {
        if (Status != ApplicationStatus.Approved || ApprovedByUserId is null || ApprovedAt is null)
        {
            throw new InvalidOperationException("User approval is required before submission.");
        }

        Transition(eventId, ApplicationStatus.Submitted, "Application submitted", occurredAt);
    }

    public void Withdraw(Guid eventId, DateTimeOffset occurredAt)
    {
        if (Status is ApplicationStatus.Withdrawn or ApplicationStatus.Rejected or ApplicationStatus.Offer)
        {
            throw new InvalidOperationException("The application cannot be withdrawn from this status.");
        }

        Transition(eventId, ApplicationStatus.Withdrawn, "Application withdrawn", occurredAt);
    }

    public void Reject(Guid eventId, DateTimeOffset occurredAt)
    {
        if (Status != ApplicationStatus.Submitted)
        {
            throw new InvalidOperationException("Only a submitted application can be rejected.");
        }

        Transition(eventId, ApplicationStatus.Rejected, "Application rejected", occurredAt);
    }

    public void RecordOffer(Guid eventId, DateTimeOffset occurredAt)
    {
        if (Status != ApplicationStatus.Submitted)
        {
            throw new InvalidOperationException("Only a submitted application can receive an offer.");
        }

        Transition(eventId, ApplicationStatus.Offer, "Offer received", occurredAt);
    }

    private void Transition(Guid eventId, ApplicationStatus next, string description, DateTimeOffset occurredAt, Guid? actorId = null)
    {
        if (occurredAt < CreatedAt || (_events.Count > 0 && occurredAt < _events[^1].OccurredAt))
        {
            throw new ArgumentException("Application events must be chronological.", nameof(occurredAt));
        }

        foreach (ApplicationEvent existing in _events)
        {
            if (existing.Id == eventId)
            {
                throw new InvalidOperationException("The application event already exists.");
            }
        }

        ApplicationEvent applicationEvent = new(eventId, Id, Status, next, description, occurredAt, actorId);
        _events.Add(applicationEvent);
        Status = next;
    }
}
