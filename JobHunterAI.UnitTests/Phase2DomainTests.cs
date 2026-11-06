using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure.Resumes;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunterAI.UnitTests;

public sealed class Phase2DomainTests
{
    [Fact]
    public void UploadedSource_IsPinnedAndPreservesCompleteText()
    {
        Resume resume = new(Guid.NewGuid(), Guid.NewGuid(), "Original");
        string text = "  Alex\nSkills\nC#\nUnrecognized section\nEvery fact is retained.\n";
        resume.AddUploadedSource(Guid.NewGuid(), text, "resume.pdf", new string('a', 64), DateTimeOffset.UtcNow);
        Guid sourceId = resume.SourceVersion!.Id;
        resume.AddVersion(Guid.NewGuid(), "Tailored output", DateTimeOffset.UtcNow, Guid.NewGuid());
        resume.AddVersion(Guid.NewGuid(), "Another base", DateTimeOffset.UtcNow);
        Assert.Equal(sourceId, resume.SourceVersion.Id);
        Assert.Equal(text, resume.SourceText);
        Assert.Single(resume.Versions, x => x.IsSource);
        Assert.Throws<InvalidOperationException>(() => resume.AddUploadedSource(Guid.NewGuid(), "Replacement", "new.pdf", new string('b', 64), DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("../resume.pdf", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("resume.pdf", "invalid")]
    public void UploadedSource_RejectsInvalidMetadataWithoutAddingVersions(string name, string hash)
    {
        Resume resume = new(Guid.NewGuid(), Guid.NewGuid(), "Base");
        Assert.Throws<ArgumentException>(() => resume.AddUploadedSource(Guid.NewGuid(), "Text", name, hash, DateTimeOffset.UtcNow));
        Assert.Empty(resume.Versions);
    }

    [Fact]
    public async Task Generation_AlwaysUsesPinnedOriginalNotLatestVariant()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex", new EmailAddress("alex@example.invalid"), "Engineer");
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Lever", "job", "Architect", "Lead",
            new JobLocation(null, null, true), "https://example.invalid/job");
        Resume resume = new(Guid.NewGuid(), candidate.Id, "Base");
        ResumeVersion source = resume.AddVersion(Guid.NewGuid(), "Verified original", DateTimeOffset.UtcNow);
        resume.AddVersion(Guid.NewGuid(), "Previously tailored", DateTimeOffset.UtcNow, job.Id);
        FakeResumeDraftingService drafts = new();
        GenerateResumeHandler handler = new(new FakeRepository<Resume>(x => x.Id, resume),
            new FakeRepository<CandidateProfile>(x => x.Id, candidate), new FakeRepository<JobPosting>(x => x.Id, job),
            drafts, new FakeUnitOfWork(), ApplicationTestDoubles.Mapper);
        var result = await handler.Handle(new(resume.Id, job.Id, Guid.NewGuid(), DateTimeOffset.UtcNow), default);
        Assert.True(result.IsSuccess);
        Assert.Same(source, drafts.ReceivedBaseVersion);
        Assert.Equal("Verified original", resume.SourceVersion!.ContentMarkdown);
    }

    [Fact]
    public void Parser_PreservesUnknownSectionsAndRejectsUnreadableOrOversizedText()
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        string text = "Alex\nUnknown achievements\nImportant source facts\nSkills\nC#";
        Assert.Equal(text, parser.ParseText(text).SourceText);
        Assert.Throws<UnsupportedResumeFormatException>(() => parser.ParseText(" \n"));
        Assert.Throws<ResumeFileTooLargeException>(() => parser.ParseText(new string('x', ResumeUploadLimits.MaxTextCharacters + 1)));
    }

