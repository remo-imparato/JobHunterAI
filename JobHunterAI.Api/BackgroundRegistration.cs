using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using JobHunterAI.Infrastructure.Background;
using Microsoft.Extensions.Options;

namespace JobHunterAI.Api;

[AutomaticRetry(Attempts = 0)]
public sealed class ScheduledJobs(BackgroundJobs jobs)
{
    [DisableConcurrentExecution(60)]
    public Task DiscoverJobsAsync(CancellationToken token) => jobs.DiscoverJobsAsync(token);
    [DisableConcurrentExecution(60)]
    public Task RefreshMatchScoresAsync(CancellationToken token) => jobs.RefreshMatchScoresAsync(token);
    [DisableConcurrentExecution(60)]
    public Task GenerateResumeVariantsAsync(CancellationToken token) => jobs.GenerateResumeVariantsAsync(token);
    [DisableConcurrentExecution(60)]
    public Task CleanupLogsAsync(CancellationToken token) => jobs.CleanupLogsAsync(token);
    [DisableConcurrentExecution(60)]
    public Task MonitorApplicationsAsync(CancellationToken token) => jobs.MonitorApplicationsAsync(token);
    [DisableConcurrentExecution(60)]
    public Task DailySummaryAsync(CancellationToken token) => jobs.DailySummaryAsync(token);
}

public static class BackgroundRegistration
{
    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackgroundOptions>().Bind(configuration.GetSection(BackgroundOptions.SectionName))
            .Validate(o => o.IsValid(), "Invalid Background targets, limits or log directory.").ValidateOnStart();
        services.AddScoped<BackgroundJobs>();
        services.AddScoped<ScheduledJobs>();
        if (!configuration.GetValue<bool>("Background:Enabled")) return services;
        string connection = configuration.GetConnectionString("JobHunterAI")
            ?? throw new InvalidOperationException("ConnectionStrings:JobHunterAI is required for Hangfire.");
        services.AddHangfire(config => config.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(storage => storage.UseNpgsqlConnection(connection), new PostgreSqlStorageOptions
            {
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true
            }));
        services.AddHangfireServer(options => options.WorkerCount = 1);
        services.AddHostedService<RecurringScheduleRegistration>();
        return services;
    }

    public static void RegisterSchedules(IRecurringJobManager manager, bool enableAiJobs)
    {
        RecurringJobOptions utc = new() { TimeZone = TimeZoneInfo.Utc };
        manager.AddOrUpdate("jobhunter-discover-jobs", Job.FromExpression<ScheduledJobs>(j => j.DiscoverJobsAsync(CancellationToken.None)), "0 */6 * * *", utc);
        manager.AddOrUpdate("jobhunter-cleanup-logs", Job.FromExpression<ScheduledJobs>(j => j.CleanupLogsAsync(CancellationToken.None)), "0 2 * * *", utc);
        manager.AddOrUpdate("jobhunter-monitor-applications", Job.FromExpression<ScheduledJobs>(j => j.MonitorApplicationsAsync(CancellationToken.None)), "0 * * * *", utc);
        manager.AddOrUpdate("jobhunter-daily-summary", Job.FromExpression<ScheduledJobs>(j => j.DailySummaryAsync(CancellationToken.None)), "0 8 * * *", utc);
        if (enableAiJobs)
        {
            manager.AddOrUpdate("jobhunter-refresh-match-scores", Job.FromExpression<ScheduledJobs>(j => j.RefreshMatchScoresAsync(CancellationToken.None)), "0 3 * * *", utc);
            manager.AddOrUpdate("jobhunter-generate-resume-variants", Job.FromExpression<ScheduledJobs>(j => j.GenerateResumeVariantsAsync(CancellationToken.None)), "0 4 * * *", utc);
        }
        else
        {
            manager.RemoveIfExists("jobhunter-refresh-match-scores");
            manager.RemoveIfExists("jobhunter-generate-resume-variants");
        }
    }
}

internal sealed class RecurringScheduleRegistration(IRecurringJobManager manager, IOptions<BackgroundOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        BackgroundRegistration.RegisterSchedules(manager, options.Value.EnableAiJobs);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
