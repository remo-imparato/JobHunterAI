namespace JobHunterAI.Domain;

public sealed record JobLocation
{
    private JobLocation() { }

    public JobLocation(string? city, string? country, bool isRemote)
    {
        if (!isRemote || !string.IsNullOrWhiteSpace(city))
        {
            City = DomainGuard.Required(city, nameof(city));
        }

        if (!isRemote || !string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(country))
        {
            Country = DomainGuard.Required(country, nameof(country));
        }

        IsRemote = isRemote;
        Arrangement = isRemote ? WorkArrangement.Remote : null;
    }

    public JobLocation(string? city, string? country, WorkArrangement? arrangement)
    {
        if (arrangement.HasValue && arrangement.Value is not (WorkArrangement.Remote or WorkArrangement.Hybrid or WorkArrangement.OnSite))
            throw new ArgumentException("Supply one confirmed arrangement or null for unknown.", nameof(arrangement));
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? null : country.Trim();
        Arrangement = arrangement;
        IsRemote = arrangement == WorkArrangement.Remote;
    }

    public string? City { get; private set; }
    public string? Country { get; private set; }
    public bool IsRemote { get; private set; }
    public WorkArrangement? Arrangement { get; private set; }
}
