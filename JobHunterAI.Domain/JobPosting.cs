namespace JobHunterAI.Domain;

public sealed class JobPosting
{
    private JobPosting() { }

    public JobPosting(
        Guid id, Guid companyId, string providerName, string externalId, string title,
        string description, JobLocation location, string applicationUrl, DateTimeOffset? postedAt = null)
    {
        Id = DomainGuard.Id(id, nameof(id));
        CompanyId = DomainGuard.Id(companyId, nameof(companyId));
        ProviderName = DomainGuard.Required(providerName, nameof(providerName));
        ExternalId = DomainGuard.Required(externalId, nameof(externalId));
        Title = DomainGuard.Required(title, nameof(title));
        Description = DomainGuard.Required(description, nameof(description));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ApplicationUrl = DomainGuard.HttpUrl(applicationUrl, nameof(applicationUrl));
        PostedAt = postedAt.HasValue ? DomainGuard.Timestamp(postedAt.Value, nameof(postedAt)) : null;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public JobLocation Location { get; private set; } = null!;
    public string ApplicationUrl { get; private set; } = string.Empty;
    public DateTimeOffset? PostedAt { get; private set; }

    public void UpdateDetails(string title, string description, JobLocation location, string applicationUrl)
    {
        string validatedTitle = DomainGuard.Required(title, nameof(title));
        string validatedDescription = DomainGuard.Required(description, nameof(description));
        ArgumentNullException.ThrowIfNull(location);
        string validatedUrl = DomainGuard.HttpUrl(applicationUrl, nameof(applicationUrl));

        Title = validatedTitle;
        Description = validatedDescription;
        Location = location;
        ApplicationUrl = validatedUrl;
    }
}
