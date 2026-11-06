using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;

namespace JobHunterAI.Web;

public sealed class CandidateClient(IHttpClientFactory factory)
{
    public async Task<CandidatePage> SearchAsync(string? search, int page, int pageSize, CancellationToken token = default)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        using HttpResponseMessage response = await client.GetAsync(
            $"api/candidates?search={Uri.EscapeDataString(search ?? string.Empty)}&page={page}&pageSize={pageSize}", token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CandidatePage>(token)
            ?? throw new HttpRequestException("Empty candidate response.");
    }

    public async Task<CandidateDto> GetAsync(Guid id, CancellationToken token = default)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        using HttpResponseMessage response = await client.GetAsync($"api/candidates/{id}", token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CandidateDto>(token)
            ?? throw new HttpRequestException("Empty candidate response.");
    }

    public async Task<CandidateDto> SaveAsync(Guid? id, CandidateInput input, CancellationToken token = default)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        using HttpResponseMessage response = id.HasValue
            ? await client.PutAsJsonAsync($"api/candidates/{id}", input, token)
            : await client.PostAsJsonAsync("api/candidates", input, token);
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<CandidateDto>(token)
            ?? throw new HttpRequestException("Empty candidate response.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        using HttpClient client = factory.CreateClient("JobHunterAI.Api");
        using HttpResponseMessage response = await client.DeleteAsync($"api/candidates/{id}", token);
        await EnsureSuccessAsync(response, token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        string message = response.StatusCode switch
        {
            System.Net.HttpStatusCode.NotFound => "Candidate not found. Return to candidate selection and refresh the results.",
            System.Net.HttpStatusCode.Conflict => "The profile changed or has related records. Reload and try again.",
            System.Net.HttpStatusCode.BadRequest => "Check the profile fields and try again.",
            _ => "The candidate service is unavailable. Check that the API is running and its database migrations are applied."
        };
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            ApiProblem? problem = await response.Content.ReadFromJsonAsync<ApiProblem>(token);
            if (!string.IsNullOrWhiteSpace(problem?.Title)) message = problem.Title;
        }
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private sealed record ApiProblem(string? Title);
}
