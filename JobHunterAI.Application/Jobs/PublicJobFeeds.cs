using JobHunterAI.Domain;

namespace JobHunterAI.Application.Jobs;

/// <summary>Employer metadata accompanies each listing; aggregate feeds are not employers.</summary>
public sealed record PublicJobListing(string CompanyName, JobPosting Job);

public sealed record PublicJobFeedResult(IReadOnlyList<PublicJobListing> Jobs, string[] Warnings);

public interface IPublicJobFeed
{
    string Name { get; }
    bool Enabled { get; }
    Task<PublicJobFeedResult> SearchAsync(int limit, CancellationToken token);
}

public interface IPublicJobStore
{
    Task<JobPosting> UpsertAsync(PublicJobListing listing, CancellationToken token);
}

public sealed class PublicFeedOptions
{
    public const string SectionName = "PublicFeeds";
    public bool Arbeitnow { get; set; } = true;
    public bool RemoteOK { get; set; } = true;
    public int MaxPages { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 15;
    public bool IsValid() => MaxPages is >= 1 and <= 10 && RequestTimeoutSeconds is >= 1 and <= 30;
}
