namespace JobHunterAI.Domain;

public enum DiscoveryStatus { Running, Completed, CompletedWithWarnings, Cancelled, Failed }

public sealed class JobDiscoveryRun
{
    private readonly List<JobDiscoveryResult> _results = [];
    private string[] _warnings = [];
    private JobDiscoveryRun() { }

    public JobDiscoveryRun(Guid id, Guid candidateId, string criteriaSnapshot, DateTimeOffset startedAt)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateId, nameof(candidateId));
        CriteriaSnapshot = DomainGuard.Required(criteriaSnapshot, nameof(criteriaSnapshot));
        StartedAt = DomainGuard.Timestamp(startedAt, nameof(startedAt));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public string CriteriaSnapshot { get; private set; } = string.Empty;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DiscoveryStatus Status { get; private set; }
    public int ExaminedJobs { get; private set; }
    public string[] Warnings => _warnings.ToArray();
    public IReadOnlyCollection<JobDiscoveryResult> Results => _results.AsReadOnly();

    public void Examine() { EnsureRunning(); ExaminedJobs++; }
    public void Warn(string warning)
    {
        EnsureRunning();
        if (_warnings.Length < 24) _warnings = [.. _warnings, DomainGuard.Required(warning, nameof(warning))];
    }
    public void AddMatch(Guid jobId)
    {
        EnsureRunning();
        DomainGuard.Id(jobId, nameof(jobId));
        if (_results.All(x => x.JobPostingId != jobId)) _results.Add(new JobDiscoveryResult(Guid.NewGuid(), Id, jobId));
    }
    public void Finish(DiscoveryStatus status, DateTimeOffset completedAt)
    {
        EnsureRunning();
        if (status == DiscoveryStatus.Running || !Enum.IsDefined(status)) throw new ArgumentException("A final status is required.", nameof(status));
        if (completedAt < StartedAt) throw new ArgumentException("Completion cannot precede the start.", nameof(completedAt));
        Status = status;
        CompletedAt = completedAt;
    }
    public void RemoveMatch(Guid jobId)
    {
        EnsureRunning();
        _results.RemoveAll(x => x.JobPostingId == jobId);
    }
    private void EnsureRunning()
    {
        if (Status != DiscoveryStatus.Running) throw new InvalidOperationException("The search run has already finished.");
    }
}

public sealed class JobDiscoveryResult
{
    private JobDiscoveryResult() { }
    internal JobDiscoveryResult(Guid id, Guid runId, Guid jobId) { Id = id; RunId = runId; JobPostingId = jobId; }
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public Guid JobPostingId { get; private set; }
}
