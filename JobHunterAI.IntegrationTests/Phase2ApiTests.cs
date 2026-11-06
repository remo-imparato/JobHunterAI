using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobHunterAI.IntegrationTests;

public sealed class Phase2ApiTests
{
    [Fact]
    public async Task UploadPreviewImportAndRepeat_PreserveSourceAndCandidateOwnership()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        byte[] file = Docx("Alex\nUnrecognized\nOriginal facts remain here.\nSkills\nC#, Azure");
        using var upload = await Upload(client, factory.First.Id, file, "../resume.docx");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        ResumePreview preview = (await upload.Content.ReadFromJsonAsync<ResumePreview>())!;
        Assert.Contains("Original facts remain here.", preview.SourceText);
        Assert.Equal("resume.docx", preview.Resume.SourceFileName);
        Assert.NotNull(preview.Resume.SourceVersionId);
        Assert.Empty(factory.First.Skills);
        Assert.Equal(2, preview.ProposedAdditions.Skills.Count);
        Resume saved = Assert.Single(factory.Resumes.Items);
        Assert.Equal(64, saved.SourceSha256!.Length);
        Assert.Equal(preview.SourceText, saved.SourceText);
        using var repeat = await Upload(client, factory.First.Id, file, "resume.docx");
        Assert.Equal(preview.Resume.Id, (await repeat.Content.ReadFromJsonAsync<ResumePreview>())!.Resume.Id);
        Assert.Single(factory.Resumes.Items);
        using var wrong = await client.GetAsync($"/api/candidates/{factory.Second.Id}/resumes/{saved.Id}");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var wrongImport = await client.PostAsync($"/api/candidates/{factory.Second.Id}/resumes/{saved.Id}/import", null);
        Assert.Equal(HttpStatusCode.NotFound, wrongImport.StatusCode);
        for (int i = 0; i < 2; i++)
        {
            using var imported = await client.PostAsync($"/api/candidates/{factory.First.Id}/resumes/{saved.Id}/import", null);
            Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
            Assert.Empty((await imported.Content.ReadFromJsonAsync<ResumePreview>())!.ProposedAdditions.Skills);
        }
        Assert.Equal(2, factory.First.Skills.Count);
        Assert.Empty(factory.Second.Skills);
        Assert.Equal(preview.SourceText, saved.SourceText);
        using var secondUpload = await Upload(client, factory.Second.Id, file, "resume.docx");
        Assert.Equal(HttpStatusCode.OK, secondUpload.StatusCode);
        Assert.Equal(2, factory.Resumes.Items.Count);
        var list = await client.GetFromJsonAsync<ResumeSummary[]>($"/api/candidates/{factory.First.Id}/resumes");
        Assert.Equal(saved.Id, Assert.Single(list!).Id);
        Assert.Empty((await client.GetFromJsonAsync<ResumeSummary[]>($"/api/candidates/{factory.First.Id}/resumes?page=1"))!);
    }

    [Theory]
    [InlineData("resume.txt", "Text")]
    [InlineData("resume.pdf", "Invalid PDF")]
    [InlineData("resume.docx", "Invalid DOCX")]
    public async Task Upload_RejectsUnsupportedAndInvalidSignatures(string name, string content)
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var response = await Upload(client, factory.First.Id, System.Text.Encoding.UTF8.GetBytes(content), name);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Resumes.Items);
    }

    [Fact]
    public async Task Upload_RejectsEmptyScannedAndMalformedRequests()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var empty = await Upload(client, factory.First.Id, [], "resume.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var scanned = await Upload(client, factory.First.Id, Docx(" "), "resume.docx");
        Assert.Equal(HttpStatusCode.BadRequest, scanned.StatusCode);
        Assert.Contains("OCR", await scanned.Content.ReadAsStringAsync());
        using var plain = await client.PostAsJsonAsync($"/api/candidates/{factory.First.Id}/resumes", new { file = "invalid" });
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plain.StatusCode);
        using MultipartFormDataContent missing = new();
        missing.Add(new StringContent("not a file"), "file");
        using var noFile = await client.PostAsync($"/api/candidates/{factory.First.Id}/resumes", missing);
        Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
        using var tooLarge = await Upload(client, factory.First.Id, new byte[ResumeUploadLimits.MaxFileBytes + 65537], "resume.pdf");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Empty(factory.Resumes.Items);
    }

    [Fact]
    public async Task MissingCandidatesSourcesAndInvalidPagination_ReturnExpectedStatus()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var missingCandidate = await Upload(client, Guid.NewGuid(), Docx("Text"), "resume.docx");
        Assert.Equal(HttpStatusCode.NotFound, missingCandidate.StatusCode);
        using var missingList = await client.GetAsync($"/api/candidates/{Guid.NewGuid()}/resumes");
        Assert.Equal(HttpStatusCode.NotFound, missingList.StatusCode);
        using var invalidPage = await client.GetAsync($"/api/candidates/{factory.First.Id}/resumes?page=-1");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Resume legacy = new(Guid.NewGuid(), factory.First.Id, "Tailored only");
        legacy.AddVersion(Guid.NewGuid(), "Tailored output", DateTimeOffset.UtcNow, Guid.NewGuid());
        factory.Resumes.Items.Add(legacy);
        using var noSource = await client.GetAsync($"/api/candidates/{factory.First.Id}/resumes/{legacy.Id}");
        Assert.Equal(HttpStatusCode.Conflict, noSource.StatusCode);
        using var import = await client.PostAsync($"/api/candidates/{factory.First.Id}/resumes/{legacy.Id}/import", null);
        Assert.Equal(HttpStatusCode.Conflict, import.StatusCode);
    }

    [Fact]
    public async Task Criteria_CreateUpdateValidateAndRead_AreCandidateScoped()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        string path = $"/api/candidates/{factory.First.Id}/search-criteria";
        using var initial = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, initial.StatusCode);
        SearchCriteriaInput input = new([" Engineer ", "engineer"], [" Boston "], WorkArrangement.Remote, false, ["C#"], ["contract"]);
        using var save = await client.PutAsJsonAsync(path, input);
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var saved = (await client.GetFromJsonAsync<SearchCriteriaInput>(path))!;
        Assert.Equal("Engineer", Assert.Single(saved.JobTitles));
        Assert.Equal("Boston", Assert.Single(saved.Locations));
        Assert.False(saved.IncludeUnknownArrangement);
        using var update = await client.PutAsJsonAsync(path, input with { Arrangements = WorkArrangement.Hybrid, IncludeUnknownArrangement = true });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(WorkArrangement.Hybrid, (await client.GetFromJsonAsync<SearchCriteriaInput>(path))!.Arrangements);
        Assert.Single(factory.Criteria.Items);
        using var otherCandidate = await client.GetAsync($"/api/candidates/{factory.Second.Id}/search-criteria");
        Assert.Equal(HttpStatusCode.NotFound, otherCandidate.StatusCode);
        foreach (SearchCriteriaInput invalid in new[]
        {
            input with { JobTitles = [] }, input with { Arrangements = (WorkArrangement)8 },
            input with { RequiredKeywords = ["contract"] }, input with { Locations = null! }
        })
        {
            using var response = await client.PutAsJsonAsync(path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var missing = await client.PutAsJsonAsync($"/api/candidates/{Guid.NewGuid()}/search-criteria", input);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid candidate, byte[] bytes, string name)
    {
        using MultipartFormDataContent form = new();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return await client.PostAsync($"/api/candidates/{candidate}/resumes", form);
    }

    private static byte[] Docx(string text)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, true))
        {
            using StreamWriter writer = new(archive.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");
            foreach (string line in text.Split('\n')) writer.Write($"<w:p><w:r><w:t>{System.Security.SecurityElement.Escape(line)}</w:t></w:r></w:p>");
            writer.Write("</w:body></w:document>");
        }
        return stream.ToArray();
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public CandidateProfile First { get; } = new(Guid.NewGuid(), "First", new EmailAddress("first@example.invalid"), "Engineer");
        public CandidateProfile Second { get; } = new(Guid.NewGuid(), "Second", new EmailAddress("second@example.invalid"), "Engineer");
        public Store<Resume> Resumes { get; } = new(x => x.Id);
        public Store<CandidateSearchCriteria> Criteria { get; } = new(x => x.Id);
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Background:Enabled", "false");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRepository<CandidateProfile>>();
                services.RemoveAll<IRepository<Resume>>();
                services.RemoveAll<IRepository<CandidateSearchCriteria>>();
                services.RemoveAll<IResumeLookup>();
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IRepository<CandidateProfile>>(new Store<CandidateProfile>(x => x.Id) { Items = [First, Second] });
                services.AddSingleton<IRepository<Resume>>(Resumes);
                services.AddSingleton<IRepository<CandidateSearchCriteria>>(Criteria);
                services.AddSingleton<IResumeLookup>(new Lookup(Resumes));
                services.AddSingleton<IUnitOfWork, Work>();
            });
        }
    }
    private sealed class Store<T>(Func<T, Guid> id) : IRepository<T> where T : class
    {
        public List<T> Items { get; init; } = [];
        public Task<T?> GetByIdAsync(Guid value, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => id(x) == value));
        public Task<IReadOnlyList<T>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Items.Skip(skip).Take(take).ToArray());
        public void Add(T item) => Items.Add(item);
        public void Remove(T item) => Items.Remove(item);
    }
    private sealed class Work : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
    private sealed class Lookup(Store<Resume> resumes) : IResumeLookup
    {
        public Task<Resume?> FindByHashAsync(Guid candidateId, string hash, CancellationToken token) => Task.FromResult(resumes.Items.SingleOrDefault(x => x.CandidateProfileId == candidateId && x.SourceSha256 == hash));
        public Task<IReadOnlyList<ResumeSummary>> ListAsync(Guid candidateId, int page, int pageSize, CancellationToken token) => Task.FromResult<IReadOnlyList<ResumeSummary>>(
            resumes.Items.Where(x => x.CandidateProfileId == candidateId).OrderBy(x => x.Name).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize).Select(ResumePreviews.Summary).ToArray());
    }
}
