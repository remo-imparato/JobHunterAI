using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobHunterAI.Infrastructure;

public sealed class JobHunterDbContextFactory : IDesignTimeDbContextFactory<JobHunterDbContext>
{
    public JobHunterDbContext CreateDbContext(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__JobHunterAI");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set ConnectionStrings__JobHunterAI for EF Core migrations.");
        }

        DbContextOptions<JobHunterDbContext> options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public"))
            .Options;
        return new JobHunterDbContext(options);
    }
}
