using System.Net.Http.Json;
using JobHunterAI.Application.Dashboard;

namespace JobHunterAI.Web;

public sealed class DashboardPreferences
{
    public Guid CandidateId { get; set; }
    public int PageSize { get; set; } = 10;
}

public sealed class DashboardClient(IHttpClientFactory factory)
{
    public async Task<DashboardPage> ReadAsync(DashboardResource resource, DashboardQuery query, CancellationToken token)
    {
        query.Validate(resource);
        string path = $"api/dashboard/{resource}?candidateId={query.CandidateId}&page={query.Page}&pageSize={query.PageSize}" +
            $"&search={Uri.EscapeDataString(query.Search ?? string.Empty)}&filter={Uri.EscapeDataString(query.Filter ?? string.Empty)}";
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        return await client.GetFromJsonAsync<DashboardPage>(path, token) ?? throw new HttpRequestException("Empty dashboard response.");
    }

    public async Task<DashboardSummary> SummarizeAsync(Guid candidateId, CancellationToken token)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        return await client.GetFromJsonAsync<DashboardSummary>($"api/dashboard/summary?candidateId={candidateId}", token)
            ?? throw new HttpRequestException("Empty summary response.");
    }
}
