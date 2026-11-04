namespace JobHunterAI.Application.Jobs;

internal static class JobInputRules
{
	public static bool IsHttpUrl(string? value) =>
		Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
		(uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
		string.IsNullOrEmpty(uri.UserInfo);

	public static bool HasValidLocation(string? city, string? country, bool isRemote) =>
		(isRemote || (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))) &&
		(string.IsNullOrWhiteSpace(city) || !string.IsNullOrWhiteSpace(country));
}
