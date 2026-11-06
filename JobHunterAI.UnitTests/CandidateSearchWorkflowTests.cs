using System.Reflection;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using JobHunterAI.Infrastructure.Background;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class CandidateSearchWorkflowTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Workflow_PreservesSearchWithoutDraftingWhenAiDisabledOrUnavailable(bool enabled, bool available)
    {
        using JobHunterDbContext db = Context();
        DiscoveryRunDetails details = Details();
        int searches = 0;
        ISender sender = Proxy<ISender>((_, args) =>
        {
            var command = Assert.IsType<StartDiscoveryCommand>(args![0]);
            Assert.Equal(details.Run.Id, command.RunId);
            searches++;
            return Task.FromResult(Result<DiscoveryRunDetails>.Success(details));
        });
        IDiscoveryStore discovery = Proxy<IDiscoveryStore>((method, _) => method.Name == nameof(IDiscoveryStore.DetailsAsync)
            ? Task.FromResult<DiscoveryRunDetails?>(null) : throw new InvalidOperationException("Unexpected persistence call"));
        CandidateSearchWorkflow workflow = new(db, sender, Options.Create(new BackgroundOptions { EnableAiJobs = enabled }),
            new Drafting(available), Never<IPackageStore>(), NullLogger<CandidateSearchWorkflow>.Instance, discovery);

        await workflow.RunAsync(Guid.NewGuid(), details.Run.Id, default);

        Assert.Equal(1, searches);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Workflow_ReusesCompletedSearchOnWorkerRedelivery()
    {
        using JobHunterDbContext db = Context();
        DiscoveryRunDetails details = Details();
        IDiscoveryStore discovery = Proxy<IDiscoveryStore>((method, _) => method.Name == nameof(IDiscoveryStore.DetailsAsync)
            ? Task.FromResult<DiscoveryRunDetails?>(details) : throw new InvalidOperationException("Unexpected persistence call"));
        CandidateSearchWorkflow workflow = new(db, Never<ISender>(), Options.Create(new BackgroundOptions()),
            new Drafting(false), Never<IPackageStore>(), NullLogger<CandidateSearchWorkflow>.Instance, discovery);

        await workflow.RunAsync(Guid.NewGuid(), details.Run.Id, default);

        Assert.Empty(db.ChangeTracker.Entries());
    }

    private static DiscoveryRunDetails Details() => new(new(Guid.NewGuid(), DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, DiscoveryStatus.Completed, 0, 0, []), "{}", []);
    private static JobHunterDbContext Context() => new(new DbContextOptionsBuilder<JobHunterDbContext>()
        .UseNpgsql("Host=localhost;Database=not_accessed;Username=tests").Options);
    private static T Never<T>() where T : class => Proxy<T>((method, _) => throw new InvalidOperationException($"Unexpected {method.Name}"));
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> implementation) where T : class
    {
        T instance = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)instance).Implementation = implementation;
        return instance;
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Implementation { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Implementation(targetMethod!, args);
    }
    private sealed class Drafting(bool available) : IPackageDraftingService
    {
        public bool IsAvailable => available;
        public Task<string> ResumeAsync(CandidateProfile candidate, JobPosting snapshot, string sourceText, CancellationToken token) => throw new InvalidOperationException("Unexpected drafting");
        public Task<string> CoverLetterAsync(CandidateProfile candidate, JobPosting snapshot, string company, string sourceText, CancellationToken token) => throw new InvalidOperationException("Unexpected drafting");
    }
}
