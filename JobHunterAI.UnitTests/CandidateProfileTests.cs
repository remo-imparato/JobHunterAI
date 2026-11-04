using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class CandidateProfileTests
{
    [Fact]
    public void AddOwnedRecords_AssignsCandidateAndValidatesDates()
    {
        Guid profileId = Guid.NewGuid();
        CandidateProfile profile = new(profileId, "Alex Smith", new EmailAddress("alex@example.com"), "Engineering leader");
        DateOnly start = new(2020, 1, 1);

        Skill skill = profile.AddSkill(Guid.NewGuid(), "C#");
        WorkExperience work = profile.AddWorkExperience(Guid.NewGuid(), "Contoso", "Architect", start, null, "Designed services");
        EducationRecord education = profile.AddEducation(Guid.NewGuid(), "College", "BSc", start, new DateOnly(2024, 1, 1));
        Certification certification = profile.AddCertification(Guid.NewGuid(), "Cloud", "Provider", start, null);

        Assert.Equal(profileId, skill.CandidateProfileId);
        Assert.Equal(profileId, work.CandidateProfileId);
        Assert.Equal(profileId, education.CandidateProfileId);
        Assert.Equal(profileId, certification.CandidateProfileId);
        Assert.Single(profile.Skills);
        Assert.Single(profile.WorkExperiences);
        Assert.Single(profile.EducationRecords);
        Assert.Single(profile.Certifications);
        Assert.Throws<ArgumentException>(() => profile.AddWorkExperience(
            Guid.NewGuid(), "Contoso", "Architect", start, new DateOnly(2019, 1, 1), "Invalid"));
    }

    [Fact]
    public void InvalidContact_LeavesProfileUnchanged()
    {
        EmailAddress originalEmail = new("alex@example.com");
        CandidateProfile profile = new(Guid.NewGuid(), "Alex Smith", originalEmail, "Leader");

        Assert.Throws<ArgumentException>(() => profile.UpdateContact("Different", originalEmail, " "));
        Assert.Throws<ArgumentException>(() => new EmailAddress("not-an-email"));

        Assert.Equal("Alex Smith", profile.FullName);
        Assert.Equal("Leader", profile.Headline);
    }

    [Fact]
    public void DuplicateSkill_IsRejectedIgnoringCase()
    {
        CandidateProfile profile = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        profile.AddSkill(Guid.NewGuid(), "C#");

        Assert.Throws<InvalidOperationException>(() => profile.AddSkill(Guid.NewGuid(), "c#"));
        Assert.Single(profile.Skills);
    }
}