    [Fact]
    public void ProfilePreview_DoesNotMutateAndImportIsIdempotent()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex", new EmailAddress("alex@example.invalid"), "Engineer");
        candidate.AddSkill(Guid.NewGuid(), "c#");
        ParsedResume parsed = ResumeTextParser.Parse("Skills\nC#, Azure\nExperience\nEngineer | Acme | 2020 - Present\nBuilt products");
        ParsedResume preview = ResumeProfileImporter.Preview(candidate, parsed);
        Assert.Equal("Azure", Assert.Single(preview.Skills).Name);
        Assert.Single(candidate.Skills);
        Assert.Empty(candidate.WorkExperiences);
        ResumeProfileImporter.Apply(candidate, preview);
        ResumeProfileImporter.Apply(candidate, preview);
        Assert.Equal(2, candidate.Skills.Count);
        Assert.Single(candidate.WorkExperiences);
        Assert.Empty(ResumeProfileImporter.Preview(candidate, parsed).Skills);
        Assert.Empty(ResumeProfileImporter.Preview(candidate, parsed).WorkHistory);
    }

    [Fact]
    public void Criteria_NormalizesDefensivelyAndRejectsInvalidUpdatesAtomically()
    {
        string[] titles = [" Engineer ", "engineer"];
        CandidateSearchCriteria criteria = Criteria(titles);
        titles[0] = "Changed";
        Assert.Equal("Engineer", Assert.Single(criteria.JobTitles));
        criteria.JobTitles[0] = "Changed again";
        Assert.Equal("Engineer", criteria.JobTitles[0]);
        Assert.Throws<ArgumentException>(() => criteria.Update([], [], WorkArrangement.Remote, true, [], []));
        Assert.Equal("Engineer", criteria.JobTitles[0]);
        Assert.Throws<ArgumentException>(() => Criteria([new string('x', 201)]));
        Assert.Throws<ArgumentException>(() => Criteria(Enumerable.Repeat("Title", 21).ToArray()));
        Assert.Throws<ArgumentException>(() => Criteria([" "]));
        Assert.Throws<ArgumentNullException>(() => Criteria(null!));
        Assert.Throws<ArgumentException>(() => new CandidateSearchCriteria(Guid.NewGuid(), ["Engineer"], [], 0, true, [], []));
        Assert.Throws<ArgumentException>(() => new CandidateSearchCriteria(Guid.NewGuid(), ["Engineer"], [], (WorkArrangement)8, true, [], []));
        Assert.Throws<ArgumentException>(() => new CandidateSearchCriteria(Guid.NewGuid(), ["Engineer"], [], WorkArrangement.Remote, true, ["Azure"], ["azure"]));
    }

    [Theory]
    [InlineData("Senior Engineer", "Boston", WorkArrangement.Remote, "Azure .NET", true)]
    [InlineData("Senior Architect", "Boston", WorkArrangement.Remote, "Azure .NET", false)]
    [InlineData("Engineer", "Seattle", WorkArrangement.Remote, "Azure .NET", false)]
    [InlineData("Engineer", null, WorkArrangement.Remote, "Azure .NET", false)]
    [InlineData("Engineer", "Boston", WorkArrangement.Hybrid, "Azure .NET", false)]
    [InlineData("Engineer", "Boston", null, "Azure .NET", false)]
    [InlineData("Engineer", "Boston", WorkArrangement.Remote, "Azure", false)]
    [InlineData("Engineer", "Boston", WorkArrangement.Remote, "Azure .NET contract", false)]
    public void Criteria_MatchingCombinesExplicitGroups(string title, string? city, WorkArrangement? arrangement, string description, bool expected)
    {
        CandidateSearchCriteria criteria = new(Guid.NewGuid(), ["Engineer", "Developer"], ["Boston"], WorkArrangement.Remote, false, ["Azure", ".NET"], ["contract"]);
        Assert.Equal(expected, criteria.Matches(title, city, "US", arrangement, description));
    }

    [Fact]
    public void UnknownArrangement_IsIndependentAndEmptyLocationAllowsAnyLocation()
    {
        CandidateSearchCriteria criteria = Criteria(["Engineer"]);
        Assert.True(criteria.Matches("engineer", null, null, null, ""));
        Assert.False(criteria.Matches("engineer", null, null, WorkArrangement.OnSite, ""));
        Assert.Throws<ArgumentException>(() => criteria.Matches("Engineer", null, null, (WorkArrangement)3, ""));
        SearchCriteriaInput input = SearchCriteriaInput.From(criteria);
        Assert.True(new SearchCriteriaInputValidator().Validate(input).IsValid);
        Assert.False(new SearchCriteriaInputValidator().Validate(input with { JobTitles = [] }).IsValid);
        Assert.False(new SaveSearchCriteriaValidator().Validate(new SaveSearchCriteriaCommand(Guid.Empty, input)).IsValid);
    }

    private static CandidateSearchCriteria Criteria(string[] titles) => new(Guid.NewGuid(), titles, [], WorkArrangement.Remote, true, [], []);

    [Fact]
    public void Import_RejectsOversizedFieldsBeforeMutatingProfile()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex", new EmailAddress("alex@example.invalid"), "Engineer");
        ParsedResume parsed = new([new("C#"), new(new string('x', 201))], [], [], []);
        Assert.Throws<UnsupportedResumeFormatException>(() => ResumeProfileImporter.Apply(candidate, parsed));
        Assert.Empty(candidate.Skills);
    }

    [Fact]
    public async Task Parser_RejectsOverPageLimitInsteadOfTruncatingAndHonorsCancellation()
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        UglyToad.PdfPig.Writer.PdfDocumentBuilder builder = new();
        for (int i = 0; i < 51; i++) builder.AddPage(595, 842);
        using MemoryStream pdf = new(builder.Build());
        await Assert.ThrowsAsync<ResumeFileTooLargeException>(() => parser.ParseAsync(pdf, "resume.pdf"));
        using MemoryStream input = new([1, 2, 3]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.ParseAsync(input, "resume.pdf", cancellation.Token));
    }
}
