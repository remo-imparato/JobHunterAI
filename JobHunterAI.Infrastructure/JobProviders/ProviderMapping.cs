using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobHunterAI.Domain;

namespace JobHunterAI.Infrastructure.JobProviders;

internal static class ProviderMapping
{
    public static string Identifier(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100)
        {
            throw new ArgumentException($"{name} must contain only ASCII letters, digits, hyphens or underscores.", name);
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))
            {
                throw new ArgumentException($"{name} must contain only ASCII letters, digits, hyphens or underscores.", name);
            }
        }

        return value;
    }

    public static Guid CompanyId(Guid value) => value != Guid.Empty
        ? value : throw new ArgumentException("A company ID is required.", nameof(value));

    public static string Required(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement value))
        {
            throw new JsonException($"Missing required property: {property}.");
        }

        string? text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
        return !string.IsNullOrWhiteSpace(text) ? text : throw new JsonException($"Invalid property: {property}.");
    }

    public static string? Optional(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static JobLocation Location(string? name, bool isRemote = false)
    {
        string? text = name?.Trim();
        isRemote |= text?.Contains("remote", StringComparison.OrdinalIgnoreCase) == true;
        string[] parts = text?.Split(',', 2, StringSplitOptions.TrimEntries) ?? [];
        if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
        {
            return new JobLocation(parts[0], parts[1], isRemote);
        }

        return new JobLocation(null, null, true);
    }

    public static DateTimeOffset? Timestamp(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value)
            ? value : null;

    public static DateTimeOffset? UnixMilliseconds(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out long milliseconds) ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null;

    public static string PlainText(string html)
    {
        string withoutScripts = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        return WebUtility.HtmlDecode(Regex.Replace(withoutScripts, @"<[^>]+>", " ",
            RegexOptions.Singleline, TimeSpan.FromSeconds(1))).Trim();
    }

    public static JobPosting Job(string provider, string board, string id, Guid companyId, string title,
        string description, JobLocation location, string applicationUrl, DateTimeOffset? postedAt)
    {
        string externalId = $"{board}:{id}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{externalId}"));
        return new JobPosting(new Guid(hash.AsSpan(0, 16)), companyId, provider, externalId,
            title, description, location, applicationUrl, postedAt);
    }
}
