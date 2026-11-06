using System.IO.Compression;
using System.Text;
using System.Xml;
using JobHunterAI.Application.Resumes;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobHunterAI.Infrastructure.Resumes;

public sealed class ResumeParser(ILogger<ResumeParser> logger) : IResumeParser
{
    public const int MaxFileBytes = ResumeUploadLimits.MaxFileBytes;
    private const int MaxPdfPages = 50;
    private const long MaxDocxXmlBytes = 20 * 1024 * 1024;

    public async Task<ParsedResume> ParseAsync(Stream content, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        string extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension is not (".pdf" or ".docx"))
        {
            throw new UnsupportedResumeFormatException("Only PDF and DOCX resumes are supported.");
        }

        byte[] bytes = await ReadBoundedAsync(content, cancellationToken);
        string text;
        try
        {
            text = extension == ".pdf" ? ExtractPdf(bytes, cancellationToken) : ExtractDocx(bytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or UnsupportedResumeFormatException))
        {
            logger.LogWarning("Resume text extraction failed for a {Extension} file ({ErrorType}).", extension, exception.GetType().Name);
            throw new UnsupportedResumeFormatException("The resume file could not be read.");
        }

        ParsedResume parsed = ParseText(text);
        logger.LogInformation(
            "Parsed resume: {Skills} skills, {Jobs} jobs, {Education} education records, {Certifications} certifications.",
            parsed.Skills.Count, parsed.WorkHistory.Count, parsed.Education.Count, parsed.Certifications.Count);
        return parsed;
    }

    public ParsedResume ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new UnsupportedResumeFormatException("No readable text was found. Scanned resumes require OCR; upload a text-based PDF or DOCX.");
        if (text.Length > ResumeUploadLimits.MaxTextCharacters)
            throw new ResumeFileTooLargeException("Extracted resume text exceeds 500,000 characters.");
        return ResumeTextParser.Parse(text);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream content, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxFileBytes)
            {
                throw new ResumeFileTooLargeException("The resume file exceeds 10 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string ExtractPdf(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length < 4 || bytes[0] != '%' || bytes[1] != 'P' || bytes[2] != 'D' || bytes[3] != 'F')
        {
            throw new UnsupportedResumeFormatException("The file is not a valid PDF.");
        }

        using PdfDocument document = PdfDocument.Open(bytes);
        StringBuilder text = new();
        if (document.NumberOfPages > MaxPdfPages)
            throw new ResumeFileTooLargeException("PDF resumes must contain at most 50 pages. The source will not be truncated.");
        int pages = document.NumberOfPages;
        for (int i = 1; i <= pages; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.AppendLine(ContentOrderTextExtractor.GetText(document.GetPage(i)));
            EnsureTextLimit(text);
        }

        return text.ToString();
    }

    private static string ExtractDocx(byte[] bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K')
        {
            throw new UnsupportedResumeFormatException("The file is not a valid DOCX.");
        }

        using MemoryStream stream = new(bytes);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.GetEntry("word/document.xml")
            ?? throw new UnsupportedResumeFormatException("The file is not a valid DOCX.");
        if (entry.Length > MaxDocxXmlBytes)
        {
            throw new ResumeFileTooLargeException("The DOCX document XML exceeds the 20 MB extraction limit.");
        }

        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxDocxXmlBytes };
        using Stream xml = entry.Open();
        using XmlReader reader = XmlReader.Create(xml, settings);
        StringBuilder text = new();
        bool inText = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.NamespaceURI.EndsWith("/wordprocessingml/2006/main", StringComparison.Ordinal))
            {
                switch (reader.LocalName)
                {
                    case "t": inText = !reader.IsEmptyElement; break;
                    case "tab": text.Append('\t'); break;
                    case "br": text.Append('\n'); break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "t")
                {
                    inText = false;
                }
                else if (reader.LocalName == "p")
                {
                    text.Append('\n');
                }
            }
            else if (inText && reader.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace)
            {
                text.Append(reader.Value);
            }
            EnsureTextLimit(text);
        }

        return text.ToString();
    }

    private static void EnsureTextLimit(StringBuilder text)
    {
        if (text.Length > ResumeUploadLimits.MaxTextCharacters)
            throw new ResumeFileTooLargeException("Extracted resume text exceeds 500,000 characters. The source will not be truncated.");
    }
}
