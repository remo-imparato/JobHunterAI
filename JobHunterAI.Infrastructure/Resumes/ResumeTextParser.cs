using System.Globalization;
using System.Text.RegularExpressions;
using JobHunterAI.Application.Resumes;

namespace JobHunterAI.Infrastructure.Resumes;

/// <summary>
/// Heuristic parser for plain resume text. Expects section headings (Skills, Experience, Education, Certifications) and
/// single-line entries such as "Title | Employer | Jan 2020 - Present" or "Title at Employer (2019 - 2021)".
/// Education uses "Qualification | Institution | 2010 - 2014"; certifications use "Name | Issuer | 2021".
/// Entries that cannot be parsed completely are skipped rather than guessed.
/// </summary>
public static partial class ResumeTextParser
{
    private const string MonthPart = @"(?:(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+|\d{1,2}/)?";
    private const string DatePart = MonthPart + @"(?:19|20)\d{2}";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private static readonly Regex RangeRegex = new(
        $@"(?<s>{DatePart})(?:\s*[-\u2013\u2014]\s*|\s+to\s+)(?<e>{DatePart}|present|current|now)\b",
        Options, Timeout);
    private static readonly Regex YearRegex = new(@"\b(?:19|20)\d{2}\b", Options, Timeout);
    private static readonly Regex MonthYearRegex = new(@"^(?:(?<m>[a-z]+)\.?\s+|(?<n>\d{1,2})/)?(?<y>(?:19|20)\d{2})$", Options, Timeout);
    private static readonly char[] SkillSeparators = [',', ';', '|', '\u2022', '\u00B7', '\u25CF', '\u25AA', '\n'];
    private static readonly char[] BulletChars = ['-', '*', '\u2022', '\u00B7', '\u25CF', '\u25AA', ' ', '\t'];

    private enum Section { None, Skills, Work, Education, Certifications, Other }

