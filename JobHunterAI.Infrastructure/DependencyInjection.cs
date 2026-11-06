using JobHunterAI.Application.Persistence;
using JobHunterAI.Application.Resumes;
using JobHunterAI.Infrastructure.Resumes;
using JobHunterAI.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A PostgreSQL connection string is required.", nameof(connectionString));
        }

        services.AddDbContext<JobHunterDbContext>(options => options.UseNpgsql(connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public")));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<JobHunterDbContext>());
        services.AddScoped<JobHunterAI.Application.Generation.IPackageStore, PackageStore>();
        services.AddScoped<JobHunterAI.Application.Jobs.IDiscoveryStore, DiscoveryStore>();
        services.AddScoped<JobHunterAI.Application.Jobs.IPublicJobStore, DiscoveryStore>();
        services.AddScoped<JobHunterAI.Application.Jobs.IDiscoverySources, DiscoverySources>();
        services.AddScoped<JobHunterAI.Application.Candidates.ICandidateLookup, CandidateLookup>();
        services.AddScoped<JobHunterAI.Application.Dashboard.IDashboardReader, DashboardReader>();
        services.AddScoped<IResumeParser, ResumeParser>();
        services.AddScoped<IResumeLookup, ResumeLookup>();
        services.AddScoped<IApplicationLookup, ApplicationLookup>();
        services.AddScoped<IJobPostingLookup, JobPostingLookup>();
        services.AddScoped<IRepository<CandidateProfile>, EfRepository<CandidateProfile>>();
        services.AddScoped<IRepository<CandidateSearchCriteria>, EfRepository<CandidateSearchCriteria>>();
        services.AddScoped<IRepository<Company>, EfRepository<Company>>();
        services.AddScoped<IRepository<JobPosting>, EfRepository<JobPosting>>();
        services.AddScoped<IRepository<Resume>, EfRepository<Resume>>();
        services.AddScoped<IRepository<CoverLetter>, EfRepository<CoverLetter>>();
        services.AddScoped<IRepository<DomainApplication>, EfRepository<DomainApplication>>();
        services.AddScoped<IRepository<AgentExecution>, EfRepository<AgentExecution>>();
        services.AddScoped<IRepository<JobMatchScore>, EfRepository<JobMatchScore>>();
        return services;
    }
}
