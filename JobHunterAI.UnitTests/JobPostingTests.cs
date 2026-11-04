using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class JobPostingTests
{
    [Fact]
    public void NormalizedPosting_PreservesProviderAndLocation()
    {
        Guid companyId = Guid.NewGuid();
        JobLocation location = new("Seattle", "USA", true);
        JobPosting posting = new(Guid.NewGuid(), companyId, "Greenhouse", "job-123", "Architect",
            "Lead software teams", location, "https://example.com/apply");

        Assert.Equal(companyId, posting.CompanyId);
        Assert.Equal("Greenhouse", posting.ProviderName);
        Assert.Equal("job-123", posting.ExternalId);
        Assert.Equal(location, posting.Location);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com/apply")]
    [InlineData("/relative")]
    public void UnsafeApplicationUrl_IsRejected(string url)
    {
        Assert.Throws<ArgumentException>(() => new JobPosting(Guid.NewGuid(), Guid.NewGuid(),
            "Lever", "job-123", "Architect", "Lead software teams", new JobLocation(null, null, true), url));
    }

    [Fact]
    public void InvalidUpdate_DoesNotChangeJobPosting()
    {
        JobPosting posting = new(Guid.NewGuid(), Guid.NewGuid(), "Ashby", "job-123", "Architect",
            "Lead software teams", new JobLocation(null, null, true), "https://example.com/apply");

        Assert.Throws<ArgumentException>(() => posting.UpdateDetails("New title", "New description",
            new JobLocation(null, null, true), "file:///private.txt"));
        Assert.Throws<ArgumentException>(() => new JobLocation(null, "USA", false));

        Assert.Equal("Architect", posting.Title);
        Assert.Equal("https://example.com/apply", posting.ApplicationUrl);
    }
}
