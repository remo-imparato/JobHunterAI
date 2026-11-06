using System.Data.Common;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JobHunterAI.IntegrationTests;

public sealed class PackageLeaseTests
{
    [Fact]
    public async Task LeaseAndFailureUpdatesAreAtomicCandidateScopedAndDoNotSaveUncommittedOutputs()
    {
        Commands commands = new();
        await using JobHunterDbContext db = new(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql("Host=localhost;Database=tests;Username=tests").AddInterceptors(new Connection(), commands).Options);
        Guid candidate = Guid.NewGuid();
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Lever", "acme:1", "Engineer", "Build", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
        ApplicationPackage package = new(Guid.NewGuid(), candidate, Guid.NewGuid(), Guid.NewGuid(), job, "Acme", "Original");
        PackageStore store = new(db);
        Assert.False(await store.AcquireAsync(package, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(3), default));
        Assert.Contains("UPDATE", commands.Sql[0]);
        Assert.Contains("LeaseExpiresAt", commands.Sql[0]);
        Assert.Contains("CandidateProfileId", commands.Sql[0]);
        Assert.Contains(candidate, commands.Parameters);
        db.Attach(package);
        Guid lease = Guid.NewGuid();
        package.Begin(lease, DateTimeOffset.UtcNow.AddMinutes(3));
        package.SaveResume(lease, Guid.NewGuid(), "Unsaved");
        await store.FailAsync(package.Id, candidate, lease, "private prompt content", default);
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Contains("LeaseId", commands.Sql[1]);
        Assert.DoesNotContain("private prompt content", commands.Parameters);
        Assert.Contains("GenerationFailed", commands.Parameters);
    }
    private sealed class Connection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?> Parameters { get; } = [];
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            foreach (DbParameter p in command.Parameters) Parameters.Add(p.Value);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }
    }
}
