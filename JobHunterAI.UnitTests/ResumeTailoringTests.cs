using System.IO.Compression;
using JobHunterAI.AI;
using JobHunterAI.Application.AI;
using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class ResumeTailoringTests
{
    private const string BaseResume = "# Alex\n## Skills\n- C#\n- Azure\n## Certifications\n- AZ-900";

    private static (CandidateProfile Candidate, JobPosting Job) Build()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        candidate.AddSkill(Guid.NewGuid(), "C#");
        candidate.AddCertification(Guid.NewGuid(), "AZ-900", "Microsoft", new DateOnly(2022, 1, 1), null);
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "acme:1", "Architect", "Lead",
            new JobLocation(null, null, true), "https://example.com/apply");
        return (candidate, job);
    }

    private static ResumeTailoringService Service(params string[] replies) =>
        new(new StructuredAiClient(new Fake(replies),
            Options.Create(new AiOptions { MaxAttempts = 2, RetryBaseDelay = TimeSpan.Zero }),
            NullLogger<StructuredAiClient>.Instance));

    [Fact]
    public async Task Tailor_DropsKeywordsNotEvidencedByBaseResume()
    {
        (CandidateProfile candidate, JobPosting job) = Build();

        TailoredResumeResult result = await Service(
            "{\"contentMarkdown\":\"# Alex\\n## Certifications\\n- AZ-900\",\"keywordsUsed\":[\"C#\",\"Kubernetes\"]}")
            .TailorAsync(candidate, job, BaseResume);

        Assert.Equal(["C#"], result.KeywordsUsed);
    }

    [Fact]
    public async Task Tailor_RejectsInventedCertification()
    {
        (CandidateProfile candidate, JobPosting job) = Build();
        string bad = "{\"contentMarkdown\":\"# Alex\\n## Certifications\\n- AZ-900\\n- CISSP\",\"keywordsUsed\":[]}";

        await Assert.ThrowsAsync<AiResponseException>(() => Service(bad, bad).TailorAsync(candidate, job, BaseResume));
    }

    [Fact]
    public async Task DraftingService_ReturnsTailoredMarkdown()
    {
        (CandidateProfile candidate, JobPosting job) = Build();
        Resume resume = new(Guid.NewGuid(), candidate.Id, "Base");
        ResumeVersion version = resume.AddVersion(Guid.NewGuid(), BaseResume, DateTimeOffset.UtcNow);
        AiResumeDraftingService drafting = new(Service("{\"contentMarkdown\":\"# Tailored\",\"keywordsUsed\":[]}"));

        ResumeDraft draft = await drafting.DraftAsync(candidate, job, version, CancellationToken.None);

        Assert.Equal("# Tailored", draft.ContentMarkdown);
    }

    [Fact]
    public void Renderer_ProducesEncodedHtml()
    {
        string html = new ResumeDocumentRenderer().ToHtml("# Alex\n- <script>x</script>\nText", "Resume");

        Assert.Contains("<h1>Alex</h1>", html);
        Assert.Contains("<li>&lt;script&gt;x&lt;/script&gt;</li>", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void Renderer_ProducesReadableDocx()
    {
        byte[] bytes = new ResumeDocumentRenderer().ToDocx("# Alex\n- Led teams & budgets");

        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);
        using StreamReader reader = new(zip.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("Alex", xml);
        Assert.Contains("Led teams &amp; budgets", xml);
    }

    private sealed class Fake(string[] replies) : IAiTextCompletion
    {
        private int _calls;

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            Task.FromResult(replies[Math.Min(_calls++, replies.Length - 1)]);
    }
}
