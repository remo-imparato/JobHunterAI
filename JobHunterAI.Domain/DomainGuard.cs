namespace JobHunterAI.Domain;

internal static class DomainGuard
{
    public static string Required(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }

    public static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }

        return value;
    }

    public static string HttpUrl(string? value, string name)
    {
        string url = Required(value, name);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException($"{name} must be an absolute HTTP or HTTPS URL without embedded credentials.", name);
        }

        return url;
    }

    public static void DateRange(DateOnly start, DateOnly? end)
    {
        if (start == default || (end.HasValue && (end.Value == default || end.Value < start)))
        {
            throw new ArgumentException("The date range is invalid.", nameof(end));
        }
    }

    public static DateTimeOffset Timestamp(DateTimeOffset value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }

        return value;
    }
}
