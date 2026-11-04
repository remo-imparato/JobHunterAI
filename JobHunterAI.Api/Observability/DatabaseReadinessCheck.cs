using JobHunterAI.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobHunterAI.Api.Observability;

public sealed class DatabaseReadinessCheck(JobHunterDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database unavailable.");
    }
}
