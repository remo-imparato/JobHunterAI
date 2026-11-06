using System.Net;
using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class LeverProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Lever";

    public async Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        board = ProviderMapping.Identifier(board, nameof(board));
        ProviderMapping.CompanyId(companyId);
        using HttpResponseMessage response = await httpClient.GetAsync($"v0/postings/{board}?mode=json", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        List<JobPosting> jobs = [];
        foreach (JsonElement element in document.RootElement.EnumerateArray())
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
        using HttpResponseMessage response = await httpClient.GetAsync($"v0/postings/{board}/{jobId}?mode=json", cancellationToken);
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
        string? location = element.TryGetProperty("categories", out JsonElement categories)
            ? ProviderMapping.Optional(categories, "location") : null;
        bool remote = string.Equals(ProviderMapping.Optional(element, "workplaceType"), "remote",
            StringComparison.OrdinalIgnoreCase);
        return ProviderMapping.Job(Name, board, ProviderMapping.Required(element, "id"), companyId,
            ProviderMapping.Required(element, "text"), ProviderMapping.Required(element, "descriptionPlain"),
            ProviderMapping.Location(location, remote, ProviderMapping.Arrangement(ProviderMapping.Optional(element, "workplaceType"))), ProviderMapping.Required(element, "hostedUrl"),
            ProviderMapping.UnixMilliseconds(element, "createdAt"));
    }
}
