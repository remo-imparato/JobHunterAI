using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobHunterAI.Infrastructure;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(JobHunterDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(logger);
        await context.Database.MigrateAsync(cancellationToken);

        Guid candidateId = Guid.Parse("e509e6f7-1060-4314-9252-685ca37a8ba7");
        Guid companyId = Guid.Parse("d04853ce-c594-4172-98ec-ecfd02129b43");
        Guid postingId = Guid.Parse("05947aa7-a22b-4940-9503-cd3ac261f074");
        if (!await context.CandidateProfiles.AnyAsync(x => x.Id == candidateId, cancellationToken))
        {
            context.CandidateProfiles.Add(new CandidateProfile(candidateId, "Sample Candidate",
                new EmailAddress("candidate@example.invalid"), "Engineering leader"));
        }

        if (!await context.Companies.AnyAsync(x => x.Id == companyId, cancellationToken))
        {
            context.Companies.Add(new Company(companyId, "Example Company"));
        }

        if (!await context.JobPostings.AnyAsync(x => x.Id == postingId, cancellationToken))
        {
            context.JobPostings.Add(new JobPosting(postingId, companyId, "DevelopmentSeed", "example-001",
                "Sample Engineering Manager", "Example opportunity for local development only.",
                new JobLocation(null, null, true), "https://example.invalid/jobs/example-001"));
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Development sample data is available");
    }
}
