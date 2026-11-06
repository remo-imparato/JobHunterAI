using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using DomainApplication = JobHunterAI.Domain.Application;

namespace JobHunterAI.IntegrationTests;

public sealed class PersistenceModelTests
{
    private const string TestConnection = "Host=localhost;Database=model_tests;Username=model_tests";

    [Fact]
    public void Model_MapsAllEntitiesOwnedValuesAndAggregateChildren()
    {
        using JobHunterDbContext context = new(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql(TestConnection).Options);

        Assert.Equal(20, context.Model.GetEntityTypes().Count());
        Assert.True(context.Model.FindEntityType(typeof(CandidateProfile))!
            .FindNavigation(nameof(CandidateProfile.Skills))!.IsEagerLoaded);
        Assert.True(context.Model.FindEntityType(typeof(DomainApplication))!
            .FindNavigation(nameof(DomainApplication.Events))!.IsEagerLoaded);
        Assert.True(context.Model.FindEntityType(typeof(Resume))!
            .FindNavigation(nameof(Resume.Versions))!.IsEagerLoaded);
        Assert.NotNull(context.Model.FindEntityType(typeof(JobPosting))!
            .FindNavigation(nameof(JobPosting.Location)));
        Assert.NotNull(context.Model.FindEntityType(typeof(CandidateProfile))!
            .FindNavigation(nameof(CandidateProfile.Email)));
    }

    [Fact]
    public void Model_UsesNativeConcurrencyAndUniqueProviderIndex()
    {
        using JobHunterDbContext context = new(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql(TestConnection).Options);
        IEntityType posting = context.Model.FindEntityType(typeof(JobPosting))!;
        IProperty version = posting.FindProperty("xmin")!;

        Assert.True(version.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, version.ValueGenerated);
        Assert.Contains(posting.GetIndexes(), index => index.IsUnique &&
            index.Properties.Count == 2 &&
            index.Properties[0].Name == nameof(JobPosting.ProviderName) &&
            index.Properties[1].Name == nameof(JobPosting.ExternalId));
        Assert.DoesNotContain("\"xmin\" xid", context.Database.GenerateCreateScript());
    }

    [Fact]
    public void Registration_SharedUnitOfWorkAndOnlyRootRepositories()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure(TestConnection);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        JobHunterDbContext context = scope.ServiceProvider.GetRequiredService<JobHunterDbContext>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.Same(context, unitOfWork);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRepository<CandidateProfile>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRepository<DomainApplication>>());
        Assert.Null(scope.ServiceProvider.GetService<IRepository<Skill>>());
    }

    [Fact]
    public async Task Repository_RejectsInvalidPagingWithoutDatabaseAccess()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddInfrastructure(TestConnection);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IRepository<JobPosting> repository = scope.ServiceProvider.GetRequiredService<IRepository<JobPosting>>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ListAsync(-1, 10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.ListAsync(0, 101));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetByIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task SavingOwnedRecords_MarksTrackedRootsModifiedForConcurrency()
    {
        using JobHunterDbContext context = new(new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseNpgsql(TestConnection).Options);
        CandidateProfile profile = new(Guid.NewGuid(), "Sample Candidate", new EmailAddress("candidate@example.invalid"), "Leader");
        Resume resume = new(Guid.NewGuid(), profile.Id, "Base resume");
        context.Attach(profile);
        context.Attach(resume);
        profile.AddSkill(Guid.NewGuid(), "C#");
        resume.AddVersion(Guid.NewGuid(), "# Sample", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        context.SavingChanges += (_, _) => throw new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => context.SaveChangesAsync());

        Assert.Equal(EntityState.Modified, context.Entry(profile).State);
        Assert.Equal(EntityState.Modified, context.Entry(resume).State);
    }
}
