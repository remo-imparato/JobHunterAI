namespace JobHunterAI.Domain;

public sealed class JobMatchScore
{
    private JobMatchScore() { }

    public JobMatchScore(
        Guid id,
        Guid candidateProfileId,
        Guid jobPostingId,
        int score,
        IEnumerable<string> strengths,
        IEnumerable<string> weaknesses,
        IEnumerable<string> recommendedKeywords,
        IEnumerable<string> riskFactors,
        DateTimeOffset scoredAt)
    {
        ArgumentNullException.ThrowIfNull(strengths);
        ArgumentNullException.ThrowIfNull(weaknesses);
        ArgumentNullException.ThrowIfNull(recommendedKeywords);
        ArgumentNullException.ThrowIfNull(riskFactors);
        if (score is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Score must be between 0 and 100.");
        }

        Id = DomainGuard.Id(id, nameof(id));
        CandidateProfileId = DomainGuard.Id(candidateProfileId, nameof(candidateProfileId));
        JobPostingId = DomainGuard.Id(jobPostingId, nameof(jobPostingId));
        Score = score;
        Strengths = [.. strengths];
        Weaknesses = [.. weaknesses];
        RecommendedKeywords = [.. recommendedKeywords];
        RiskFactors = [.. riskFactors];
        ScoredAt = DomainGuard.Timestamp(scoredAt, nameof(scoredAt));
    }

    public Guid Id { get; private set; }
    public Guid CandidateProfileId { get; private set; }
    public Guid JobPostingId { get; private set; }
    public int Score { get; private set; }
    public string[] Strengths { get; private set; } = [];
    public string[] Weaknesses { get; private set; } = [];
    public string[] RecommendedKeywords { get; private set; } = [];
    public string[] RiskFactors { get; private set; } = [];
    public DateTimeOffset ScoredAt { get; private set; }
}
