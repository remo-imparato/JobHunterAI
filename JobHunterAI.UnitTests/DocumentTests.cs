using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class DocumentTests
{
    private static readonly DateTimeOffset CreatedAt = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AddResumeVersion_SequencesVersionsAndLinksTailoredJob()
    {
        Resume resume = new(Guid.NewGuid(), Guid.NewGuid(), "Master resume");
        Guid jobId = Guid.NewGuid();

        ResumeVersion original = resume.AddVersion(Guid.NewGuid(), "# Base resume", CreatedAt);
        ResumeVersion tailored = resume.AddVersion(Guid.NewGuid(), "# Tailored resume", CreatedAt.AddDays(1), jobId);

        Assert.Equal(1, original.VersionNumber);
        Assert.Equal(2, tailored.VersionNumber);
        Assert.Equal(jobId, tailored.JobPostingId);
        Assert.Equal(resume.Id, tailored.ResumeId);
        Assert.Equal(2, resume.Versions.Count);
        Assert.Throws<ArgumentException>(() => resume.AddVersion(Guid.NewGuid(), " ", CreatedAt));
        Assert.Equal(2, resume.Versions.Count);
    }

    [Fact]
    public void CoverLetter_RequiresContentAndRelatedIdentifiers()
    {
        CoverLetter letter = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Dear team", CreatedAt);

        Assert.Throws<ArgumentException>(() => letter.UpdateContent(" "));
        Assert.Throws<ArgumentException>(() => new CoverLetter(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(),
            "Dear team", CreatedAt));
        Assert.Equal("Dear team", letter.ContentMarkdown);
    }
}
