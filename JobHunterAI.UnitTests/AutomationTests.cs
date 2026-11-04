using JobHunterAI.Automation;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class AutomationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "jh-automation-" + Guid.NewGuid());
    private readonly AutomationAuditLog _audit = new(NullLogger<AutomationAuditLog>.Instance);

    public AutomationTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private static RawFormField Raw(string id, string tag, string type, string label, string name = "") =>
        new() { Selector = $"[data-jh-id=\"{id}\"]", Tag = tag, Type = type, Label = label, Name = name };

    private static List<RawFormField> SampleForm() =>
    [
        Raw("f0", "input", "text", "Full name"),
        Raw("f1", "input", "email", "Email"),
        Raw("f2", "input", "file", "Resume/CV"),
        Raw("f3", "input", "file", "Cover letter"),
        Raw("f4", "textarea", "", "Why do you want to work here?"),
        new RawFormField { Selector = "[data-jh-id=\"f5\"]", Tag = "select", Label = "Work authorization", Options = ["Yes", "No"] },
        Raw("f6", "input", "hidden", "", "token"),
        Raw("f7", "input", "checkbox", "I agree")
    ];

    private ResumeUploadService Uploads() =>
        new(_audit, Options.Create(new AutomationOptions { MaxUploadBytes = 1024 }));

    private string NewFile(string name, int size = 10)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public void FormDetection_ClassifiesControlsAndSkipsHidden()
    {
        IReadOnlyList<DetectedField> fields = new FormDetectionService().Detect(SampleForm());

        Assert.Equal(7, fields.Count);
        Assert.Equal(FieldKind.File, fields[2].Kind);
        Assert.Equal(FieldKind.TextArea, fields[4].Kind);
        Assert.Equal(FieldKind.Select, fields[5].Kind);
        Assert.Equal(FieldKind.Checkbox, fields[6].Kind);
    }

    [Fact]
    public void QuestionDetection_ExcludesIdentityFilesAndCheckboxes()
    {
        IReadOnlyList<DetectedField> fields = new FormDetectionService().Detect(SampleForm());

        IReadOnlyList<DetectedField> questions = new QuestionDetectionService().FindQuestionFields(fields);

        Assert.Equal(["Why do you want to work here?", "Work authorization"], questions.Select(q => q.Label));
        Assert.Contains("choose exactly one of: Yes | No", QuestionDetectionService.ToQuestion(questions[1]).Question);
    }

    [Fact]
    public async Task Upload_TargetsResumeAndCoverFieldsAndValidatesFiles()
    {
        IReadOnlyList<DetectedField> fields = new FormDetectionService().Detect(SampleForm());
        FakePage page = new(SampleForm());

        Assert.True(await Uploads().UploadAsync(page, fields, NewFile("r.pdf"), "resume"));
        Assert.True(await Uploads().UploadAsync(page, fields, NewFile("c.docx"), "cover"));

        Assert.Equal(["[data-jh-id=\"f2\"]", "[data-jh-id=\"f3\"]"], page.Uploads.Select(u => u.Selector));
        await Assert.ThrowsAsync<ArgumentException>(() => Uploads().UploadAsync(page, fields, NewFile("r.exe"), "resume"));
        await Assert.ThrowsAsync<ArgumentException>(() => Uploads().UploadAsync(page, fields, NewFile("big.pdf", 2048), "resume"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => Uploads().UploadAsync(page, fields, Path.Combine(_directory, "x.pdf"), "resume"));
    }

    [Fact]
    public async Task Workflow_FillsIdentityUploadsAndNeverSubmits()
    {
        FakePage page = new(SampleForm());
        ApplicationWorkflow workflow = new(new FakeBrowser(page), new FormDetectionService(), new QuestionDetectionService(),
            Uploads(), new FileScreenshotStore(Options.Create(new AutomationOptions { ScreenshotDirectory = _directory })), _audit);
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "a:1", "Architect", "Lead",
            new JobLocation(null, null, true), "https://example.com/apply");

        await using ApplicationSession session = await workflow.PrepareAsync(
            job, candidate, new ApplicationDocuments(NewFile("r.pdf"), NewFile("c.pdf")));

        Assert.Equal("Alex Smith", page.Filled["[data-jh-id=\"f0\"]"]);
        Assert.Equal("alex@example.com", page.Filled["[data-jh-id=\"f1\"]"]);
        Assert.Equal(2, page.Uploads.Count);
        Assert.Equal(2, session.QuestionFields.Count);
        Assert.Equal(0, page.SubmitClicks);
        Assert.Equal(2, session.Screenshots.Count);
        Assert.All(session.Screenshots, s => Assert.True(File.Exists(s)));
        Assert.Contains(_audit.Entries, e => e.Step == "Review" && e.Detail.Contains("Nothing was submitted"));
    }

    [Fact]
    public async Task Workflow_RejectsUnsafeUrlsAndDisposesPageOnFailure()
    {
        FakePage page = new(SampleForm()) { FailOnRead = true };
        ApplicationWorkflow workflow = new(new FakeBrowser(page), new FormDetectionService(), new QuestionDetectionService(),
            Uploads(), new FileScreenshotStore(Options.Create(new AutomationOptions { ScreenshotDirectory = _directory })), _audit);
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "a:1", "Architect", "Lead",
            new JobLocation(null, null, true), "https://example.com/apply");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.PrepareAsync(job, candidate, new ApplicationDocuments(null, null)));

        Assert.True(page.Disposed);
        Assert.Throws<ArgumentException>(() => AutomationGuard.RequireHttpUrl("file:///c:/secret"));
        Assert.Throws<ArgumentException>(() => AutomationGuard.RequireHttpUrl("https://user:pw@example.com"));
    }

    private sealed class FakeBrowser(FakePage page) : IBrowserService
    {
        public Task<IApplicationPage> OpenPageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IApplicationPage>(page);
    }

    private sealed class FakePage(List<RawFormField> form) : IApplicationPage
    {
        public string CurrentUrl { get; private set; } = string.Empty;
        public Dictionary<string, string> Filled { get; } = [];
        public List<(string Selector, string Path)> Uploads { get; } = [];
        public int SubmitClicks { get; private set; }
        public bool FailOnRead { get; init; }
        public bool Disposed { get; private set; }

        public Task NavigateAsync(Uri url, CancellationToken cancellationToken = default)
        {
            CurrentUrl = url.AbsoluteUri;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RawFormField>> ReadFormFieldsAsync(CancellationToken cancellationToken = default) =>
            FailOnRead ? throw new InvalidOperationException("boom") : Task.FromResult<IReadOnlyList<RawFormField>>(form);

        public Task FillTextAsync(string selector, string value, CancellationToken cancellationToken = default)
        {
            Filled[selector] = value;
            return Task.CompletedTask;
        }

        public Task SelectOptionAsync(string selector, string option, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetCheckedAsync(string selector, bool isChecked, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default)
        {
            Uploads.Add((selector, filePath));
            return Task.CompletedTask;
        }

        public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new byte[] { 1, 2, 3 });

        public Task ClickSubmitAsync(CancellationToken cancellationToken = default)
        {
            SubmitClicks++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
