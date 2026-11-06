extern alias WebHost;
using System.Net;
using System.Net.Http.Headers;
using JobHunterAI.Application.Generation;
using WebHost::JobHunterAI.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunterAI.IntegrationTests;

public sealed class PackageDownloadTests
{
    [Theory]
    [InlineData(200, "application/zip", 200)]
    [InlineData(200, "text/plain", 502)]
    [InlineData(404, "text/plain", 404)]
    [InlineData(409, "text/plain", 409)]
    [InlineData(500, "text/plain", 502)]
    public async Task ProxyPreservesSafeAttachmentAndSanitizesBackendErrors(int status, string type, int expected)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHttpClientFactory>(new Factory(status, type));
        await using var app = builder.Build();
        app.MapPackageDownloads();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync($"/packages/{Guid.NewGuid()}/{Guid.NewGuid()}/download");
        Assert.Equal(expected, (int)response.StatusCode);
        if (expected == 200)
        {
            Assert.Equal("Acme-Engineer.zip", response.Content.Headers.ContentDisposition!.FileNameStar);
            Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        }
        else Assert.DoesNotContain("private backend content", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ProxyRejectsOversizedArchive()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IHttpClientFactory>(new Factory(200, "application/zip", true));
        await using var app = builder.Build();
        app.MapPackageDownloads();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.GetAsync($"/packages/{Guid.NewGuid()}/{Guid.NewGuid()}/download");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
    private sealed class Factory(int status, string type, bool large = false) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(status, type, large)) { BaseAddress = new Uri("https://example.invalid/") };
    }
    private sealed class Handler(int status, string type, bool large) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(large ? new byte[PackageZip.MaxArchiveBytes + 1] : [1, 2, 3]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileNameStar = "Acme-Engineer.zip" };
            return Task.FromResult(response);
        }
    }
}