    public static ParsedResume Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        List<string> skillLines = [];
        List<string> workLines = [];
        List<string> educationLines = [];
        List<string> certificationLines = [];
        Section current = Section.None;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Replace('\r', ' ').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryGetSection(line, out Section heading))
            {
                current = heading;
                continue;
            }

            switch (current)
            {
                case Section.Skills: skillLines.Add(line); break;
                case Section.Work: workLines.Add(line); break;
                case Section.Education: educationLines.Add(line); break;
                case Section.Certifications: certificationLines.Add(line); break;
            }
        }

        return new ParsedResume(ParseSkills(skillLines), ParseWork(workLines), ParseEducation(educationLines),
            ParseCertifications(certificationLines));
    }

    private static bool TryGetSection(string line, out Section section)
    {
        section = Section.None;
        string heading = line.TrimEnd(':', ' ').ToLowerInvariant();
        if (heading.Length > 40)
        {
            return false;
        }

        switch (heading)
        {
            case "skills": case "technical skills": case "key skills": case "core competencies": case "core skills":
                section = Section.Skills; return true;
            case "experience": case "work experience": case "work history": case "professional experience":
            case "employment history": case "employment":
                section = Section.Work; return true;
            case "education": case "education and training": case "academic background":
                section = Section.Education; return true;
            case "certifications": case "certificates": case "licenses and certifications": case "certifications and licenses":
                section = Section.Certifications; return true;
            case "summary": case "professional summary": case "profile": case "projects": case "awards":
            case "references": case "interests": case "publications": case "languages": case "objective":
                section = Section.Other; return true;
            default:
                return false;
        }
    }

    private static List<ParsedSkill> ParseSkills(List<string> lines)
    {
        List<ParsedSkill> skills = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            string content = line;
            int colon = content.IndexOf(':');
            if (colon > 0 && colon < 30)
            {
                content = content[(colon + 1)..];
            }

            foreach (string part in content.Split(SkillSeparators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim(BulletChars);
                if (name.Length is > 0 and <= 100 && seen.Add(name))
                {
                    skills.Add(new ParsedSkill(name));
                }
            }
        }

        return skills;
    }

    private static List<ParsedWorkExperience> ParseWork(List<string> lines)
    {
        List<(string Title, string Employer, DateOnly Start, DateOnly? End, List<string> Description)> entries = [];
        bool collecting = false;
        foreach (string line in lines)
        {
            if (TryParseHeader(line, out string[] parts, out DateOnly start, out DateOnly? end) && parts.Length >= 2)
            {
                entries.Add((parts[0], parts[1], start, end, []));
                collecting = true;
            }
            else if (collecting)
            {
                entries[^1].Description.Add(line.TrimStart(BulletChars));
            }
        }

        List<ParsedWorkExperience> result = [];
        foreach ((string title, string employer, DateOnly start, DateOnly? end, List<string> description) in entries)
        {
            string details = description.Count > 0 ? string.Join(Environment.NewLine, description) : title;
            result.Add(new ParsedWorkExperience(employer, title, start, end, details));
        }

        return result;
    }

    private static List<ParsedEducation> ParseEducation(List<string> lines)
    {
        List<ParsedEducation> result = [];
        foreach (string line in lines)
        {
            if (TryParseHeader(line, out string[] parts, out DateOnly start, out DateOnly? end) && parts.Length >= 2)
            {
                result.Add(new ParsedEducation(parts[1], parts[0], start, end));
            }
        }

        return result;
    }

    private static List<ParsedCertification> ParseCertifications(List<string> lines)
    {
        List<ParsedCertification> result = [];
        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimStart(BulletChars);
            Match year = YearRegex.Match(line);
            if (!year.Success)
            {
                continue;
            }

            string[] parts = SplitParts(Clean(YearRegex.Replace(line, " ", 1)));
            if (parts.Length >= 2)
            {
                result.Add(new ParsedCertification(parts[0], parts[1],
                    new DateOnly(int.Parse(year.Value, CultureInfo.InvariantCulture), 1, 1), null));
            }
        }

        return result;
    }

    private static bool TryParseHeader(string rawLine, out string[] parts, out DateOnly start, out DateOnly? end)
    {
        parts = [];
        start = default;
        end = null;
        string line = rawLine.TrimStart(BulletChars);
        string remainder;

        Match range = RangeRegex.Match(line);
        if (range.Success)
        {
            if (!TryParseDate(range.Groups["s"].Value, out start))
            {
                return false;
            }

            string endText = range.Groups["e"].Value.Trim();
            if (endText.Equals("present", StringComparison.OrdinalIgnoreCase) ||
                endText.Equals("current", StringComparison.OrdinalIgnoreCase) ||
                endText.Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                end = null;
            }
            else if (TryParseDate(endText, out DateOnly parsedEnd) && parsedEnd >= start)
            {
                end = parsedEnd;
            }
            else
            {
                return false;
            }

            remainder = line.Remove(range.Index, range.Length);
        }
        else
        {
            Match single = YearRegex.Match(line);
            if (!single.Success)
            {
                return false;
            }

            start = new DateOnly(int.Parse(single.Value, CultureInfo.InvariantCulture), 1, 1);
            end = start;
            remainder = line.Remove(single.Index, single.Length);
        }

        parts = SplitParts(Clean(remainder));
        return parts.Length > 0;
    }

    private static string Clean(string value) => value.Trim(' ', '\t', '(', ')', '[', ']', '|', ',', '-', '\u2013', '\u2014', ':');

    private static string[] SplitParts(string value)
    {
        string[] raw;
        if (value.Contains('|'))
        {
            raw = value.Split('|');
        }
        else if (value.Contains(" at ", StringComparison.OrdinalIgnoreCase))
        {
            raw = value.Split(" at ", StringSplitOptions.None);
        }
        else if (value.Contains(" - ") || value.Contains(" \u2013 ") || value.Contains(" \u2014 "))
        {
            raw = value.Split([" - ", " \u2013 ", " \u2014 "], StringSplitOptions.None);
        }
        else
        {
            raw = value.Split(',');
        }

        List<string> parts = [];
        foreach (string part in raw)
        {
            string cleaned = Clean(part);
            if (cleaned.Length is > 0 and <= 200)
            {
                parts.Add(cleaned);
            }
        }

        return [.. parts];
    }

    private static bool TryParseDate(string text, out DateOnly date)
    {
        date = default;
        Match match = MonthYearRegex.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        int year = int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
        int month = 1;
        if (match.Groups["n"].Success)
        {
            month = int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture);
        }
        else if (match.Groups["m"].Success)
        {
            string name = match.Groups["m"].Value;
            string[] months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
            int index = Array.IndexOf(months, name.Length >= 3 ? name[..3].ToLowerInvariant() : string.Empty);
            if (index < 0)
            {
                return false;
            }

            month = index + 1;
        }

        if (month is < 1 or > 12)
        {
            return false;
        }

        date = new DateOnly(year, month, 1);
        return true;
    }
}
