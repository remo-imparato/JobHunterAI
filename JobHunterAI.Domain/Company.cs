namespace JobHunterAI.Domain;

public sealed class Company
{
    private Company() { }

    public Company(Guid id, string name, string? websiteUrl = null)
    {
        Id = DomainGuard.Id(id, nameof(id));
        Name = DomainGuard.Required(name, nameof(name));
        WebsiteUrl = websiteUrl is null ? null : DomainGuard.HttpUrl(websiteUrl, nameof(websiteUrl));
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? WebsiteUrl { get; private set; }

    public void UpdateDetails(string name, string? websiteUrl)
    {
        string validatedName = DomainGuard.Required(name, nameof(name));
        string? validatedUrl = websiteUrl is null ? null : DomainGuard.HttpUrl(websiteUrl, nameof(websiteUrl));

        Name = validatedName;
        WebsiteUrl = validatedUrl;
    }
}
