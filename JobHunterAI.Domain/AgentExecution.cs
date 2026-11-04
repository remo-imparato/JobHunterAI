namespace JobHunterAI.Domain;

public sealed class AgentExecution
{
    private AgentExecution() { }

    public AgentExecution(Guid id, Guid candidateProfileId, string agentName, DateTimeOffset startedAt, Guid? applicationId = null)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        AgentName = DomainGuard.Required(agentName, nameof(agentName));
        StartedAt = DomainGuard.Timestamp(startedAt, nameof(startedAt));
        ApplicationId = applicationId.HasValue ? DomainGuard.Id(applicationId.Value, nameof(applicationId)) : null;
        Status = AgentExecutionStatus.Running;
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public string AgentName { get; private set; } = string.Empty;
    public AgentExecutionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureCode { get; private set; }

    public void Complete(DateTimeOffset completedAt)
    {
        EnsureRunning(completedAt);
        CompletedAt = completedAt;
        Status = AgentExecutionStatus.Succeeded;
    }

    public void Fail(string failureCode, DateTimeOffset completedAt)
    {
        string validatedCode = DomainGuard.Required(failureCode, nameof(failureCode));
        EnsureRunning(completedAt);
        FailureCode = validatedCode;
        CompletedAt = completedAt;
        Status = AgentExecutionStatus.Failed;
    }

    private void EnsureRunning(DateTimeOffset completedAt)
    {
        DomainGuard.Timestamp(completedAt, nameof(completedAt));
        if (Status != AgentExecutionStatus.Running || completedAt < StartedAt)
        {
            throw new InvalidOperationException("Only a running execution can finish after it starts.");
        }
    }
}
