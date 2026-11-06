using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Infrastructure.JobProviders;

public static class PublicFeedMapping
{
    public static Guid CompanyId(string provider, string company) => new(SHA256.HashData(
        Encoding.UTF8.GetBytes($"public-company:{provider}:{company.Trim().ToUpperInvariant()}")).AsSpan(0, 16));
}

/// <summary>Fixed public endpoints only. Never follows pagination URLs supplied by a feed.</summary>
public abstract class PublicJobFeed(HttpClient client, IOptions<PublicFeedOptions> options) : IPublicJobFeed
{
    protected PublicFeedOptions Settings => options.Value;
    public abstract string Name { get; }
    public abstract bool Enabled { get; }
    protected abstract string Path(int page);
    protected abstract JsonElement Items(JsonElement root);
    protected abstract PublicJobListing Map(JsonElement item);
    protected virtual bool IsMetadata(JsonElement item) => false;
    protected virtual bool HasMore(JsonElement root) => false;

    public async Task<PublicJobFeedResult> SearchAsync(int limit, CancellationToken token)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Enabled) return new([], []);
        List<PublicJobListing> jobs = [];
        List<string> warnings = [];
        HashSet<Guid> seen = [];
        for (int page = 1; page <= Settings.MaxPages; page++)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(Settings.RequestTimeoutSeconds));
            using HttpRequestMessage request = new(HttpMethod.Get, Path(page));
            request.Headers.UserAgent.ParseAdd("JobHunterAI/1.0");
            request.Headers.Accept.ParseAdd("application/json");
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, deadline.Token);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(deadline.Token));
            JsonElement items = Items(document.RootElement);
            if (items.ValueKind != JsonValueKind.Array) throw new JsonException("Expected a job array.");
            int invalid = 0;
            int visited = 0;
            foreach (JsonElement item in items.EnumerateArray())
            {
                visited++;
                deadline.Token.ThrowIfCancellationRequested();
                try
                {
                    if (item.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a job object.");
                    if (IsMetadata(item)) continue;
                    PublicJobListing listing = Map(item);
                    if (seen.Add(listing.Job.Id)) jobs.Add(listing);
                }
                catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or InvalidOperationException)
                { invalid++; }
                if (jobs.Count >= limit) break;
            }
            if (invalid > 0) warnings.Add($"{Name}: skipped {invalid} invalid listings on page {page}.");
            bool more = HasMore(document.RootElement);
            if (jobs.Count >= limit)
            {
                if (more || visited < items.GetArrayLength())
                    warnings.Add($"{Name}: feed examination limit ({limit} unique postings) reached after {page} page(s); additional postings may exist. Saved criteria are applied locally, not by this feed API.");
                break;
            }
            if (page == Settings.MaxPages && more)
            {
                warnings.Add($"{Name}: page limit ({Settings.MaxPages}) reached after {jobs.Count} unique postings; additional postings may exist. Saved criteria are applied locally, not by this feed API.");
                break;
            }
            if (!more || items.GetArrayLength() == 0) break;
        }
        return new(jobs, warnings.ToArray());
    }

    protected PublicJobListing Listing(string id, string company, string title, string description,
        string? location, bool remote, string url, DateTimeOffset? postedAt)
    {
        company = company.Trim();
        if (company.Length is 0 or > 200 || title.Length > 300 || id.Length > 250 || description.Length > 500000 ||
            url.Length > 2048 || location?.Length > 200)
            throw new ArgumentException("Feed listing exceeds supported limits.");
        return new(company, ProviderMapping.Job(Name, "public", id, PublicFeedMapping.CompanyId(Name, company),
            title, ProviderMapping.PlainText(description), new JobLocation(location, null,
                remote ? WorkArrangement.Remote : null), url, postedAt));
    }

    protected static DateTimeOffset? UnixSeconds(JsonElement item, string property) =>
        item.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
}

public sealed class ArbeitnowFeed(HttpClient client, IOptions<PublicFeedOptions> options) : PublicJobFeed(client, options)
{
    public override string Name => "Arbeitnow";
    public override bool Enabled => Settings.Arbeitnow;
    protected override string Path(int page) => $"job-board-api?page={page.ToString(CultureInfo.InvariantCulture)}";
    protected override JsonElement Items(JsonElement root) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data)
        ? data : throw new JsonException("Missing job data.");
    protected override bool HasMore(JsonElement root) => root.TryGetProperty("links", out JsonElement links) && links.ValueKind == JsonValueKind.Object &&
        links.TryGetProperty("next", out JsonElement next) && next.ValueKind == JsonValueKind.String;
    protected override PublicJobListing Map(JsonElement item) => Listing(
        ProviderMapping.Required(item, "slug"), ProviderMapping.Required(item, "company_name"),
        ProviderMapping.Required(item, "title"), ProviderMapping.Required(item, "description"),
        ProviderMapping.Optional(item, "location"), item.TryGetProperty("remote", out JsonElement remote) && remote.ValueKind == JsonValueKind.True,
        ProviderMapping.Required(item, "url"), UnixSeconds(item, "created_at"));
}

public sealed class RemoteOkFeed(HttpClient client, IOptions<PublicFeedOptions> options) : PublicJobFeed(client, options)
{
    public override string Name => "RemoteOK";
    public override bool Enabled => Settings.RemoteOK;
    protected override string Path(int page) => "api";
    protected override JsonElement Items(JsonElement root) => root;
    protected override bool IsMetadata(JsonElement item) => item.TryGetProperty("legal", out _) && !item.TryGetProperty("id", out _);
    protected override PublicJobListing Map(JsonElement item) => Listing(
        ProviderMapping.Required(item, "id"), ProviderMapping.Required(item, "company"),
        ProviderMapping.Required(item, "position"), ProviderMapping.Required(item, "description"),
        ProviderMapping.Optional(item, "location"), true, ProviderMapping.Required(item, "url"),
        ProviderMapping.Timestamp(ProviderMapping.Optional(item, "date")));
}
