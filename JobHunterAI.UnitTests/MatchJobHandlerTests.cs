using JobHunterAI.Application.AI;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Matching;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class MatchJobHandlerTests
{
    [Fact]
    public void JobMatchScore_RejectsOutOfRangeScore()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JobMatchScore(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            101, [], [], [], [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Handle_PersistsScoreAndReturnsDto()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "acme:1", "Engineering Manager", "Lead",
            new JobLocation(null, null, true), "https://example.com/apply");
        Repo<JobMatchScore> scores = new();
        FakeUnitOfWork uow = new();
        MatchJobHandler handler = new(scores, new Repo<CandidateProfile>(candidate, candidate.Id),
            new Repo<JobPosting>(job, job.Id), new FakeMatching(), uow);

        Result<JobMatchScoreDto> result = await handler.Handle(
            new MatchJobCommand(Guid.NewGuid(), candidate.Id, job.Id, DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(88, result.Value!.MatchScore);
        Assert.Single(scores.Items);
        Assert.Equal(1, uow.Saves);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundForMissingJob()
    {
        CandidateProfile candidate = new(Guid.NewGuid(), "Alex Smith", new EmailAddress("alex@example.com"), "Leader");
        MatchJobHandler handler = new(new Repo<JobMatchScore>(), new Repo<CandidateProfile>(candidate, candidate.Id),
            new Repo<JobPosting>(), new FakeMatching(), new FakeUnitOfWork());

        Result<JobMatchScoreDto> result = await handler.Handle(
            new MatchJobCommand(Guid.NewGuid(), candidate.Id, Guid.NewGuid(), DateTimeOffset.UtcNow), CancellationToken.None);

        Assert.Equal(ErrorKind.NotFound, result.Error);
    }

    private sealed class FakeMatching : IJobMatchingService
    {
        public Task<JobMatchResult> MatchAsync(CandidateProfile candidate, JobPosting job, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobMatchResult(88, ["C#"], ["None"], ["leadership"], []));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class Repo<T> : IRepository<T> where T : class
    {
        private readonly Dictionary<Guid, T> _items = [];

        public Repo() { }

        public Repo(T item, Guid id) => _items[id] = item;

        public List<T> Items { get; } = [];

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));

        public Task<IReadOnlyList<T>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Items);

        public void Add(T entity) => Items.Add(entity);

        public void Remove(T entity) => Items.Remove(entity);
    }
}
