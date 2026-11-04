using System.IO.Compression;
using JobHunterAI.AI;
using JobHunterAI.Application.AI;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class CoverLetterTests
{
    private static readonly CandidateProfile Candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
    private static readonly Company Acme = new(Guid.NewGuid(), "Acme");
    private static readonly JobPosting Job = new(Guid.NewGuid(), Acme.Id, "Greenhouse", "acme:1", "Architect", "Lead",
        new JobLocation(null, null, true), "https://example.com/apply");

    private static CoverLetterService Service(Fake fake) =>
        new(new StructuredAiClient(fake, Options.Create(new AiOptions { MaxAttempts = 2, RetryBaseDelay = TimeSpan.Zero }),
            NullLogger<StructuredAiClient>.Instance));

    [Theory]
    [InlineData(CoverLetterTone.Executive, "executive")]
    [InlineData(CoverLetterTone.Warm, "warm")]
    [InlineData(CoverLetterTone.Direct, "direct")]
    [InlineData(CoverLetterTone.Professional, "professional")]
    public async Task Write_AppliesRequestedToneToPrompt(CoverLetterTone tone, string expected)
    {
        Fake fake = new("{\"contentMarkdown\":\"Dear Acme\"}");

        await Service(fake).WriteAsync(Candidate, Job, Acme, tone);

        Assert.Contains($"Tone: {expected}", fake.SystemPrompts[0]);
    }

    [Fact]
    public async Task Write_RejectsUnresolvedPlaceholders()
    {
        Fake fake = new("{\"contentMarkdown\":\"Dear Acme, [Your Name]\"}");

        await Assert.ThrowsAsync<AiResponseException>(() => Service(fake).WriteAsync(Candidate, Job, Acme, CoverLetterTone.Executive));
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task DraftingService_UsesCompanyAndConfiguredTone()
    {
        Fake fake = new("{\"contentMarkdown\":\"Dear Acme\"}");
        AiCoverLetterDraftingService drafting = new(Service(fake), new CompanyRepo(Acme),
            Options.Create(new AiOptions { CoverLetterTone = CoverLetterTone.Warm }));

        CoverLetterDraft draft = await drafting.DraftAsync(Candidate, Job, CancellationToken.None);

        Assert.Equal("Dear Acme", draft.ContentMarkdown);
        Assert.Contains("Tone: warm", fake.SystemPrompts[0]);
        Assert.Contains("Acme", fake.UserPrompts[0]);
    }

    [Fact]
    public async Task DraftingService_FailsWhenCompanyMissing()
    {
        AiCoverLetterDraftingService drafting = new(Service(new Fake("{}")), new CompanyRepo(null), Options.Create(new AiOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => drafting.DraftAsync(Candidate, Job, CancellationToken.None));
    }

    [Fact]
    public void Renderer_ProducesDocxForLetter()
    {
        byte[] bytes = new ResumeDocumentRenderer().ToDocx("Dear Acme,\n\nI lead teams & deliver.");

        using ZipArchive zip = new(new MemoryStream(bytes), ZipArchiveMode.Read);
        using StreamReader reader = new(zip.GetEntry("word/document.xml")!.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("Dear Acme,", xml);
        Assert.Contains("I lead teams &amp; deliver.", xml);
    }

    private sealed class Fake(string reply) : IAiTextCompletion
    {
        public int Calls { get; private set; }
        public List<string> SystemPrompts { get; } = [];
        public List<string> UserPrompts { get; } = [];

        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            Calls++;
            SystemPrompts.Add(systemPrompt);
            UserPrompts.Add(userPrompt);
            return Task.FromResult(reply);
        }
    }

    private sealed class CompanyRepo(Company? company) : IRepository<Company>
    {
        public Task<Company?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(company);

        public Task<IReadOnlyList<Company>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Company>>([]);

        public void Add(Company entity) { }

        public void Remove(Company entity) { }
    }
}
