using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using JobHunterAI.Web;

namespace JobHunterAI.UnitTests;

public sealed class OnboardingClientTests
{
    [Fact]
    public async Task ResumeMethods_UseCandidateScopedRoutesAndMultipartFileField()
    {
        Guid candidate = Guid.NewGuid();
        ResumeSummary summary = new(Guid.NewGuid(), "resume.docx", Guid.NewGuid(), "resume.docx", DateTimeOffset.UtcNow);
        ResumePreview preview = new(summary, "Original text", new([], [], [], []));
        Factory factory = new((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.Method == HttpMethod.Get && request.RequestUri!.Query.Length > 0
                ? JsonContent.Create(new[] { summary }) : JsonContent.Create(preview)
        }));
        OnboardingClient client = new(factory);
        Assert.Single(await client.ListResumesAsync(candidate, 2, default));
        Assert.Equal($"/api/candidates/{candidate}/resumes?page=2&pageSize=10", factory.LastUri!.PathAndQuery);
        Assert.Equal(summary.Id, (await client.PreviewAsync(candidate, summary.Id, default)).Resume.Id);
        Assert.EndsWith($"/resumes/{summary.Id}", factory.LastUri.AbsolutePath);
        await client.ImportAsync(candidate, summary.Id, default);
        Assert.Equal(HttpMethod.Post, factory.LastMethod);
        Assert.EndsWith("/import", factory.LastUri.AbsolutePath);
        using MemoryStream stream = new([1, 2, 3]);
        await client.UploadAsync(candidate, stream, "../resume.docx", default);
        Assert.Contains("name=file", factory.LastBody!.Replace("\"", ""));
        Assert.Contains("resume.docx", factory.LastBody);
        Assert.DoesNotContain("../", factory.LastBody);
    }

    [Fact]
    public async Task CriteriaMethods_UseTypedCandidateGetAndPut()
    {
        SearchCriteriaInput criteria = new(["Engineer"], [], WorkArrangement.Remote, true, [], []);
        Factory factory = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(criteria) }));
        OnboardingClient client = new(factory);
        Guid candidate = Guid.NewGuid();
        Assert.Equal("Engineer", Assert.Single((await client.GetCriteriaAsync(candidate, default)).JobTitles));
        await client.SaveCriteriaAsync(candidate, criteria, default);
        Assert.Equal(HttpMethod.Put, factory.LastMethod);
        Assert.Equal($"/api/candidates/{candidate}/search-criteria", factory.LastUri!.AbsolutePath);
        Assert.Contains("jobTitles", factory.LastBody!);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType)]
    public async Task ExpectedApiErrors_KeepActionableTitles(HttpStatusCode status)
    {
        Factory factory = new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = JsonContent.Create(new { title = "Upload a text-based PDF." }) }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new OnboardingClient(factory).PreviewAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal("Upload a text-based PDF.", error.Message);
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid JSON")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "private backend details")]
    [InlineData(HttpStatusCode.OK, "null")]
    public async Task InvalidOrUnexpectedResponses_FailSafely(HttpStatusCode status, string body)
    {
        Factory factory = new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => new OnboardingClient(factory).GetCriteriaAsync(Guid.NewGuid(), default));
        Assert.DoesNotContain("private backend details", error.Message);
    }

    private sealed class Factory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : IHttpClientFactory
    {
        public Uri? LastUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastBody { get; private set; }
        public HttpClient CreateClient(string name) => new(new Handler(async (request, token) =>
        {
            Assert.Equal("JobHunterAI.Api", name);
            LastUri = request.RequestUri;
            LastMethod = request.Method;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            return await send(request, token);
        })) { BaseAddress = new Uri("https://example.invalid/") };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
