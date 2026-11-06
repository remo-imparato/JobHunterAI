using FluentValidation;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using JobHunterAI.Infrastructure.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunterAI.IntegrationTests;

public sealed class Phase2PersistenceTests
{
    [Fact]
    public void Model_EnforcesSingleSourceAndCandidateUploadDeduplication()
    {
        using JobHunterDbContext db = Create();
        var versions = db.Model.FindEntityType(typeof(ResumeVersion))!;
        Assert.Contains(versions.GetIndexes(), index => index.IsUnique && index.GetFilter() == "\"IsSource\"");
        var resumes = db.Model.FindEntityType(typeof(Resume))!;
        Assert.Contains(resumes.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(Resume.CandidateProfileId), nameof(Resume.SourceSha256)]));
        var criteria = db.Model.FindEntityType(typeof(CandidateSearchCriteria))!;
        Assert.Equal(DeleteBehavior.Cascade, Assert.Single(criteria.GetForeignKeys()).DeleteBehavior);
        Assert.Equal("text[]", criteria.FindProperty(nameof(CandidateSearchCriteria.JobTitles))!.GetColumnType());
        Assert.True(criteria.FindProperty("xmin")!.IsConcurrencyToken);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migration_BackfillsEarliestUntailoredVersionBeforeUniqueSourceIndex()
    {
        using JobHunterDbContext db = Create();
        string sql = db.GetService<IMigrator>().GenerateScript("20261105182354_AddCandidateUsernames", "20261105222158_AddResumeSourcesAndSearchCriteria");
        Assert.Contains("WHERE \"JobPostingId\" IS NULL", sql);
        Assert.Contains("ORDER BY \"ResumeId\", \"VersionNumber\", \"Id\"", sql);
        Assert.True(sql.IndexOf("SET \"IsSource\" = TRUE", StringComparison.Ordinal) < sql.IndexOf("CREATE UNIQUE INDEX \"IX_resume_versions_ResumeId\"", StringComparison.Ordinal));
        Assert.Contains("candidate_search_criteria", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    [Fact]
    public async Task ResumeLists_AreCandidateScopedPagedAndDoNotLoadSourceText()
    {
        RepositoryQueryTests.CaptureCommands commands = new();
        await using JobHunterDbContext db = RepositoryQueryTests.Create(commands);
        ResumeLookup lookup = new(db);
        Assert.Empty(await lookup.ListAsync(Guid.NewGuid(), 2, 10, default));
        string sql = Assert.Single(commands.Sql);
        Assert.Contains("CandidateProfileId", sql);
        Assert.Contains("IsSource", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("OFFSET", sql);
        Assert.DoesNotContain("SourceText", sql);
        Assert.Contains(commands.Parameters, value => Equals(value, 20));
        await Assert.ThrowsAsync<ValidationException>(() => lookup.ListAsync(Guid.NewGuid(), -1, 10, default));
    }

    private static JobHunterDbContext Create() => new(new DbContextOptionsBuilder<JobHunterDbContext>()
        .UseNpgsql("Host=localhost;Database=tests;Username=tests").Options);
}
