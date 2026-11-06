namespace JobHunterAI.Domain;

[Flags]
public enum WorkArrangement
{
    Remote = 1,
    Hybrid = 2,
    OnSite = 4
}

public sealed class CandidateSearchCriteria
{
    private string[] _jobTitles = [];
    private string[] _locations = [];
    private string[] _requiredKeywords = [];
    private string[] _excludedKeywords = [];

    private CandidateSearchCriteria() { }

    public CandidateSearchCriteria(Guid candidateId, IEnumerable<string> jobTitles, IEnumerable<string> locations,
        WorkArrangement arrangements, bool includeUnknownArrangement, IEnumerable<string> requiredKeywords, IEnumerable<string> excludedKeywords)
    {
        Id = DomainGuard.Id(candidateId, nameof(candidateId));
        Update(jobTitles, locations, arrangements, includeUnknownArrangement, requiredKeywords, excludedKeywords);
    }

    public Guid Id { get; private set; }
    public string[] JobTitles => _jobTitles.ToArray();
    public string[] Locations => _locations.ToArray();
    public string[] RequiredKeywords => _requiredKeywords.ToArray();
    public string[] ExcludedKeywords => _excludedKeywords.ToArray();
    public WorkArrangement Arrangements { get; private set; }
    public bool IncludeUnknownArrangement { get; private set; }

    public void Update(IEnumerable<string> jobTitles, IEnumerable<string> locations, WorkArrangement arrangements,
        bool includeUnknownArrangement, IEnumerable<string> requiredKeywords, IEnumerable<string> excludedKeywords)
    {
        string[] titles = Normalize(jobTitles, nameof(jobTitles));
        string[] places = Normalize(locations, nameof(locations));
        string[] required = Normalize(requiredKeywords, nameof(requiredKeywords));
        string[] excluded = Normalize(excludedKeywords, nameof(excludedKeywords));
        if (titles.Length == 0) throw new ArgumentException("Enter at least one job title.", nameof(jobTitles));
        if ((int)arrangements is < 1 or > 7) throw new ArgumentException("Select at least one valid work arrangement.", nameof(arrangements));
        if (required.Intersect(excluded, StringComparer.OrdinalIgnoreCase).Any())
            throw new ArgumentException("A keyword cannot be both required and excluded.", nameof(excludedKeywords));
        _jobTitles = titles;
        _locations = places;
        _requiredKeywords = required;
        _excludedKeywords = excluded;
        Arrangements = arrangements;
        IncludeUnknownArrangement = includeUnknownArrangement;
    }

    public bool Matches(string title, string? city, string? country, WorkArrangement? knownArrangement, string description)
    {
        if (knownArrangement.HasValue && knownArrangement.Value is not (WorkArrangement.Remote or WorkArrangement.Hybrid or WorkArrangement.OnSite))
            throw new ArgumentException("Supply one known arrangement or null for unknown.", nameof(knownArrangement));
        bool arrangementMatches = knownArrangement.HasValue
            ? (Arrangements & knownArrangement.Value) != 0 : IncludeUnknownArrangement;
        string location = $"{city} {country}";
        string content = $"{title}\n{description}";
        return arrangementMatches && _jobTitles.Any(term => title.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
            (_locations.Length == 0 || _locations.Any(term => location.Contains(term, StringComparison.OrdinalIgnoreCase))) &&
            _requiredKeywords.All(term => content.Contains(term, StringComparison.OrdinalIgnoreCase)) &&
            !_excludedKeywords.Any(term => content.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] Normalize(IEnumerable<string> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        string[] input = values.Take(21).ToArray();
        if (input.Length > 20) throw new ArgumentException("Each criteria list supports at most 20 entries.", name);
        if (input.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 200))
            throw new ArgumentException("Each criteria entry must contain 1–200 characters.", name);
        return input.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
