using JobHunterAI.Domain;

namespace JobHunterAI.Application.Common;

public sealed record JobLocationDto(string? City, string? Country, bool IsRemote);

public sealed record JobDto(
	Guid Id, Guid CompanyId, string ProviderName, string ExternalId, string Title, string Description,
	JobLocationDto Location, string ApplicationUrl, DateTimeOffset? PostedAt);

public sealed record ApplicationDto(
	Guid Id, Guid CandidateProfileId, Guid JobPostingId, DateTimeOffset CreatedAt,
	ApplicationStatus Status, Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt);

public sealed record ResumeVersionDto(
	Guid Id, Guid ResumeId, int VersionNumber, string ContentMarkdown, DateTimeOffset CreatedAt, Guid? JobPostingId);

public sealed record CoverLetterDto(
	Guid Id, Guid CandidateProfileId, Guid JobPostingId, string ContentMarkdown, DateTimeOffset CreatedAt);
