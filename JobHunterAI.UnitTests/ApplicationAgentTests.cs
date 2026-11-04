using JobHunterAI.Application.AI;
using JobHunterAI.Automation;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class ApplicationAgentTests
{
    [Fact]
    public async Task Prepare_SuggestsBeforeUploadsAndPausesForReview()
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationReview review = await session.GetReviewAsync();

        Assert.Equal(ApplicationReviewStatus.AwaitingReview, session.Status);
        Assert.Equal(0, fixture.Page.Clicks);
        Assert.Equal("Experience", review.Answers[0].Answer);
        Assert.Equal("Yes", fixture.Page.Selected);
        Assert.Equal(["suggest", "fill", "select", "upload", "upload"], fixture.Page.Actions);
        Assert.Equal(2, review.Screenshots.Count);
        Assert.DoesNotContain(fixture.Audit.Entries, e => e.Step == "Approve");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Submit_RequiresExplicitApprovalForThisSession(bool approved, bool wrongSession)
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationApproval approval = new(wrongSession ? Guid.NewGuid() : session.Id, 1, Guid.NewGuid(), approved);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(approval));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, 1, Guid.Empty, true)));
        Assert.Equal(0, fixture.Page.Clicks);
    }

    [Fact]
    public async Task Edit_InvalidatesOldReviewAndSubmissionIsSingleUse()
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationReview old = await session.GetReviewAsync();
        await session.SetAnswerAsync("q", "Revised", old.Revision);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, old.Revision, Guid.NewGuid(), true)));
        ApplicationReview current = await session.GetReviewAsync();
        Assert.Equal(2, current.Revision);
        Assert.Equal("Revised", current.Answers[0].Answer);
        Guid user = Guid.NewGuid();
        ApplicationSubmissionAttempt result = await session.ApproveAndSubmitAsync(new(session.Id, current.Revision, user, true));

        Assert.Equal(user, result.ApprovedByUserId);
        Assert.Equal(ApplicationReviewStatus.SubmissionAttempted, session.Status);
        Assert.Equal(1, fixture.Page.Clicks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, current.Revision, user, true)));
        Assert.Equal(1, fixture.Page.Clicks);
        Assert.Contains(fixture.Audit.Entries, e => e.Step == "Approve");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Unsupported", true)]
    public async Task InvalidOrUnsupportedSuggestions_RequireUserAnswer(string text, bool needsInput)
    {
        Fixture fixture = new() { TextAnswer = text, NeedsInput = needsInput };
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationReview review = await session.GetReviewAsync();
        Assert.True(review.Answers[0].NeedsUserInput);
        Assert.DoesNotContain("fill", fixture.Page.Actions);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, 1, Guid.NewGuid(), true)));
        await session.SetAnswerAsync("q", "User answer", 1);
        await session.ApproveAndSubmitAsync(new(session.Id, 2, Guid.NewGuid(), true));
        Assert.Equal(1, fixture.Page.Clicks);
    }

    [Fact]
    public async Task Edit_ValidatesDetectedFieldLengthAndSelectOptions()
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => session.SetAnswerAsync("unknown", "text", 1));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SetAnswerAsync("q", new string('x', 21), 1));
        await Assert.ThrowsAsync<ArgumentException>(() => session.SetAnswerAsync("s", "Maybe", 1));
        Assert.Equal(1, (await session.GetReviewAsync()).Revision);
    }

    [Fact]
    public async Task Submit_PageChangedRequiresNewPreparation()
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        fixture.Page.Fields[0].Label = "Changed question";
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, 1, Guid.NewGuid(), true)));
        Assert.Equal(ApplicationReviewStatus.Faulted, session.Status);
        Assert.Equal(0, fixture.Page.Clicks);
    }

    [Fact]
    public async Task Submit_FailureIsUncertainAndCannotBeRetried()
    {
        Fixture fixture = new();
        fixture.Page.FailSubmit = true;
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationApproval approval = new(session.Id, 1, Guid.NewGuid(), true);
        await Assert.ThrowsAsync<IOException>(() => session.ApproveAndSubmitAsync(approval));
        Assert.Equal(ApplicationReviewStatus.SubmissionUncertain, session.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(approval));
        Assert.Equal(1, fixture.Page.Clicks);
    }

    [Fact]
    public async Task ConcurrentApproval_ClicksOnlyOnce()
    {
        Fixture fixture = new();
        await using ApplicationReviewSession session = await fixture.PrepareAsync();
        ApplicationApproval approval = new(session.Id, 1, Guid.NewGuid(), true);
        Task<ApplicationSubmissionAttempt> first = session.ApproveAndSubmitAsync(approval);
        Task<ApplicationSubmissionAttempt> second = session.ApproveAndSubmitAsync(approval);
        await first;
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(1, fixture.Page.Clicks);
    }

    [Fact]
    public async Task CancellationAndDisposal_NeverSubmit()
    {
        Fixture fixture = new();
        ApplicationReviewSession session = await fixture.PrepareAsync();
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ApproveAndSubmitAsync(new(session.Id, 1, Guid.NewGuid(), true), source.Token));
        await session.DisposeAsync();
        await session.DisposeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ApproveAndSubmitAsync(new(session.Id, 1, Guid.NewGuid(), true)));
        Assert.True(fixture.Page.Disposed);
        Assert.Equal(0, fixture.Page.Clicks);
    }

    [Fact]
    public async Task SuggestionFailure_DisposesOwnedPage()
    {
        Fixture fixture = new() { FailSuggestions = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.PrepareAsync());
        Assert.True(fixture.Page.Disposed);
        Assert.Equal(0, fixture.Page.Clicks);
    }

    private sealed class Fixture : IBrowserService, IApplicationQuestionService, IScreenshotStore
    {
        public FakePage Page { get; } = new();
        public AutomationAuditLog Audit { get; } = new(NullLogger<AutomationAuditLog>.Instance);
        public string TextAnswer { get; init; } = "Experience";
        public bool NeedsInput { get; init; }
        public bool FailSuggestions { get; init; }

        public async Task<ApplicationReviewSession> PrepareAsync()
        {
            string resume = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
            string cover = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".docx");
            try
            {
                await File.WriteAllBytesAsync(resume, [1]);
                await File.WriteAllBytesAsync(cover, [1]);
                FormDetectionService detection = new();
                ApplicationWorkflow workflow = new(this, detection, new QuestionDetectionService(),
                    new ResumeUploadService(Audit, Options.Create(new AutomationOptions())), this, Audit);
                ApplicationAgent agent = new(workflow, this, detection, this, Audit);
                CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
                JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "a:1", "Architect", "Lead",
                    new JobLocation(null, null, true), "https://example.com/apply");
                return await agent.PrepareAsync(job, candidate, new ApplicationDocuments(resume, cover));
            }
            finally
            {
                File.Delete(resume);
                File.Delete(cover);
            }
        }

        public Task<IApplicationPage> OpenPageAsync(CancellationToken cancellationToken = default) => Task.FromResult<IApplicationPage>(Page);
        public Task<string> SaveAsync(string step, byte[] png, CancellationToken cancellationToken = default) => Task.FromResult(step + ".png");
        public Task<IReadOnlyList<SuggestedAnswer>> SuggestAnswersAsync(CandidateProfile candidate, JobPosting job,
            IReadOnlyList<ApplicationQuestion> questions, CancellationToken cancellationToken = default)
        {
            if (FailSuggestions) throw new InvalidOperationException("AI unavailable");
            Page.Actions.Add("suggest");
            return Task.FromResult<IReadOnlyList<SuggestedAnswer>>([
                new(questions[0].Question, TextAnswer, NeedsInput), new(questions[1].Question, "Yes", false)]);
        }
    }

    private sealed class FakePage : IApplicationPage
    {
        public List<RawFormField> Fields { get; } =
        [
            new() { Selector = "q", Tag = "textarea", Label = "Why this role?", MaxLength = 20 },
            new() { Selector = "s", Tag = "select", Label = "Available?", Options = ["Yes", "No"] },
            new() { Selector = "r", Tag = "input", Type = "file", Label = "Resume" },
            new() { Selector = "c", Tag = "input", Type = "file", Label = "Cover letter" }
        ];
        public List<string> Actions { get; } = [];
        public string CurrentUrl { get; private set; } = string.Empty;
        public string? Selected { get; private set; }
        public int Clicks { get; private set; }
        public bool Disposed { get; private set; }
        public bool FailSubmit { get; set; }
        public Task NavigateAsync(Uri url, CancellationToken cancellationToken = default) { CurrentUrl = url.AbsoluteUri; return Task.CompletedTask; }
        public Task<IReadOnlyList<RawFormField>> ReadFormFieldsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RawFormField>>(Fields);
        public Task FillTextAsync(string selector, string value, CancellationToken cancellationToken = default) { Actions.Add("fill"); return Task.CompletedTask; }
        public Task SelectOptionAsync(string selector, string option, CancellationToken cancellationToken = default) { Selected = option; Actions.Add("select"); return Task.CompletedTask; }
        public Task SetCheckedAsync(string selector, bool isChecked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UploadFileAsync(string selector, string filePath, CancellationToken cancellationToken = default) { Actions.Add("upload"); return Task.CompletedTask; }
        public Task<byte[]> ScreenshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new byte[] { 1 });
        public Task ClickSubmitAsync(CancellationToken cancellationToken = default) { Clicks++; return FailSubmit ? Task.FromException(new IOException("Unknown outcome")) : Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
