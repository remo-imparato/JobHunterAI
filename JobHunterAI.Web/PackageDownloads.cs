using System.Net;
using JobHunterAI.Application.Generation;

namespace JobHunterAI.Web;

public static class PackageDownloads
{
    public static IEndpointRouteBuilder MapPackageDownloads(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/packages/{candidateId:guid}/{packageId:guid}/download", async (Guid candidateId, Guid packageId, IHttpClientFactory factory, HttpContext context, CancellationToken token) =>
        {
            using HttpClient client = factory.CreateClient("JobHunterAI.Api");
            using HttpResponseMessage response = await client.GetAsync($"api/candidates/{candidateId}/packages/{packageId}/download", HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return Results.Problem(statusCode: response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict ? (int)response.StatusCode : 502, title: "Package download unavailable. Refresh its status and try again.");
            if (response.Content.Headers.ContentType?.MediaType != "application/zip" || response.Content.Headers.ContentLength > PackageZip.MaxArchiveBytes)
                return Results.Problem(statusCode: 502, title: "Invalid package download response.");
            await using Stream source = await response.Content.ReadAsStreamAsync(token);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + read > PackageZip.MaxArchiveBytes) return Results.Problem(statusCode: 502, title: "Package exceeds download limits.");
                await buffer.WriteAsync(chunk.AsMemory(0, read), token);
            }
            string raw = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "application";
            string safe = raw.Length <= 124 && raw.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                !raw.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) ? raw : "application-package.zip";
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(buffer.ToArray(), "application/zip", safe);
        });
        return endpoints;
    }
}
