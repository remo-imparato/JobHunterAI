using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobHunterAI.Web;

public sealed class ApiReadinessCheck(IHttpClientFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        using HttpResponseMessage response = await client.GetAsync("health/ready", cancellationToken);
        return response.IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("API unavailable.");
    }
}
