using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using JobHunterAI.Infrastructure.Background;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using JobHunterAI.Application.Common;

namespace JobHunterAI.Api;

[AutomaticRetry(Attempts = 0)]
public sealed class ScheduledCandidateSearch(CandidateSearchWorkflow workflow)
{
    [DisableConcurrentExecution(60)]
    public Task RunAsync(Guid candidateId, Guid runId, CancellationToken token) => workflow.RunAsync(candidateId, runId, token);
    [DisableConcurrentExecution(60)]
    public Task RunScheduledAsync(CancellationToken token) => workflow.RunScheduledAsync(token);
}

[AutomaticRetry(Attempts = 0)]
public sealed class ScheduledJobs(BackgroundJobs jobs, ILogger<ScheduledJobs> logger)
{
    [DisableConcurrentExecution(60)]
    public Task DiscoverJobsAsync(CancellationToken token) => RunAsync("DiscoverJobs", jobs.DiscoverJobsAsync, token);
    [DisableConcurrentExecution(60)]
    public Task RefreshMatchScoresAsync(CancellationToken token) => RunAsync("RefreshMatchScores", jobs.RefreshMatchScoresAsync, token);
    [DisableConcurrentExecution(60)]
    public Task GenerateResumeVariantsAsync(CancellationToken token) => RunAsync("GenerateResumeVariants", jobs.GenerateResumeVariantsAsync, token);
    [DisableConcurrentExecution(60)]
    public Task CleanupLogsAsync(CancellationToken token) => RunAsync("CleanupLogs", jobs.CleanupLogsAsync, token);
    [DisableConcurrentExecution(60)]
    public Task MonitorApplicationsAsync(CancellationToken token) => RunAsync("MonitorApplications", jobs.MonitorApplicationsAsync, token);
    [DisableConcurrentExecution(60)]
    public Task DailySummaryAsync(CancellationToken token) => RunAsync("DailySummary", jobs.DailySummaryAsync, token);

    private async Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken token)
    {
        using Activity? activity = JobHunterDiagnostics.Activities.StartActivity("Background " + operation, ActivityKind.Consumer);
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = Guid.NewGuid().ToString("N"),
            ["TraceId"] = Activity.Current?.TraceId.ToString(), ["Operation"] = operation });
        long started = Stopwatch.GetTimestamp();
        string outcome = "success";
        try { await action(token); }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        catch { outcome = "failure"; activity?.SetStatus(ActivityStatusCode.Error); throw; }
        finally
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            JobHunterDiagnostics.RecordOperation("background", operation, outcome, elapsed);
            logger.LogInformation("Background {Operation} completed with {Outcome} in {ElapsedMs} ms", operation, outcome, elapsed.TotalMilliseconds);
        }
    }
}

public static class BackgroundRegistration
{
    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackgroundOptions>().Bind(configuration.GetSection(BackgroundOptions.SectionName))
            .Validate(o => o.IsValid(), "Invalid Background targets, limits or log directory.").ValidateOnStart();
        services.AddScoped<BackgroundJobs>();
        services.AddScoped<ScheduledJobs>();
        services.AddScoped<CandidateSearchWorkflow>();
        services.AddScoped<ScheduledCandidateSearch>();
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

    public static void RegisterSchedules(IRecurringJobManager manager, bool enableAiJobs, bool enableSearchPipeline = false)
    {
        RecurringJobOptions utc = new() { TimeZone = TimeZoneInfo.Utc };
        if (enableSearchPipeline)
            manager.AddOrUpdate("jobhunter-search-packages", Job.FromExpression<ScheduledCandidateSearch>(j => j.RunScheduledAsync(CancellationToken.None)), "15 */6 * * *", utc);
        else manager.RemoveIfExists("jobhunter-search-packages");
        manager.AddOrUpdate("jobhunter-discover-jobs", Job.FromExpression<ScheduledJobs>(j => j.DiscoverJobsAsync(CancellationToken.None)), "0 */6 * * *", utc);
        manager.AddOrUpdate("jobhunter-cleanup-logs", Job.FromExpression<ScheduledJobs>(j => j.CleanupLogsAsync(CancellationToken.None)), "0 2 * * *", utc);
        manager.AddOrUpdate("jobhunter-monitor-applications", Job.FromExpression<ScheduledJobs>(j => j.MonitorApplicationsAsync(CancellationToken.None)), "0 * * * *", utc);
        manager.AddOrUpdate("jobhunter-daily-summary", Job.FromExpression<ScheduledJobs>(j => j.DailySummaryAsync(CancellationToken.None)), "0 8 * * *", utc);
        if (enableAiJobs && !enableSearchPipeline)
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
        BackgroundRegistration.RegisterSchedules(manager, options.Value.EnableAiJobs, options.Value.EnableSearchPipeline);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
