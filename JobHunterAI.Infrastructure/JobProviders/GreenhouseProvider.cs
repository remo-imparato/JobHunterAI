using System.Net;
using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class GreenhouseProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Greenhouse";

    public async Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId,
        JobProviderSearchCriteria? criteria = null, CancellationToken cancellationToken = default)
    {
        board = ProviderMapping.Identifier(board, nameof(board));
        ProviderMapping.CompanyId(companyId);
        List<JobPosting> jobs = [];
        HashSet<Guid> seen = [];
        Exception? firstFailure = null;
        bool success = false;
        foreach (string requestPath in SearchPaths(board, criteria))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpResponseMessage response = await httpClient.GetAsync(requestPath, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                foreach (JsonElement element in document.RootElement.GetProperty("jobs").EnumerateArray())
                {
                    JobPosting mapped = Map(element, board, companyId);
                    if (seen.Add(mapped.Id)) jobs.Add(mapped);
                }

                success = true;
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                firstFailure ??= exception;
            }
        }

        if (!success && firstFailure is not null) throw firstFailure;
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

    private static IEnumerable<string> SearchPaths(string board, JobProviderSearchCriteria? criteria)
    {
        const int maxHints = 20;
        string basePath = $"boards/{board}/jobs?content=true";
        if (criteria is null || (criteria.Terms.Length == 0 && criteria.Locations.Length == 0))
        {
            yield return basePath;
            yield break;
        }

        HashSet<string> paths = [basePath];
        foreach (string term in criteria.Terms.Take(maxHints))
            paths.Add($"{basePath}&query={Uri.EscapeDataString(term)}");
        foreach (string location in criteria.Locations.Take(maxHints))
            paths.Add($"{basePath}&location={Uri.EscapeDataString(location)}");
        foreach (string path in paths) yield return path;
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
