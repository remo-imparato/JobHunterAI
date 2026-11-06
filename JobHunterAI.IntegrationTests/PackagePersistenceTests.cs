using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.IntegrationTests;

public sealed class PackagePersistenceTests
{
    [Fact]
    public void PackageModelProtectsReferencesAndHasUniqueSelectionAndConcurrency()
    {
        using var db = RepositoryQueryTests.Create(new());
        var entity = db.Model.FindEntityType(typeof(ApplicationPackage))!;
        Assert.All(entity.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        Assert.Equal(7, entity.GetForeignKeys().Count());
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "CandidateProfileId", "JobPostingId", "ResumeId" }));
        Assert.True(entity.FindProperty("xmin")!.IsConcurrencyToken);
        Assert.Contains("application_packages", db.Database.GenerateCreateScript());
        Assert.Contains(db.Database.GetMigrations(), x => x.EndsWith("AddApplicationPackages", StringComparison.Ordinal));
        Assert.False(db.Database.HasPendingModelChanges());
    }
    [Fact]
    public async Task ReadsAreCandidateScopedAndUseParameterizedSelection()
    {
        RepositoryQueryTests.CaptureCommands commands = new();
        await using var db = RepositoryQueryTests.Create(commands);
        PackageStore store = new(db);
        Guid candidate = Guid.NewGuid();
        Assert.Null(await store.GetAsync(candidate, Guid.NewGuid(), default));
        Assert.Contains("CandidateProfileId", commands.Sql[^1]);
        Assert.Contains(candidate, commands.Parameters);
        Assert.Null(await store.FindAsync(candidate, Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Contains("ResumeId", commands.Sql[^1]);
        Assert.Contains("JobPostingId", commands.Sql[^1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.HasMatchAsync(candidate, Guid.NewGuid(), default));
        Assert.Contains("job_discovery_runs", commands.Sql[^1]);
        Assert.Contains("CandidateProfileId", commands.Sql[^1]);
    }

    [Fact]
    public void NewPackageResumeVersionIsTrackedAsInsertOnExistingResume()
    {
        using var db = RepositoryQueryTests.Create(new());
        Resume resume = new(Guid.NewGuid(), Guid.NewGuid(), "Source");
        resume.AddUploadedSource(Guid.NewGuid(), "Original", "resume.docx", new string('a', 64), DateTimeOffset.UtcNow);
        db.Attach(resume);
        ResumeVersion version = resume.AddVersion(Guid.NewGuid(), "Tailored", DateTimeOffset.UtcNow, Guid.NewGuid());
        db.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Added, db.Entry(version).State);
    }
}
