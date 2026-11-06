using System.Net.Http.Json;
using JobHunterAI.Application.Jobs;

namespace JobHunterAI.Web;

public sealed class DiscoveryClient(OnboardingClient client)
{
    public Task<QueuedDiscovery> QueueAsync(Guid candidateId, Guid runId, CancellationToken token) =>
        client.SendAsync<QueuedDiscovery>(HttpMethod.Post, $"api/candidates/{candidateId}/discovery/background", JsonContent.Create(new { runId }), token);
    public Task<DiscoveryRunDetails> StartAsync(Guid candidateId, Guid runId, CancellationToken token) =>
        client.SendAsync<DiscoveryRunDetails>(HttpMethod.Post, $"api/candidates/{candidateId}/discovery", JsonContent.Create(new { runId }), token);
    public Task<IReadOnlyList<DiscoveryRunSummary>> ListAsync(Guid candidateId, int page, CancellationToken token) =>
        client.SendAsync<IReadOnlyList<DiscoveryRunSummary>>(HttpMethod.Get, $"api/candidates/{candidateId}/discovery?page={page}&pageSize=10", null, token);
    public Task<DiscoveryRunDetails> DetailsAsync(Guid candidateId, Guid runId, CancellationToken token) =>
        client.SendAsync<DiscoveryRunDetails>(HttpMethod.Get, $"api/candidates/{candidateId}/discovery/{runId}", null, token);
}
