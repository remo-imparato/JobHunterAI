using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class AshbyProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Ashby";

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
        IReadOnlyList<JobPosting> jobs = await SearchJobsAsync(board, companyId, null, cancellationToken);
        foreach (JobPosting job in jobs)
        {
            if (job.ExternalId == $"{board}:{jobId}")
            {
                return job;
            }
        }

        return null;
    }

    private static IEnumerable<string> SearchPaths(string board, JobProviderSearchCriteria? criteria)
    {
        const int maxHints = 20;
        string basePath = $"job-board/{board}";
        if (criteria is null || (criteria.Terms.Length == 0 && criteria.Locations.Length == 0))
        {
            yield return basePath;
            yield break;
        }

        HashSet<string> paths = [basePath];
        foreach (string term in criteria.Terms.Take(maxHints))
            paths.Add($"{basePath}?query={Uri.EscapeDataString(term)}");
        foreach (string location in criteria.Locations.Take(maxHints))
            paths.Add($"{basePath}?location={Uri.EscapeDataString(location)}");
        foreach (string path in paths) yield return path;
    }

    private JobPosting Map(JsonElement element, string board, Guid companyId)
    {
        bool remote = element.TryGetProperty("isRemote", out JsonElement remoteElement) &&
            remoteElement.ValueKind == JsonValueKind.True;
        return ProviderMapping.Job(Name, board, ProviderMapping.Required(element, "id"), companyId,
            ProviderMapping.Required(element, "title"), ProviderMapping.Required(element, "descriptionPlain"),
            ProviderMapping.Location(ProviderMapping.Optional(element, "location"), remote,
                ProviderMapping.Arrangement(ProviderMapping.Optional(element, "workplaceType"))),
            ProviderMapping.Required(element, "applyUrl"),
            ProviderMapping.Timestamp(ProviderMapping.Optional(element, "publishedAt")));
    }
}
