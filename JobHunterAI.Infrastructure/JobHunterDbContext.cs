using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Infrastructure;

public sealed class JobHunterDbContext(DbContextOptions<JobHunterDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<CandidateProfile> CandidateProfiles => Set<CandidateProfile>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<CoverLetter> CoverLetters => Set<CoverLetter>();
    public DbSet<DomainApplication> Applications => Set<DomainApplication>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        HashSet<Guid> changedCandidates = [];
        HashSet<Guid> changedResumes = [];
        foreach (EntityEntry entry in ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            switch (entry.Entity)
            {
                case Skill skill:
                    changedCandidates.Add(skill.CandidateProfileId);
                    break;
                case WorkExperience experience:
                    changedCandidates.Add(experience.CandidateProfileId);
                    break;
                case EducationRecord education:
                    changedCandidates.Add(education.CandidateProfileId);
                    break;
                case Certification certification:
                    changedCandidates.Add(certification.CandidateProfileId);
                    break;
                case ResumeVersion version:
                    changedResumes.Add(version.ResumeId);
                    break;
            }
        }

        foreach (EntityEntry<CandidateProfile> entry in ChangeTracker.Entries<CandidateProfile>())
        {
            if (entry.State == EntityState.Unchanged && changedCandidates.Contains(entry.Entity.Id))
            {
                entry.Property(x => x.Headline).IsModified = true;
            }
        }

        foreach (EntityEntry<Resume> entry in ChangeTracker.Entries<Resume>())
        {
            if (entry.State == EntityState.Unchanged && changedResumes.Contains(entry.Entity.Id))
            {
                entry.Property(x => x.Name).IsModified = true;
            }
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        ModelConfiguration.Configure(modelBuilder);
    }
}
