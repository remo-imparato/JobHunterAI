namespace JobHunterAI.Infrastructure.Background;

public sealed class DiscoveryBoard
{
    public string Provider { get; set; } = string.Empty;
    public string Board { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
}

public sealed class BackgroundCandidate
{
    public Guid CandidateId { get; set; }
    public Guid? ResumeId { get; set; }
}

public sealed class BackgroundOptions
{
    public const string SectionName = "Background";
    public bool Enabled { get; set; }
    public bool EnableAiJobs { get; set; }
    public int BatchSize { get; set; } = 10;
    public int ScoreRefreshHours { get; set; } = 24;
    public int LogRetentionDays { get; set; } = 30;
    public string? LogDirectory { get; set; }
    public List<DiscoveryBoard> Boards { get; set; } = [];
    public List<BackgroundCandidate> Candidates { get; set; } = [];

    public bool IsValid()
    {
        if (BatchSize is < 1 or > 100 || ScoreRefreshHours is < 1 or > 8760 || LogRetentionDays is < 1 or > 3650 ||
            Boards.Count > 20 || Candidates.Count > 20)
        {
            return false;
        }

        HashSet<Guid> candidates = [];
        foreach (BackgroundCandidate candidate in Candidates)
        {
            if (candidate.CandidateId == Guid.Empty || candidate.ResumeId == Guid.Empty || !candidates.Add(candidate.CandidateId)) return false;
        }

        foreach (DiscoveryBoard board in Boards)
        {
            if (board.Provider is not ("Greenhouse" or "Lever" or "Ashby") || board.CompanyId == Guid.Empty ||
                string.IsNullOrWhiteSpace(board.Board) || board.Board.Length > 200) return false;
        }

        return LogDirectory is null || (Path.IsPathFullyQualified(LogDirectory) &&
            Path.GetFullPath(LogDirectory).TrimEnd(Path.DirectorySeparatorChar) != Path.GetPathRoot(LogDirectory)?.TrimEnd(Path.DirectorySeparatorChar));
    }
}
