using FluentValidation;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunterAI.IntegrationTests;

public sealed class CandidatePersistenceTests
{
    [Fact]
    public async Task Search_UsesParameterizedStableServerSidePagingWithoutLoadingAggregates()
    {
        RepositoryQueryTests.CaptureCommands commands = new();
        await using JobHunterDbContext db = RepositoryQueryTests.Create(commands);
        CandidatePage page = await new CandidateLookup(db).SearchAsync(new("Architect", 2, 10), default);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(2, commands.Sql.Count);
        Assert.Contains("count(*)", commands.Sql[0]);
        Assert.Contains("lower(", commands.Sql[0]);
        Assert.Contains("Username", commands.Sql[0]);
        Assert.Contains("ORDER BY", commands.Sql[1]);
        Assert.Contains("LIMIT", commands.Sql[1]);
        Assert.Contains("OFFSET", commands.Sql[1]);
        Assert.DoesNotContain("JOIN", commands.Sql[1]);
        Assert.Contains(commands.Parameters, value => Equals(value, 20));
        Assert.Contains(commands.Parameters, value => Equals(value, 10));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Search_RejectsInvalidInputBeforeDatabaseAccess()
    {
        RepositoryQueryTests.CaptureCommands commands = new();
        await using JobHunterDbContext db = RepositoryQueryTests.Create(commands);
        CandidateLookup lookup = new(db);
        await Assert.ThrowsAsync<ValidationException>(() => lookup.SearchAsync(new(Page: -1), default));
        await Assert.ThrowsAsync<ValidationException>(() => lookup.SearchAsync(new(new string('x', 201)), default));
        Assert.Empty(commands.Sql);
    }

    [Fact]
    public void Model_EnforcesCanonicalUniqueUsernameAndProtectsCandidateHistory()
    {
        using JobHunterDbContext db = Create();
        var profile = db.Model.FindEntityType(typeof(CandidateProfile))!;
        Assert.Contains(profile.GetIndexes(), index => index.IsUnique &&
            index.Properties.Single().Name == nameof(CandidateProfile.Username));
        Assert.False(profile.FindProperty(nameof(CandidateProfile.Username))!.IsNullable);
        Assert.Equal(64, profile.FindProperty(nameof(CandidateProfile.Username))!.GetMaxLength());
        foreach (Type dependent in new[] { typeof(Resume), typeof(CoverLetter), typeof(JobHunterAI.Domain.Application), typeof(AgentExecution), typeof(JobMatchScore) })
        {
            Assert.Equal(DeleteBehavior.Restrict, db.Model.FindEntityType(dependent)!.GetForeignKeys()
                .Single(key => key.PrincipalEntityType.ClrType == typeof(CandidateProfile)).DeleteBehavior);
        }
        string sql = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE UNIQUE INDEX \"IX_candidate_profiles_Username\"", sql);
        Assert.Contains("CHECK (\"Username\" ~ '^[a-z0-9._-]{1,64}$')", sql);
    }

    [Fact]
    public void Migration_BackfillsExistingRowsBeforeConstraintsAndMatchesSnapshot()
    {
        using JobHunterDbContext db = Create();
        string sql = db.GetService<IMigrator>().GenerateScript("20261104180801_AddJobMatchScores", "20261105182354_AddCandidateUsernames");
        int update = sql.IndexOf("UPDATE public.candidate_profiles", StringComparison.Ordinal);
        int required = sql.IndexOf("SET NOT NULL", StringComparison.Ordinal);
        int unique = sql.IndexOf("CREATE UNIQUE INDEX", StringComparison.Ordinal);
        Assert.True(update >= 0 && required > update && unique > required);
        Assert.Contains("'candidate-' || replace(\"Id\"::text, '-', '')", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    private static JobHunterDbContext Create() => new(new DbContextOptionsBuilder<JobHunterDbContext>()
        .UseNpgsql("Host=localhost;Database=tests;Username=tests").Options);
}
