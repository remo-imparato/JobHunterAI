using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

public sealed class AshbyProvider(HttpClient httpClient) : IJobProvider
{
    public string Name => "Ashby";

    public async Task<IReadOnlyList<JobPosting>> SearchJobsAsync(string board, Guid companyId,
        CancellationToken cancellationToken = default)
    {
        board = ProviderMapping.Identifier(board, nameof(board));
        ProviderMapping.CompanyId(companyId);
        using HttpResponseMessage response = await httpClient.GetAsync($"job-board/{board}", cancellationToken);
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
        IReadOnlyList<JobPosting> jobs = await SearchJobsAsync(board, companyId, cancellationToken);
        foreach (JobPosting job in jobs)
        {
            if (job.ExternalId == $"{board}:{jobId}")
            {
                return job;
            }
        }

        return null;
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
