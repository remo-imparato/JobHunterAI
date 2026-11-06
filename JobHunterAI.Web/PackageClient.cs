using System.Net.Http.Json;
using JobHunterAI.Application.Generation;

namespace JobHunterAI.Web;

public sealed class PackageClient(OnboardingClient client)
{
    public Task<PackageView> GenerateAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token) =>
        client.SendAsync<PackageView>(HttpMethod.Post, $"api/candidates/{candidateId}/packages", JsonContent.Create(new { jobId, resumeId }), token);
    public Task<PackageView> FindAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token) =>
        client.SendAsync<PackageView>(HttpMethod.Get, $"api/candidates/{candidateId}/packages/selection?jobId={jobId}&resumeId={resumeId}", null, token);
}
