using System.IO.Compression;
using System.Text;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure.Resumes;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace JobHunterAI.UnitTests;

public sealed class ResumeParserTests
{
    private static readonly string[] ResumeLines =
    [
        "Alex Smith",
        "Skills",
        "C#, .NET, Azure; Leadership",
        "Languages: SQL | c#",
        "Experience",
        "Engineering Manager | Acme Corp | Jan 2020 - Present",
        "- Led 12 engineers",
        "Senior Developer at Globex (2015 - 2019)",
        "Built APIs",
        "Education",
        "BSc Computer Science | State University | 2008 - 2012",
        "Certifications",
        "AWS Solutions Architect | Amazon | 2021"
    ];

    [Fact]
    public void Parse_ExtractsAllSections()
    {
        ParsedResume parsed = ResumeTextParser.Parse(string.Join('\n', ResumeLines));

        Assert.Equal(["C#", ".NET", "Azure", "Leadership", "SQL"], parsed.Skills.Select(x => x.Name));
        Assert.Equal(2, parsed.WorkHistory.Count);
        ParsedWorkExperience first = parsed.WorkHistory[0];
        Assert.Equal("Engineering Manager", first.Title);
        Assert.Equal("Acme Corp", first.Employer);
        Assert.Equal(new DateOnly(2020, 1, 1), first.Start);
        Assert.Null(first.End);
        Assert.Equal("Led 12 engineers", first.Description);
        Assert.Equal("Globex", parsed.WorkHistory[1].Employer);
        Assert.Equal(new DateOnly(2019, 1, 1), parsed.WorkHistory[1].End);
        ParsedEducation education = Assert.Single(parsed.Education);
        Assert.Equal("State University", education.Institution);
        Assert.Equal("BSc Computer Science", education.Qualification);
        ParsedCertification certification = Assert.Single(parsed.Certifications);
        Assert.Equal("Amazon", certification.Issuer);
        Assert.Equal(new DateOnly(2021, 1, 1), certification.ObtainedOn);
    }

    [Fact]
    public void Parse_SkipsIncompleteEntriesInsteadOfGuessing()
    {
        ParsedResume parsed = ResumeTextParser.Parse("Experience\nManager 2020 - 2018\nCertifications\nSome cert without issuer 2020");

        Assert.Empty(parsed.WorkHistory);
        Assert.Empty(parsed.Certifications);
    }

    [Fact]
    public async Task ParseAsync_ReadsDocx()
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        using MemoryStream docx = new(BuildDocx(ResumeLines));

        ParsedResume parsed = await parser.ParseAsync(docx, "resume.docx");

        Assert.Equal(5, parsed.Skills.Count);
        Assert.Equal(2, parsed.WorkHistory.Count);
        Assert.Single(parsed.Education);
        Assert.Single(parsed.Certifications);
    }

    [Fact]
    public async Task ParseAsync_ReadsPdf()
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        using MemoryStream pdf = new(BuildPdf(ResumeLines));

        ParsedResume parsed = await parser.ParseAsync(pdf, "resume.pdf");

        Assert.Equal(2, parsed.WorkHistory.Count);
        Assert.Contains(parsed.Skills, x => x.Name == "Azure");
        Assert.Single(parsed.Education);
    }

    [Theory]
    [InlineData("resume.txt", "hello")]
    [InlineData("resume.pdf", "not a pdf")]
    [InlineData("resume.docx", "not a docx")]
    public async Task ParseAsync_RejectsUnsupportedOrInvalidFiles(string fileName, string content)
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(content));

        await Assert.ThrowsAsync<UnsupportedResumeFormatException>(() => parser.ParseAsync(stream, fileName));
    }

    [Fact]
    public async Task ParseAsync_RejectsOversizedFiles()
    {
        ResumeParser parser = new(NullLogger<ResumeParser>.Instance);
        using MemoryStream stream = new(new byte[ResumeParser.MaxFileBytes + 1]);

        await Assert.ThrowsAsync<ResumeFileTooLargeException>(() => parser.ParseAsync(stream, "resume.pdf"));
    }

    [Fact]
    public void Importer_StoresParsedDataInProfileAndSkipsDuplicates()
    {
        CandidateProfile profile = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        profile.AddSkill(Guid.NewGuid(), "c#");
        ParsedResume parsed = ResumeTextParser.Parse(string.Join('\n', ResumeLines));

        ResumeProfileImporter.Apply(profile, parsed);
        ResumeProfileImporter.Apply(profile, parsed);

        Assert.Equal(5, profile.Skills.Count);
        Assert.Equal(2, profile.WorkExperiences.Count);
        Assert.Single(profile.EducationRecords);
        Assert.Single(profile.Certifications);
    }

    private static byte[] BuildDocx(IEnumerable<string> lines)
    {
        StringBuilder body = new();
        foreach (string line in lines)
        {
            body.Append("<w:p><w:r><w:t xml:space=\"preserve\">")
                .Append(System.Security.SecurityElement.Escape(line)).Append("</w:t></w:r></w:p>");
        }

        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
            + body + "</w:body></w:document>";
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, true))
        {
            using StreamWriter writer = new(archive.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }

        return output.ToArray();
    }

    private static byte[] BuildPdf(IEnumerable<string> lines)
    {
        PdfDocumentBuilder builder = new();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(595, 842);
        double y = 800;
        foreach (string line in lines)
        {
            page.AddText(line, 10, new UglyToad.PdfPig.Core.PdfPoint(30, y), font);
            y -= 16;
        }

        return builder.Build();
    }
}
