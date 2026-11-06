using JobHunterAI.Domain;

namespace JobHunterAI.Application.Resumes;

public sealed record ParsedSkill(string Name);

public sealed record ParsedWorkExperience(string Employer, string Title, DateOnly Start, DateOnly? End, string Description);

public sealed record ParsedEducation(string Institution, string Qualification, DateOnly Start, DateOnly? End);

public sealed record ParsedCertification(string Name, string Issuer, DateOnly ObtainedOn, DateOnly? ExpiresOn);

public sealed record ParsedResume(
	IReadOnlyList<ParsedSkill> Skills,
	IReadOnlyList<ParsedWorkExperience> WorkHistory,
	IReadOnlyList<ParsedEducation> Education,
	IReadOnlyList<ParsedCertification> Certifications)
{
	public string SourceText { get; init; } = string.Empty;
}

public interface IResumeParser
{
	/// <summary>Parses a PDF or DOCX resume. The file type is determined by the file extension.</summary>
	Task<ParsedResume> ParseAsync(Stream content, string fileName, CancellationToken cancellationToken = default);
	ParsedResume ParseText(string text);
}

public class UnsupportedResumeFormatException(string message) : Exception(message);
public sealed class ResumeFileTooLargeException(string message) : UnsupportedResumeFormatException(message);

public static class ResumeUploadLimits
{
	public const int MaxFileBytes = 10 * 1024 * 1024;
	public const int MaxTextCharacters = 500000;
}

public static class ResumeProfileImporter
{
	public static ParsedResume Preview(CandidateProfile profile, ParsedResume parsed) => new(
		parsed.Skills.Where(x => !HasSkill(profile, x.Name)).ToArray(),
		parsed.WorkHistory.Where(x => !HasWork(profile, x)).ToArray(),
		parsed.Education.Where(x => !HasEducation(profile, x)).ToArray(),
		parsed.Certifications.Where(x => !HasCertification(profile, x)).ToArray());

	/// <summary>Adds parsed items to the profile, skipping items the profile already contains.</summary>
	public static void Apply(CandidateProfile profile, ParsedResume parsed)
	{
		ArgumentNullException.ThrowIfNull(profile);
		ArgumentNullException.ThrowIfNull(parsed);

		if (parsed.Skills.Any(x => x.Name.Length > 200) ||
			parsed.WorkHistory.Any(x => x.Employer.Length > 200 || x.Title.Length > 200) ||
			parsed.Education.Any(x => x.Institution.Length > 200 || x.Qualification.Length > 200) ||
			parsed.Certifications.Any(x => x.Name.Length > 200 || x.Issuer.Length > 200))
		{
			throw new UnsupportedResumeFormatException("Some extracted profile fields exceed 200 characters. The source is preserved; review the text and edit the candidate profile manually.");
		}

		foreach (ParsedSkill skill in parsed.Skills)
		{
			if (!HasSkill(profile, skill.Name))
			{
				profile.AddSkill(Guid.NewGuid(), skill.Name);
			}
		}

		foreach (ParsedWorkExperience job in parsed.WorkHistory)
		{
			if (!HasWork(profile, job))
			{
				profile.AddWorkExperience(Guid.NewGuid(), job.Employer, job.Title, job.Start, job.End, job.Description);
			}
		}

		foreach (ParsedEducation education in parsed.Education)
		{
			if (!HasEducation(profile, education))
			{
				profile.AddEducation(Guid.NewGuid(), education.Institution, education.Qualification, education.Start, education.End);
			}
		}

		foreach (ParsedCertification certification in parsed.Certifications)
		{
			if (!HasCertification(profile, certification))
			{
				profile.AddCertification(Guid.NewGuid(), certification.Name, certification.Issuer,
					certification.ObtainedOn, certification.ExpiresOn);
			}
		}
	}

	private static bool HasSkill(CandidateProfile profile, string name)
	{
		foreach (Skill existing in profile.Skills)
		{
			if (Same(existing.Name, name))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasWork(CandidateProfile profile, ParsedWorkExperience job)
	{
		foreach (WorkExperience existing in profile.WorkExperiences)
		{
			if (Same(existing.Employer, job.Employer) && Same(existing.Title, job.Title) && existing.Start == job.Start)
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasEducation(CandidateProfile profile, ParsedEducation education)
	{
		foreach (EducationRecord existing in profile.EducationRecords)
		{
			if (Same(existing.Institution, education.Institution) && Same(existing.Qualification, education.Qualification))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasCertification(CandidateProfile profile, ParsedCertification certification)
	{
		foreach (Certification existing in profile.Certifications)
		{
			if (Same(existing.Name, certification.Name) && Same(existing.Issuer, certification.Issuer))
			{
				return true;
			}
		}

		return false;
	}

	private static bool Same(string left, string right) =>
		string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
