using FluentValidation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using JobHunterAI.Infrastructure.Background;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;

namespace JobHunterAI.IntegrationTests;

public sealed class DiscoveryPersistenceTests
{
    [Fact]
    public void Model_RestrictsCandidateAndJobDeletionAndEnforcesUniqueRunMatches()
    {
        using var db = RepositoryQueryTests.Create(new());
        var run = db.Model.FindEntityType(typeof(JobDiscoveryRun))!;
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(run.GetForeignKeys()).DeleteBehavior);
        var result = db.Model.FindEntityType(typeof(JobDiscoveryResult))!;
        Assert.Contains(result.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "RunId", "JobPostingId" }));
        Assert.Contains(result.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(JobPosting) && x.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(result.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == typeof(JobDiscoveryRun) && x.DeleteBehavior == DeleteBehavior.Cascade);
        Assert.Equal(ValueGenerated.Never, result.FindProperty("Id")!.ValueGenerated);
        string script = db.Database.GenerateCreateScript();
        Assert.Contains("job_discovery_runs", script);
        Assert.Contains("WorkArrangement", script);
        Assert.Contains(db.Database.GetMigrations(), migration => migration.EndsWith("AddCandidateDiscovery", StringComparison.Ordinal));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Store_UsesCandidateScopedParameterizedPagingAndBoundedPostingReads()
    {
        RepositoryQueryTests.CaptureCommands commands = new();
        await using var db = RepositoryQueryTests.Create(commands);
        DiscoveryStore store = new(db);
        Guid candidate = Guid.NewGuid();
        Assert.Empty(await store.ListAsync(candidate, 2, 10, default));
        Assert.Contains("CandidateProfileId", commands.Sql[^1]);
        Assert.Contains("ORDER BY", commands.Sql[^1]);
        Assert.Contains("OFFSET", commands.Sql[^1]);
        Assert.Contains(candidate, commands.Parameters);
        Assert.Contains(20, commands.Parameters);
        Assert.Null(await store.DetailsAsync(candidate, Guid.NewGuid(), default));
        Assert.Contains("CandidateProfileId", commands.Sql[^1]);
        Assert.Empty(await store.StoredJobsAsync(5, default));
        Assert.Contains("LIMIT", commands.Sql[^1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompanyExistsAsync(Guid.NewGuid(), default));
        Assert.Contains("EXISTS", commands.Sql[^1]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.StoredJobsAsync(0, default));
        await Assert.ThrowsAsync<ValidationException>(() => store.ListAsync(candidate, -1, 10, default));
        await Assert.ThrowsAsync<ValidationException>(() => store.DetailsAsync(Guid.Empty, Guid.Empty, default));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public void Sources_AreAvailableWithRecurringProcessingDisabled()
    {
        Guid company = Guid.NewGuid();
        var sources = new DiscoverySources(Options.Create(new BackgroundOptions { Enabled = false, Boards = [new() { Provider = "Lever", Board = "acme", CompanyId = company }] }));
        Assert.Equal(new DiscoverySource("Lever", "acme", company), Assert.Single(sources.Boards));
    }

    [Fact]
    public async Task Upsert_RejectsOversizedProviderFieldsBeforeDatabaseAccess()
    {
        await using var db = RepositoryQueryTests.Create(new());
        DiscoveryStore store = new(db);
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Lever", "acme:1", new string('x', 301), "Build", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertJobAsync(job, default));
    }

    [Fact]
    public void ScheduledCandidateQuery_RequiresCompletedCandidateAssociatedMatches()
    {
        using var db = RepositoryQueryTests.Create(new());
        var jobs = new BackgroundJobs(db, [], null!, null!, Options.Create(new BackgroundOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BackgroundJobs>.Instance, new DiscoveryStore(db));
        Guid candidate = Guid.NewGuid();
        var method = typeof(BackgroundJobs).GetMethod("CandidateMatches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var query = (IQueryable<JobPosting>)method.Invoke(jobs, [candidate])!;
        string sql = query.ToQueryString();
        Assert.Contains("job_discovery_results", sql);
        Assert.Contains("job_discovery_runs", sql);
        Assert.Contains("CandidateProfileId", sql);
        Assert.Contains(candidate.ToString(), sql);
        Assert.Contains("Status", sql);
        Assert.Contains("IN (1, 2)", sql);
        CandidateSearchCriteria criteria = new(candidate, ["Engineer"], [], WorkArrangement.Remote, false, [], []);
        JobPosting matching = new(Guid.NewGuid(), Guid.NewGuid(), "Lever", "acme:1", "Engineer", "Build", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
        var matches = typeof(BackgroundJobs).GetMethod("Matches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)matches.Invoke(null, [criteria, matching])!);
        criteria.Update(["Sales"], [], WorkArrangement.Remote, false, [], []);
        Assert.False((bool)matches.Invoke(null, [criteria, matching])!);
    }
}
