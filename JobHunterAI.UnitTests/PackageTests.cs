using System.IO.Compression;
using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class PackageTests
{
    [Fact]
    public void Package_RequiresBothDocumentsAndPreservesExactArchiveEntries()
    {
        Fixture f = new();
        ApplicationPackage package = f.Package();
        Guid lease = Guid.NewGuid();
        package.Begin(lease, DateTimeOffset.UtcNow.AddMinutes(3));
        Assert.Throws<InvalidOperationException>(() => package.Complete(lease));
        Assert.Throws<InvalidOperationException>(() => package.SaveResume(Guid.NewGuid(), Guid.NewGuid(), "Wrong lease"));
        package.SaveResume(lease, Guid.NewGuid(), "# Resume\nOriginal facts");
        Assert.Throws<InvalidOperationException>(() => package.SaveResume(lease, Guid.NewGuid(), "Duplicate"));
        Assert.Throws<InvalidOperationException>(() => package.Complete(lease));
        package.SaveCoverLetter(lease, Guid.NewGuid(), "Dear hiring team");
        package.Complete(lease);
        Assert.Throws<InvalidOperationException>(() => package.Begin(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(3)));
        PackageArchive zip = PackageZip.Create(PackageView.From(package));
        Assert.Equal("Acme-Engineer.zip", zip.FileName);
        using ZipArchive archive = new(new MemoryStream(zip.Bytes));
        Assert.Equal(new[] { "cover-letter.md", "job-description.txt", "job-link.txt", "resume.md" }, archive.Entries.Select(x => x.FullName).OrderBy(x => x).ToArray());
        foreach (var entry in archive.Entries)
        {
            using StreamReader reader = new(entry.Open());
            string expected = entry.FullName switch { "resume.md" => package.ResumeMarkdown!, "cover-letter.md" => package.CoverLetterMarkdown!, "job-description.txt" => package.JobDescription, _ => package.ApplicationUrl };
            Assert.Equal(expected, reader.ReadToEnd());
        }
        Assert.Equal("Original source", package.SourceText);
        Assert.Throws<InvalidOperationException>(() => PackageZip.Create(PackageView.From(f.Package())));
        Assert.Throws<ArgumentException>(() => ApplicationPackage.ValidateDocument(new string('x', 100001)));
    }

    [Theory]
    [InlineData("../CON", "\r\nTitle:*?\\")]
    [InlineData("", "")]
    [InlineData("CON", "LPT1")]
    [InlineData("PRN.txt", "AUX")]
    public void Filename_IsBoundedAndPathControlReservedNameSafe(string company, string title)
    {
        string file = PackageZip.FileName(company, title);
        Assert.EndsWith(".zip", file);
        Assert.InRange(file.Length, 5, 124);
        Assert.DoesNotContain(file, c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c));
        Assert.NotEqual("CON", file.Split('.')[0]);
        Assert.NotEqual("LPT1", file.Split('.')[0]);
    }
    [Fact]
    public void Filename_BoundsLongNames() => Assert.InRange(PackageZip.FileName(new string('a', 1000), new string('b', 1000)).Length, 5, 124);

    [Fact]
    public async Task Generation_UsesPinnedUploadedSourceAndSnapshotAndReusesCompletedOutputs()
    {
        Fixture f = new();
        f.Resume.AddVersion(Guid.NewGuid(), "Tailored poison", DateTimeOffset.UtcNow, f.Job.Id);
        var result = (await f.Generate()).Value!;
        Assert.Equal(PackageStatus.Completed, result.Status);
        Assert.All(f.Drafting.Sources, x => Assert.Equal("Original source", x));
        Assert.Equal(f.Resume.SourceVersion!.Id, result.SourceVersionId);
        Assert.Equal("Original source", f.Resume.SourceText);
        Assert.Equal(3, f.Resume.Versions.Count);
        Assert.Single(f.Letters.Items);
        Assert.Equal(f.Job.Id, f.Letters.Items[0].JobPostingId);
        Assert.Equal(2, f.Drafting.Calls);
        Assert.Equal(result.Id, (await f.Generate()).Value!.Id);
        Assert.Equal(2, f.Drafting.Calls);
    }

    [Fact]
    public async Task Generation_FailedLetterRetriesOnlyMissingDocumentAgainstOriginalSnapshot()
    {
        Fixture f = new();
        f.Drafting.FailLetter = true;
        var failed = (await f.Generate()).Value!;
        Assert.Equal(PackageStatus.Failed, failed.Status);
        Assert.NotNull(failed.ResumeMarkdown);
        Assert.Null(failed.CoverLetterMarkdown);
        Assert.Equal("GenerationFailed", failed.FailureCode);
        Assert.Equal(2, f.Resume.Versions.Count);
        f.Job.UpdateDetails("Changed title", "Changed description", f.Job.Location, "https://example.invalid/changed");
        f.Drafting.FailLetter = false;
        var completed = (await f.Generate()).Value!;
        Assert.Equal(PackageStatus.Completed, completed.Status);
        Assert.Equal("Engineer", completed.JobTitle);
        Assert.Equal("Build software", completed.JobDescription);
        Assert.Equal(3, f.Drafting.Calls);
        Assert.Equal(2, f.Resume.Versions.Count);
        Assert.Single(f.Letters.Items);
    }

    [Fact]
    public async Task Generation_ValidatesCandidateResumeOwnershipUploadedSourceAndDiscoveredJob()
    {
        Fixture f = new();
        Assert.False((await f.Handler.Handle(new(Guid.NewGuid(), f.Job.Id, f.Resume.Id), default)).IsSuccess);
        Assert.False((await f.Handler.Handle(new(f.Candidate.Id, f.Job.Id, Guid.NewGuid()), default)).IsSuccess);
        Resume other = new(Guid.NewGuid(), Guid.NewGuid(), "Other");
        f.Resumes.Items.Add(other);
        Assert.False((await f.Handler.Handle(new(f.Candidate.Id, f.Job.Id, other.Id), default)).IsSuccess);
        Resume legacy = new(Guid.NewGuid(), f.Candidate.Id, "Legacy");
        legacy.AddVersion(Guid.NewGuid(), "Base", DateTimeOffset.UtcNow);
        f.Resumes.Items.Add(legacy);
        Assert.Equal(JobHunterAI.Application.Common.ErrorKind.Conflict, (await f.Handler.Handle(new(f.Candidate.Id, f.Job.Id, legacy.Id), default)).Error);
        f.Store.HasMatch = false;
        Assert.False((await f.Generate()).IsSuccess);
        Assert.Equal(0, f.Drafting.Calls);
        Assert.Empty(f.Store.Items);
    }

    [Fact]
    public async Task Generation_AiUnavailableAndCancellationHaveSafeRecoverableStatus()
    {
        Fixture f = new();
        f.Drafting.IsAvailable = false;
        var unavailable = (await f.Generate()).Value!;
        Assert.Equal("AiUnavailable", unavailable.FailureCode);
        Assert.Equal(0, f.Drafting.Calls);
        f.Drafting.IsAvailable = true;
        f.Drafting.Cancel = true;
        var cancelled = (await f.Generate()).Value!;
        Assert.Equal("Cancelled", cancelled.FailureCode);
        Assert.Equal(PackageStatus.Failed, cancelled.Status);
        f.Drafting.Cancel = false;
        Assert.Equal(PackageStatus.Completed, (await f.Generate()).Value!.Status);
    }

    [Fact]
    public async Task Generation_ActiveLeaseRejectsOverlappingRetryAndQueriesAreScoped()
    {
        Fixture f = new();
        var package = f.Package();
        package.Begin(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(3));
        f.Store.Items.Add(package);
        Assert.Equal(JobHunterAI.Application.Common.ErrorKind.Conflict, (await f.Generate()).Error);
        Assert.Equal(0, f.Drafting.Calls);
        Assert.False((await new GetPackageHandler(f.Store).Handle(new(Guid.NewGuid(), package.Id), default)).IsSuccess);
        Assert.True((await new GetPackageHandler(f.Store).Handle(new(f.Candidate.Id, package.Id), default)).IsSuccess);
        Assert.True((await new FindPackageHandler(f.Store).Handle(new(f.Candidate.Id, f.Job.Id, f.Resume.Id), default)).IsSuccess);
        Assert.False((await new FindPackageHandler(f.Store).Handle(new(Guid.NewGuid(), f.Job.Id, f.Resume.Id), default)).IsSuccess);
        Assert.False(new GeneratePackageValidator().Validate(new GeneratePackageCommand(Guid.Empty, Guid.Empty, Guid.Empty)).IsValid);
        Assert.False(new GetPackageValidator().Validate(new GetPackageQuery(Guid.Empty, Guid.Empty)).IsValid);
        Assert.False(new FindPackageValidator().Validate(new FindPackageQuery(Guid.Empty, Guid.Empty, Guid.Empty)).IsValid);
    }

    private sealed class Fixture
    {
        public CandidateProfile Candidate { get; } = new(Guid.NewGuid(), "Candidate", new EmailAddress("candidate@example.invalid"), "Engineer");
        public Company Company { get; } = new(Guid.NewGuid(), "Acme");
        public JobPosting Job { get; }
        public Resume Resume { get; }
        public FakeRepository<Resume> Resumes { get; }
        public FakeRepository<CoverLetter> Letters { get; } = new(x => x.Id);
        public Store Store { get; } = new();
        public Drafting Drafting { get; } = new();
        public GeneratePackageHandler Handler => new(new FakeRepository<CandidateProfile>(x => x.Id, Candidate), new FakeRepository<JobPosting>(x => x.Id, Job), new FakeRepository<Company>(x => x.Id, Company), Resumes, Letters, Store, Drafting);
        public Fixture()
        {
            Job = new(Guid.NewGuid(), Company.Id, "Lever", "acme:1", "Engineer", "Build software", new(null, null, WorkArrangement.Remote), "https://example.invalid/job");
            Resume = new(Guid.NewGuid(), Candidate.Id, "Uploaded");
            Resume.AddUploadedSource(Guid.NewGuid(), "Original source", "resume.docx", new string('a', 64), DateTimeOffset.UtcNow);
            Resumes = new(x => x.Id, Resume);
        }
        public ApplicationPackage Package() => new(Guid.NewGuid(), Candidate.Id, Resume.Id, Resume.SourceVersion!.Id, Job, Company.Name, Resume.SourceText!);
        public Task<JobHunterAI.Application.Common.Result<PackageView>> Generate() => Handler.Handle(new(Candidate.Id, Job.Id, Resume.Id), default);
    }
    private sealed class Drafting : IPackageDraftingService
    {
        public bool IsAvailable { get; set; } = true;
        public bool FailLetter { get; set; }
        public bool Cancel { get; set; }
        public int Calls { get; private set; }
        public List<string> Sources { get; } = [];
        public Task<string> ResumeAsync(CandidateProfile candidate, JobPosting snapshot, string sourceText, CancellationToken token) => Draft(sourceText, false);
        public Task<string> CoverLetterAsync(CandidateProfile candidate, JobPosting snapshot, string company, string sourceText, CancellationToken token) => Draft(sourceText, true);
        private Task<string> Draft(string source, bool letter)
        {
            Calls++; Sources.Add(source);
            if (Cancel) throw new OperationCanceledException();
            if (letter && FailLetter) throw new InvalidOperationException("secret prompt contents");
            return Task.FromResult(letter ? "Dear hiring team" : "# Tailored resume");
        }
    }
    private sealed class Store : IPackageStore
    {
        public List<ApplicationPackage> Items { get; } = [];
        public bool HasMatch { get; set; } = true;
        public Task<ApplicationPackage?> FindAsync(Guid candidateId, Guid jobId, Guid resumeId, CancellationToken token) => Task.FromResult(Items.SingleOrDefault(x => x.CandidateProfileId == candidateId && x.JobPostingId == jobId && x.ResumeId == resumeId));
        public Task<ApplicationPackage?> GetAsync(Guid candidateId, Guid packageId, CancellationToken token) => Task.FromResult(Items.SingleOrDefault(x => x.CandidateProfileId == candidateId && x.Id == packageId));
        public Task<bool> HasMatchAsync(Guid candidateId, Guid jobId, CancellationToken token) => Task.FromResult(HasMatch);
        public Task CreateAsync(ApplicationPackage package, CancellationToken token) { Items.Add(package); return Task.CompletedTask; }
        public Task<bool> AcquireAsync(ApplicationPackage package, Guid leaseId, DateTimeOffset expiresAt, CancellationToken token)
        {
            if (package.Status == PackageStatus.Generating && package.LeaseExpiresAt > DateTimeOffset.UtcNow) return Task.FromResult(false);
            package.Begin(leaseId, expiresAt); return Task.FromResult(true);
        }
        public Task SaveAsync(ApplicationPackage package, CancellationToken token) => Task.CompletedTask;
        public Task FailAsync(Guid packageId, Guid candidateId, Guid leaseId, string code, CancellationToken token)
        {
            Items.Single(x => x.Id == packageId && x.CandidateProfileId == candidateId).Fail(leaseId, code);
            return Task.CompletedTask;
        }
    }
}
