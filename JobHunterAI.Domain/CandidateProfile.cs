namespace JobHunterAI.Domain;

public sealed class CandidateProfile
{
    private readonly List<Skill> _skills = [];
    private readonly List<WorkExperience> _workExperiences = [];
    private readonly List<EducationRecord> _educationRecords = [];
    private readonly List<Certification> _certifications = [];

    private CandidateProfile() { }

    public CandidateProfile(Guid id, string fullName, EmailAddress email, string headline)
    {
        Id = DomainGuard.Id(id, nameof(id));
        FullName = DomainGuard.Required(fullName, nameof(fullName));
        Email = email ?? throw new ArgumentNullException(nameof(email));
        Headline = DomainGuard.Required(headline, nameof(headline));
    }

    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public EmailAddress Email { get; private set; } = null!;
    public string Headline { get; private set; } = string.Empty;
    public IReadOnlyCollection<Skill> Skills => _skills.AsReadOnly();
    public IReadOnlyCollection<WorkExperience> WorkExperiences => _workExperiences.AsReadOnly();
    public IReadOnlyCollection<EducationRecord> EducationRecords => _educationRecords.AsReadOnly();
    public IReadOnlyCollection<Certification> Certifications => _certifications.AsReadOnly();

    public void UpdateContact(string fullName, EmailAddress email, string headline)
    {
        string validatedName = DomainGuard.Required(fullName, nameof(fullName));
        ArgumentNullException.ThrowIfNull(email);
        string validatedHeadline = DomainGuard.Required(headline, nameof(headline));

        FullName = validatedName;
        Email = email;
        Headline = validatedHeadline;
    }

    public Skill AddSkill(Guid id, string name)
    {
        Skill skill = new(id, Id, name);
        foreach (Skill existing in _skills)
        {
            if (existing.Id == id || string.Equals(existing.Name, skill.Name, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The candidate already has this skill.");
            }
        }

        _skills.Add(skill);
        return skill;
    }

    public WorkExperience AddWorkExperience(Guid id, string employer, string title, DateOnly start, DateOnly? end, string description)
    {
        WorkExperience experience = new(id, Id, employer, title, start, end, description);
        foreach (WorkExperience existing in _workExperiences)
        {
            if (existing.Id == id)
            {
                throw new InvalidOperationException("The candidate already has this work experience.");
            }
        }

        _workExperiences.Add(experience);
        return experience;
    }

    public EducationRecord AddEducation(Guid id, string institution, string qualification, DateOnly start, DateOnly? end)
    {
        EducationRecord education = new(id, Id, institution, qualification, start, end);
        foreach (EducationRecord existing in _educationRecords)
        {
            if (existing.Id == id)
            {
                throw new InvalidOperationException("The candidate already has this education record.");
            }
        }

        _educationRecords.Add(education);
        return education;
    }

    public Certification AddCertification(Guid id, string name, string issuer, DateOnly obtainedOn, DateOnly? expiresOn)
    {
        Certification certification = new(id, Id, name, issuer, obtainedOn, expiresOn);
        foreach (Certification existing in _certifications)
        {
            if (existing.Id == id)
            {
                throw new InvalidOperationException("The candidate already has this certification.");
            }
        }

        _certifications.Add(certification);
        return certification;
    }
}
