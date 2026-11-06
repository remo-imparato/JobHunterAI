using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobHunterAI.IntegrationTests;

public sealed class PackageApiTests
{
    [Fact]
    public async Task GenerateStatusRetryAndZipAreCandidateScopedAndSnapshotConsistent()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = $"/api/candidates/{factory.Candidate.Id}/packages";
        using var generate = await client.PostAsJsonAsync(path, new { jobId = factory.Job.Id, resumeId = factory.Resume.Id });
        Assert.Equal(HttpStatusCode.OK, generate.StatusCode);
        PackageView package = (await generate.Content.ReadFromJsonAsync<PackageView>())!;
        Assert.Equal(PackageStatus.Completed, package.Status);
        Assert.Equal(factory.Resume.SourceVersion!.Id, package.SourceVersionId);
        using var retry = await client.PostAsJsonAsync(path, new { jobId = factory.Job.Id, resumeId = factory.Resume.Id });
        Assert.Equal(package.Id, (await retry.Content.ReadFromJsonAsync<PackageView>())!.Id);
        Assert.Equal(2, factory.Drafts.Calls);
        Assert.Equal(package.Id, (await client.GetFromJsonAsync<PackageView>($"{path}/{package.Id}"))!.Id);
        Assert.Equal(package.Id, (await client.GetFromJsonAsync<PackageView>($"{path}/selection?jobId={factory.Job.Id}&resumeId={factory.Resume.Id}"))!.Id);
        using var download = await client.GetAsync($"{path}/{package.Id}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", download.Headers.CacheControl!.ToString());
        Assert.Equal("Acme-Engineer.zip", download.Content.Headers.ContentDisposition!.FileNameStar);
        using ZipArchive archive = new(new MemoryStream(await download.Content.ReadAsByteArrayAsync()));
        Assert.Equal(4, archive.Entries.Count);
        using StreamReader source = new(archive.GetEntry("resume.md")!.Open());
        Assert.Equal("# Resume from original", source.ReadToEnd());
        using var wrong = await client.GetAsync($"/api/candidates/{Guid.NewGuid()}/packages/{package.Id}/download");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public async Task UnavailableAiIncompleteDownloadValidationAndMissingSelectionHaveSafeResponses()
    {
        using Factory factory = new();
        factory.Drafts.IsAvailable = false;
        using HttpClient client = factory.CreateClient();
        string path = $"/api/candidates/{factory.Candidate.Id}/packages";
        using var missing = await client.GetAsync($"{path}/selection?jobId={factory.Job.Id}&resumeId={factory.Resume.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var invalid = await client.PostAsJsonAsync(path, new { jobId = Guid.Empty, resumeId = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var response = await client.PostAsJsonAsync(path, new { jobId = factory.Job.Id, resumeId = factory.Resume.Id });
        var failed = (await response.Content.ReadFromJsonAsync<PackageView>())!;
        Assert.Equal("AiUnavailable", failed.FailureCode);
        using var incomplete = await client.GetAsync($"{path}/{failed.Id}/download");
        Assert.Equal(HttpStatusCode.Conflict, incomplete.StatusCode);
        factory.Drafts.IsAvailable = true;
        factory.Drafts.FailLetter = true;
        using var failure = await client.PostAsJsonAsync(path, new { jobId = factory.Job.Id, resumeId = factory.Resume.Id });
        string body = await failure.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private source", body);
        Assert.DoesNotContain("secret exception", body);
        factory.Drafts.FailLetter = false;
        using var retry = await client.PostAsJsonAsync(path, new { jobId = factory.Job.Id, resumeId = factory.Resume.Id });
        Assert.Equal(PackageStatus.Completed, (await retry.Content.ReadFromJsonAsync<PackageView>())!.Status);
        Assert.Equal(3, factory.Drafts.Calls);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public CandidateProfile Candidate { get; } = new(Guid.NewGuid(), "Alex", new EmailAddress("alex@example.invalid"), "Engineer");
        public Company Company { get; } = new(Guid.NewGuid(), "Acme");
        public JobPosting Job { get; }
        public Resume Resume { get; }
        public Drafts Drafts { get; } = new();
        public Store Packages { get; } = new();
        public Factory()
        {
            Job = new(Guid.NewGuid(), Company.Id, "Lever", "acme:1", "Engineer", "Job snapshot", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
            Resume = new(Guid.NewGuid(), Candidate.Id, "Source");
            Resume.AddUploadedSource(Guid.NewGuid(), "private source", "resume.docx", new string('a', 64), DateTimeOffset.UtcNow);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.UseSetting("Background:Enabled", "false");
            builder.ConfigureServices(services =>
            {
                Register(services, new Repo<CandidateProfile>(x => x.Id, Candidate));
                Register(services, new Repo<Company>(x => x.Id, Company));
                Register(services, new Repo<JobPosting>(x => x.Id, Job));
                Register(services, new Repo<Resume>(x => x.Id, Resume));
                Register(services, new Repo<CoverLetter>(x => x.Id));
                services.RemoveAll<IPackageStore>(); services.AddSingleton<IPackageStore>(Packages);
                services.RemoveAll<IPackageDraftingService>(); services.AddSingleton<IPackageDraftingService>(Drafts);
            });
        }
        private static void Register<T>(IServiceCollection services, Repo<T> repo) where T : class { services.RemoveAll<IRepository<T>>(); services.AddSingleton<IRepository<T>>(repo); }
    }
    private sealed class Repo<T>(Func<T, Guid> id, params T[] items) : IRepository<T> where T : class
    {
        private readonly List<T> _items = [.. items];
        public Task<T?> GetByIdAsync(Guid value, CancellationToken cancellationToken = default) => Task.FromResult(_items.SingleOrDefault(x => id(x) == value));
        public Task<IReadOnlyList<T>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(_items.Skip(skip).Take(take).ToArray());
        public void Add(T entity) => _items.Add(entity);
        public void Remove(T entity) => _items.Remove(entity);
    }
    private sealed class Drafts : IPackageDraftingService
    {
        public bool IsAvailable { get; set; } = true;
        public bool FailLetter { get; set; }
        public int Calls { get; private set; }
        public Task<string> ResumeAsync(CandidateProfile candidate, JobPosting snapshot, string sourceText, CancellationToken token) { Calls++; Assert.Equal("private source", sourceText); return Task.FromResult("# Resume from original"); }
        public Task<string> CoverLetterAsync(CandidateProfile candidate, JobPosting snapshot, string company, string sourceText, CancellationToken token) { Calls++; if (FailLetter) throw new Exception("secret exception"); return Task.FromResult("Dear hiring team"); }
    }
    private sealed class Store : IPackageStore
    {
        private readonly List<ApplicationPackage> _items = [];
        public Task<ApplicationPackage?> FindAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token) => Task.FromResult(_items.SingleOrDefault(x => x.CandidateProfileId == candidateId && x.JobPostingId == jobId && x.ResumeId == resumeId));
        public Task<ApplicationPackage?> GetAsync(Guid candidateId, Guid packageId, CancellationToken token) => Task.FromResult(_items.SingleOrDefault(x => x.CandidateProfileId == candidateId && x.Id == packageId));
        public Task<bool> HasMatchAsync(Guid candidateId, Guid jobId, CancellationToken token) => Task.FromResult(true);
        public Task CreateAsync(ApplicationPackage package, CancellationToken token) { _items.Add(package); return Task.CompletedTask; }
        public Task<bool> AcquireAsync(ApplicationPackage package, Guid leaseId, DateTimeOffset expiresAt, CancellationToken token) { package.Begin(leaseId, expiresAt); return Task.FromResult(true); }
        public Task SaveAsync(ApplicationPackage package, CancellationToken token) => Task.CompletedTask;
        public Task FailAsync(Guid packageId, Guid candidateId, Guid leaseId, string code, CancellationToken token) { _items.Single(x => x.Id == packageId && x.CandidateProfileId == candidateId).Fail(leaseId, code); return Task.CompletedTask; }
    }
}
