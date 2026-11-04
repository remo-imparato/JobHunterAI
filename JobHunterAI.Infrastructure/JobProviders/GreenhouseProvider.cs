using System.Net;
using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class GreenhouseProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Greenhouse";

    public async Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        board = ProviderMapping.Identifier(board, nameof(board));
        ProviderMapping.CompanyId(companyId);
        using HttpResponseMessage response = await httpClient.GetAsync($"boards/{board}/jobs?content=true", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        List<JobPosting> jobs = [];
        foreach (JsonElement element in document.RootElement.GetProperty("jobs").EnumerateArray())
        {
            jobs.Add(Map(element, board, companyId));
        }

        return jobs;
    }

    public async Task<JobPosting?> GetJobDetailsAsync(string board, string jobId, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        board = ProviderMapping.Identifier(board, nameof(board));
        jobId = ProviderMapping.Identifier(jobId, nameof(jobId));
        ProviderMapping.CompanyId(companyId);
        using HttpResponseMessage response = await httpClient.GetAsync($"boards/{board}/jobs/{jobId}?content=true", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return Map(document.RootElement, board, companyId);
    }

    private JobPosting Map(JsonElement element, string board, Guid companyId)
    {
        string? location = element.TryGetProperty("location", out JsonElement locationElement)
            ? ProviderMapping.Optional(locationElement, "name") : null;
        return ProviderMapping.Job(Name, board, ProviderMapping.Required(element, "id"), companyId,
            ProviderMapping.Required(element, "title"),
            ProviderMapping.PlainText(ProviderMapping.Required(element, "content")),
            ProviderMapping.Location(location), ProviderMapping.Required(element, "absolute_url"),
            ProviderMapping.Timestamp(ProviderMapping.Optional(element, "first_published")));
    }
}
