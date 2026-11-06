using System.Data;
using System.Data.Common;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace JobHunterAI.IntegrationTests;

public sealed class DiscoveryUpsertTests
{
    [Fact]
    public async Task Upsert_UsesParameterizedIdentityConflictUpdateAndReturnsCanonicalId()
    {
        Guid canonicalId = Guid.NewGuid();
        Commands commands = new(canonicalId);
        await using JobHunterDbContext db = new(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql("Host=localhost;Database=tests;Username=tests").AddInterceptors(new Connection(), commands).Options);
        JobPosting input = new(Guid.NewGuid(), commands.Company, "Lever", "acme:1", "Engineer", "Build ' safely", new("London", null, WorkArrangement.Hybrid), "https://example.invalid/job");
        JobPosting stored = await new DiscoveryStore(db).UpsertJobAsync(input, default);
        Assert.Equal(canonicalId, stored.Id);
        Assert.Equal(WorkArrangement.Hybrid, stored.Location.Arrangement);
        Assert.Contains("ON CONFLICT (\"ProviderName\", \"ExternalId\") DO UPDATE", commands.Sql);
        Assert.DoesNotContain("Build ' safely", commands.Sql);
        Assert.Contains(input.Description, commands.Parameters);
        Assert.Contains((int)WorkArrangement.Hybrid, commands.Parameters);
        Assert.DoesNotContain("xmin", commands.Sql);
        Assert.Empty(db.ChangeTracker.Entries());
    }
    private sealed class Connection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }
    private sealed class Commands(Guid canonicalId) : DbCommandInterceptor
    {
        public Guid Company { get; } = Guid.NewGuid();
        public string Sql { get; private set; } = "";
        public List<object?> Parameters { get; } = [];
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Sql = command.CommandText;
            foreach (DbParameter parameter in command.Parameters) Parameters.Add(parameter.Value);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            DataTable table = new();
            object[] values = [canonicalId, "https://example.invalid/job", Company, "Build ' safely", "acme:1", DBNull.Value, "Lever", "Engineer", (uint)1, (int)WorkArrangement.Hybrid, "London", DBNull.Value, false];
            for (int i = 0; i < values.Length; i++) table.Columns.Add("c" + i, values[i] is DBNull ? typeof(object) : values[i].GetType());
            table.Rows.Add(values);
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
