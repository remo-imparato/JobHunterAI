using System.Data;
using System.Data.Common;
using JobHunterAI.Application.Dashboard;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunterAI.IntegrationTests;

/// <summary>Exercises Npgsql SQL generation with intercepted results, not a live PostgreSQL server.</summary>
public sealed class RepositoryQueryTests
{
    [Theory]
    [InlineData(DashboardResource.Jobs, "Greenhouse")]
    [InlineData(DashboardResource.Applications, "Draft")]
    [InlineData(DashboardResource.ResumeVersions, "Tailored")]
    [InlineData(DashboardResource.ResumeVersions, "Base")]
    [InlineData(DashboardResource.CoverLetters, "Lever")]
    [InlineData(DashboardResource.AutomationQueue, "Running")]
    public async Task Dashboard_PagesAndFiltersInSql(DashboardResource resource, string filter)
    {
        CaptureCommands commands = new();
        await using JobHunterDbContext db = Create(commands);
        DashboardPage page = await new DashboardReader(db).ReadAsync(resource,
            new DashboardQuery(Guid.NewGuid(), 2, 10, "Architect", filter));
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(2, commands.Sql.Count);
        Assert.Contains("count(*)", commands.Sql[0]);
        Assert.Contains("LIMIT", commands.Sql[1]);
        Assert.Contains("OFFSET", commands.Sql[1]);
        Assert.Contains("ORDER BY", commands.Sql[1]);
        if (resource != DashboardResource.Jobs) Assert.Contains("CandidateProfileId", commands.Sql[0]);
        Assert.Contains(commands.Parameters, value => Equals(value, 20));
        Assert.Contains(commands.Parameters, value => Equals(value, 10));
    }

    [Fact]
    public async Task Dashboard_SummaryAggregatesWithoutLoadingEntities()
    {
        CaptureCommands commands = new();
        await using JobHunterDbContext db = Create(commands);
        DashboardSummary summary = await new DashboardReader(db).SummarizeAsync(Guid.NewGuid());
        Assert.Equal(0, summary.Jobs);
        Assert.Equal(0, summary.Applications);
        Assert.Empty(summary.ApplicationStatuses);
        Assert.Equal(6, commands.Sql.Count);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Repository_ReadsUseStablePagingAndParameterizedLookup()
    {
        CaptureCommands commands = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost;Database=tests;Username=tests");
        services.AddScoped(_ => Create(commands));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Company>>();
        Assert.Null(await repository.GetByIdAsync(Guid.NewGuid()));
        Assert.Empty(await repository.ListAsync(5, 10));
        Assert.Contains("ORDER BY", commands.Sql[1]);
        Assert.Contains("LIMIT", commands.Sql[1]);
        var applications = scope.ServiceProvider.GetRequiredService<IApplicationLookup>();
        Assert.Empty(await applications.ListByCandidateAsync(Guid.NewGuid(), 0, 10));
        Assert.Null(await applications.FindByCandidateAndJobAsync(Guid.NewGuid(), Guid.NewGuid()));
        var jobs = scope.ServiceProvider.GetRequiredService<IJobPostingLookup>();
        const string untrusted = "x' OR 1=1 --";
        Assert.Null(await jobs.FindByProviderAsync("Lever", untrusted));
        Assert.DoesNotContain(untrusted, commands.Sql[^1]);
        Assert.Contains(untrusted, commands.Parameters);
    }

    [Fact]
    public async Task Repository_AddAndRemoveTrackEntitiesAndRejectNull()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure("Host=localhost;Database=tests;Username=tests");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Company>>();
        var db = scope.ServiceProvider.GetRequiredService<JobHunterDbContext>();
        Company company = new(Guid.NewGuid(), "Acme");
        repository.Add(company);
        Assert.Equal(EntityState.Added, db.Entry(company).State);
        repository.Remove(company);
        Assert.Equal(EntityState.Detached, db.Entry(company).State);
        Assert.Throws<ArgumentNullException>(() => repository.Add(null!));
        Assert.Throws<ArgumentNullException>(() => repository.Remove(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetByIdAsync(Guid.Empty));
    }

    internal static JobHunterDbContext Create(CaptureCommands commands) => new(new DbContextOptionsBuilder<JobHunterDbContext>()
        .UseNpgsql("Host=localhost;Database=tests;Username=tests")
        .AddInterceptors(new SuppressConnection(), commands).Options);

    private sealed class SuppressConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    internal sealed class CaptureCommands : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object?> Parameters { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sql.Add(command.CommandText);
            foreach (DbParameter parameter in command.Parameters) Parameters.Add(parameter.Value);
            DataTable table = new();
            if (command.CommandText.StartsWith("SELECT count(*)", StringComparison.Ordinal))
            {
                table.Columns.Add("count", typeof(int));
                table.Rows.Add(0);
            }
            else
            {
                // Split-query buffering inspects field metadata even when the result has no rows.
                for (int i = 0; i < 64; i++) table.Columns.Add("column" + i, typeof(object));
            }
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
