using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Infrastructure;

internal static class ModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CandidateProfile>(entity =>
        {
            entity.ToTable("candidate_profiles", table => table.HasCheckConstraint(
                "CK_candidate_profiles_Username", "\"Username\" ~ '^[a-z0-9._-]{1,64}$'"));
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.Headline).HasMaxLength(300).IsRequired();
            entity.OwnsOne(x => x.Email, email =>
                email.Property(x => x.Value).HasColumnName("Email").HasMaxLength(320).IsRequired());
            entity.Navigation(x => x.Email).IsRequired();
            entity.HasMany(x => x.Skills).WithOne().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.WorkExperiences).WithOne().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.EducationRecords).WithOne().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Certifications).WithOne().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Skills).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            entity.Navigation(x => x.WorkExperiences).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            entity.Navigation(x => x.EducationRecords).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
            entity.Navigation(x => x.Certifications).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });
        modelBuilder.Entity<ApplicationPackage>(entity =>
        {
            entity.ToTable("application_packages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            Concurrency(entity);
            entity.HasIndex(x => new { x.CandidateProfileId, x.JobPostingId, x.ResumeId }).IsUnique();
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Resume>().WithMany().HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ResumeVersion>().WithMany().HasForeignKey(x => x.SourceVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ResumeVersion>().WithMany().HasForeignKey(x => x.ResumeVersionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<CoverLetter>().WithMany().HasForeignKey(x => x.CoverLetterId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CandidateSearchCriteria>(entity =>
        {
            entity.ToTable("candidate_search_criteria");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithOne().HasForeignKey<CandidateSearchCriteria>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
            entity.Property(x => x.JobTitles).HasField("_jobTitles").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]");
            entity.Property(x => x.Locations).HasField("_locations").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]");
            entity.Property(x => x.RequiredKeywords).HasField("_requiredKeywords").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]");
            entity.Property(x => x.ExcludedKeywords).HasField("_excludedKeywords").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]");
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.ToTable("skills");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => new { x.CandidateProfileId, x.Name }).IsUnique();
        });
        modelBuilder.Entity<WorkExperience>(entity =>
        {
            entity.ToTable("work_experiences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Employer).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.HasIndex(x => x.CandidateProfileId);
        });
        modelBuilder.Entity<EducationRecord>(entity =>
        {
            entity.ToTable("education_records");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Institution).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Qualification).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.CandidateProfileId);
        });
        modelBuilder.Entity<Certification>(entity =>
        {
            entity.ToTable("certifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Issuer).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.CandidateProfileId);
        });

        modelBuilder.Entity<JobDiscoveryRun>(entity =>
        {
            entity.ToTable("job_discovery_runs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.CriteriaSnapshot).IsRequired();
            entity.Property(x => x.Warnings).HasField("_warnings").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]");
            entity.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Results).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.CandidateProfileId, x.StartedAt });
        });
        modelBuilder.Entity<JobDiscoveryResult>(entity =>
        {
            entity.ToTable("job_discovery_results");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RunId, x.JobPostingId }).IsUnique();
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.WebsiteUrl).HasMaxLength(2048);
            entity.HasIndex(x => x.Name);
        });
        modelBuilder.Entity<JobPosting>(entity =>
        {
            entity.ToTable("job_postings");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ProviderName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Description).IsRequired();
            entity.Property(x => x.ApplicationUrl).HasMaxLength(2048).IsRequired();
            entity.OwnsOne(x => x.Location, location =>
            {
                location.Property(x => x.City).HasColumnName("City").HasMaxLength(200);
                location.Property(x => x.Country).HasColumnName("Country").HasMaxLength(200);
                location.Property(x => x.IsRemote).HasColumnName("IsRemote").IsRequired();
                location.Property(x => x.Arrangement).HasColumnName("WorkArrangement");
            });
            entity.Navigation(x => x.Location).IsRequired();
            entity.HasIndex(x => new { x.ProviderName, x.ExternalId }).IsUnique();
            entity.HasIndex(x => x.PostedAt);
        });

        modelBuilder.Entity<Resume>(entity =>
        {
            entity.ToTable("resumes");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.CandidateProfileId);
            entity.Ignore(x => x.SourceVersion);
            entity.Property(x => x.SourceFileName).HasMaxLength(200);
            entity.Property(x => x.SourceSha256).HasMaxLength(64);
            entity.HasIndex(x => new { x.CandidateProfileId, x.SourceSha256 }).IsUnique();
            entity.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.ResumeId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });
        modelBuilder.Entity<ResumeVersion>(entity =>
        {
            entity.ToTable("resume_versions", table => table.HasCheckConstraint(
                "CK_resume_versions_SourceNotTailored", "NOT \"IsSource\" OR \"JobPostingId\" IS NULL"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ContentMarkdown).IsRequired();
            entity.HasIndex(x => new { x.ResumeId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.ResumeId).IsUnique().HasFilter("\"IsSource\"");
            entity.HasIndex(x => x.JobPostingId);
        });
        modelBuilder.Entity<CoverLetter>(entity =>
        {
            entity.ToTable("cover_letters");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ContentMarkdown).IsRequired();
            entity.HasIndex(x => new { x.CandidateProfileId, x.JobPostingId });
            entity.HasIndex(x => x.JobPostingId);
        });

        modelBuilder.Entity<DomainApplication>(entity =>
        {
            entity.ToTable("applications");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.CandidateProfileId, x.JobPostingId }).IsUnique();
            entity.HasIndex(x => x.JobPostingId);
            entity.HasIndex(x => x.Status);
            entity.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Events).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        });
        modelBuilder.Entity<ApplicationEvent>(entity =>
        {
            entity.ToTable("application_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.ApplicationId, x.OccurredAt });
        });
        modelBuilder.Entity<JobMatchScore>(entity =>
        {
            entity.ToTable("job_match_scores");
            entity.HasKey(x => x.Id);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.CandidateProfileId, x.JobPostingId, x.ScoredAt });
            entity.HasIndex(x => x.JobPostingId);
        });
        modelBuilder.Entity<AgentExecution>(entity =>
        {
            entity.ToTable("agent_executions");
            entity.HasKey(x => x.Id);
            Concurrency(entity);
            entity.HasOne<CandidateProfile>().WithMany().HasForeignKey(x => x.CandidateProfileId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DomainApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.AgentName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FailureCode).HasMaxLength(100);
            entity.HasIndex(x => new { x.CandidateProfileId, x.StartedAt });
            entity.HasIndex(x => x.ApplicationId);
        });
    }

    private static void Concurrency<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.Property<uint>("xmin").IsRowVersion();
    }
}
