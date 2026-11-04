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
    }

    public string? City { get; private set; }
    public string? Country { get; private set; }
    public bool IsRemote { get; private set; }
}
