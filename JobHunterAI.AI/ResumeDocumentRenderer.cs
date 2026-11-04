using System.IO.Compression;
using System.Net;
using System.Text;
using JobHunterAI.Application.Generation;

namespace JobHunterAI.AI;

/// <summary>Renders resume markdown (headings, bullets, paragraphs) to PDF-ready HTML and a minimal DOCX.</summary>
public sealed class ResumeDocumentRenderer : IResumeDocumentRenderer
{
    public string ToHtml(string markdown, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        StringBuilder body = new();
        bool inList = false;
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.Trim();
            bool bullet = line.StartsWith("- ") || line.StartsWith("* ");
            if (inList && !bullet)
            {
                body.AppendLine("</ul>");
                inList = false;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (bullet)
            {
                if (!inList)
                {
                    body.AppendLine("<ul>");
                    inList = true;
                }

                body.Append("<li>").Append(WebUtility.HtmlEncode(line[2..].Trim())).AppendLine("</li>");
                continue;
            }

            int level = 0;
            while (level < line.Length && line[level] == '#')
            {
                level++;
            }

            if (level is >= 1 and <= 6 && line.Length > level && line[level] == ' ')
            {
                body.Append($"<h{level}>").Append(WebUtility.HtmlEncode(line[(level + 1)..].Trim())).AppendLine($"</h{level}>");
            }
            else
            {
                body.Append("<p>").Append(WebUtility.HtmlEncode(line)).AppendLine("</p>");
            }
        }

        if (inList)
        {
            body.AppendLine("</ul>");
        }

        return "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\" />" +
            "<title>" + WebUtility.HtmlEncode(title) + "</title>" +
            "<style>@page{size:A4;margin:18mm}body{font-family:Calibri,Arial,sans-serif;font-size:11pt;line-height:1.35}" +
            "h1{font-size:20pt;margin:0 0 4pt}h2{font-size:13pt;border-bottom:1px solid #444;margin:12pt 0 4pt}" +
            "h3{font-size:11pt;margin:8pt 0 2pt}ul{margin:2pt 0 6pt 18pt;padding:0}p{margin:2pt 0}</style></head><body>\n" +
            body + "</body></html>";
    }

    public byte[] ToDocx(string markdown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markdown);
        StringBuilder paragraphs = new();
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            bool bold = false;
            int size = 22;
            if (line.StartsWith('#'))
            {
                int level = 0;
                while (level < line.Length && line[level] == '#')
                {
                    level++;
                }

                line = line[level..].Trim();
                bold = true;
                size = level == 1 ? 40 : level == 2 ? 26 : 22;
            }
            else if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                line = "\u2022 " + line[2..].Trim();
            }

            paragraphs.Append("<w:p><w:r><w:rPr>")
                .Append(bold ? "<w:b/>" : string.Empty)
                .Append($"<w:sz w:val=\"{size}\"/></w:rPr><w:t xml:space=\"preserve\">")
                .Append(WebUtility.HtmlEncode(line)).Append("</w:t></w:r></w:p>");
        }

        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                paragraphs + "</w:body></w:document>");
        }

        return output.ToArray();
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using StreamWriter writer = new(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
