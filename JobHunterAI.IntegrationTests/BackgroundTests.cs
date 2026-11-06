using System.Reflection;
using Hangfire;
using JobHunterAI.Api;
using JobHunterAI.Infrastructure.Background;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunterAI.IntegrationTests;

public sealed class BackgroundTests
{
    [Fact]
    public void Options_DefaultToSafeDisabledConfiguration()
    {
        BackgroundOptions options = new();
        Assert.False(options.Enabled);
        Assert.False(options.EnableAiJobs);
        Assert.True(options.IsValid());
        Assert.Null(options.LogDirectory);
        Assert.Empty(options.Candidates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Options_RejectUnboundedBatch(int size) => Assert.False(new BackgroundOptions { BatchSize = size }.IsValid());

    [Fact]
    public void Options_RejectInvalidAndDuplicateTargets()
    {
        Assert.False(new BackgroundOptions { Candidates = [new()] }.IsValid());
        Guid id = Guid.NewGuid();
        Assert.False(new BackgroundOptions { Candidates = [new() { CandidateId = id }, new() { CandidateId = id }] }.IsValid());
        Assert.False(new BackgroundOptions { Boards = [new() { Provider = "Unknown", CompanyId = id, Board = "test" }] }.IsValid());
        Assert.False(new BackgroundOptions { LogDirectory = "relative/logs" }.IsValid());
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 6)]
    public void Schedules_HaveStableIdsUtcAndExplicitAiOptIn(bool ai, int count)
    {
        var manager = DispatchProxy.Create<IRecurringJobManager, ManagerProxy>();
        var proxy = (ManagerProxy)manager;
        BackgroundRegistration.RegisterSchedules(manager, ai);
        Assert.Equal(count, proxy.Added.Count);
        Assert.All(proxy.Added, args =>
        {
            Assert.StartsWith("jobhunter-", (string)args[0]!);
            Assert.Equal(TimeZoneInfo.Utc, ((RecurringJobOptions)args[3]!).TimeZone);
        });
        Assert.Equal(ai ? 0 : 2, proxy.Removed.Count);
        BackgroundRegistration.RegisterSchedules(manager, ai);
        Assert.Equal(count, proxy.Added.Take(count).Select(a => a[0]).Distinct().Count());
    }

    [Fact]
    public async Task DisabledJobs_DoNotStartHangfireOrAccessDatabase()
    {
        using var factory = new Factory();
        using var scope = factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IRecurringJobManager>());
        BackgroundJobs jobs = scope.ServiceProvider.GetRequiredService<BackgroundJobs>();
        await jobs.DiscoverJobsAsync(CancellationToken.None);
        await jobs.RefreshMatchScoresAsync(CancellationToken.None);
        await jobs.GenerateResumeVariantsAsync(CancellationToken.None);
        await jobs.CleanupLogsAsync(CancellationToken.None);
        await jobs.MonitorApplicationsAsync(CancellationToken.None);
        await jobs.DailySummaryAsync(CancellationToken.None);
    }

    [Fact]
    public void Cleanup_DeletesOnlyExpiredOwnedTopLevelLogsAndHonorsLimit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jh-retention-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string old = Path.Combine(directory, "jobhunterai-old.log");
            string recent = Path.Combine(directory, "jobhunterai-current.log");
            string unrelated = Path.Combine(directory, "unrelated.log");
            File.WriteAllText(old, "old");
            File.WriteAllText(recent, "current");
            File.WriteAllText(unrelated, "unrelated");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
            File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-40));
            string sub = Directory.CreateDirectory(Path.Combine(directory, "nested")).FullName;
            string nested = Path.Combine(sub, "jobhunterai-nested.log");
            File.WriteAllText(nested, "nested");
            File.SetLastWriteTimeUtc(nested, DateTime.UtcNow.AddDays(-40));
            Assert.Equal(1, LogRetention.Cleanup(directory, DateTime.UtcNow.AddDays(-30), 1, CancellationToken.None));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(recent));
            Assert.True(File.Exists(unrelated));
            Assert.True(File.Exists(nested));
            Assert.Equal(0, LogRetention.Cleanup(directory, DateTime.UtcNow.AddDays(-30), 1, CancellationToken.None));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Cleanup_CancellationPreservesFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "jh-retention-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "jobhunterai-old.log");
            File.WriteAllText(path, "log");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-40));
            using CancellationTokenSource source = new();
            source.Cancel();
            Assert.Throws<OperationCanceledException>(() => LogRetention.Cleanup(directory, DateTime.UtcNow.AddDays(-30), 10, source.Token));
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    public class ManagerProxy : DispatchProxy
    {
        public List<object?[]> Added { get; } = [];
        public List<object?[]> Removed { get; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "AddOrUpdate") Added.Add(args!);
            if (targetMethod.Name == "RemoveIfExists") Removed.Add(args!);
            return targetMethod.ReturnType == typeof(string) ? "test" : null;
        }
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.UseSetting("Background:Enabled", "false");
        }
    }
}
