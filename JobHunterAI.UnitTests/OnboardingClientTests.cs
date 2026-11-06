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

    [Theory]
    [InlineData(3)]
    [InlineData(ResumeUploadLimits.MaxFileBytes)]
    public async Task UploadAsync_NonSeekableStream_CanSerializeMultipartTwice(int size)
    {
        byte[] bytes = new byte[size];
        bytes[0] = 42;
        using NonSeekableStream stream = new(bytes);
        ReplayFactory factory = new();

        await new OnboardingClient(factory).UploadAsync(Guid.NewGuid(), stream, "../resume.pdf", default);

        Assert.Equal(1, factory.SendCount);
        Assert.Equal(factory.FirstBody, factory.SecondBody);
        Assert.True(factory.FirstBody!.AsSpan().IndexOf(bytes) >= 0);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task UploadAsync_OverLimitNonSeekableStream_IsRejectedBeforeSending()
    {
        using NonSeekableStream stream = new(new byte[ResumeUploadLimits.MaxFileBytes + 1]);
        ReplayFactory factory = new();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new OnboardingClient(factory).UploadAsync(Guid.NewGuid(), stream, "resume.pdf", default));

        Assert.Equal(0, factory.SendCount);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task UploadAsync_CanceledBuffering_DoesNotSendRequest()
    {
        using NonSeekableStream stream = new([1, 2, 3]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ReplayFactory factory = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OnboardingClient(factory).UploadAsync(Guid.NewGuid(), stream, "resume.pdf", cancellation.Token));

        Assert.Equal(0, factory.SendCount);
        Assert.True(stream.Disposed);
    }

    // Unlike ReadAsStringAsync, CopyToAsync does not implicitly buffer HttpContent.
    // Serializing twice exercises the replay required by a 307/308 redirect.
    private sealed class ReplayFactory : IHttpClientFactory
    {
        public int SendCount { get; private set; }
        public byte[]? FirstBody { get; private set; }
        public byte[]? SecondBody { get; private set; }

        public HttpClient CreateClient(string name) => new(new Handler(async (request, token) =>
        {
            SendCount++;
            using MemoryStream first = new();
            using MemoryStream second = new();
            await request.Content!.CopyToAsync(first, token);
            await request.Content.CopyToAsync(second, token);
            FirstBody = first.ToArray();
            SecondBody = second.ToArray();
            ResumeSummary summary = new(Guid.NewGuid(), "resume.pdf", Guid.NewGuid(), "resume.pdf", DateTimeOffset.UtcNow);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ResumePreview(summary, "Synthetic test text", new([], [], [], [])))
            };
        })) { BaseAddress = new Uri("https://example.invalid/") };
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            Disposed = true;
            base.Dispose(disposing);
        }
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
