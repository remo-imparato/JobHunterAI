using System.Net;
using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class LeverProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Lever";

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
                foreach (JsonElement element in document.RootElement.EnumerateArray())
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

    private static IEnumerable<string> SearchPaths(string board, JobProviderSearchCriteria? criteria)
    {
        const int maxHints = 20;
        string basePath = $"v0/postings/{board}?mode=json";
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
