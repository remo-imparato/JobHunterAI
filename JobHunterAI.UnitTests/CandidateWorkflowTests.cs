using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Common;
using JobHunterAI.Domain;

namespace JobHunterAI.UnitTests;

public sealed class CandidateWorkflowTests
{
    private static readonly CandidateInput Input = new("Sample Candidate", "  REMO_One  ", "candidate@example.invalid", "Engineer");

    [Theory]
    [InlineData(" Remo.One ", "remo.one")]
    [InlineData("REMO_One-1", "remo_one-1")]
    public void Username_IsCanonical(string input, string expected) =>
        Assert.Equal(expected, CandidateProfile.NormalizeUsername(input));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("two words")]
    [InlineData("a/b")]
    [InlineData("rémo")]
    public void Username_RejectsInvalidCharacters(string input) =>
        Assert.Throws<ArgumentException>(() => CandidateProfile.NormalizeUsername(input));

    [Fact]
    public void LegacyConstructor_UsesDeterministicUsername()
    {
        Guid id = Guid.NewGuid();
        CandidateProfile profile = new(id, Input.FullName, new EmailAddress(Input.Email), Input.Headline);
        Assert.Equal($"candidate-{id:N}", profile.Username);
    }

    [Fact]
    public void InvalidProfileUpdate_DoesNotMutateContact()
    {
        CandidateProfile profile = Profile();
        Assert.Throws<ArgumentException>(() => profile.UpdateProfile("Changed", new EmailAddress(Input.Email), "", "new"));
        Assert.Equal(Input.FullName, profile.FullName);
        Assert.Equal("remo_one", profile.Username);
        Assert.Throws<ArgumentException>(() => profile.UpdateProfile("Changed", new EmailAddress(Input.Email), "Engineer", "bad/name"));
        Assert.Equal(Input.FullName, profile.FullName);
    }

    [Fact]
    public void Validators_RejectInvalidInputsAndBounds()
    {
        CandidateInputValidator validator = new();
        Assert.True(validator.Validate(Input).IsValid);
        foreach (CandidateInput input in new[]
        {
            Input with { FullName = " " }, Input with { FullName = new string('x', 201) },
            Input with { Username = "a/b" }, Input with { Username = new string('a', 65) },
            Input with { Email = "not-email" }, Input with { Email = "Name <a@example.invalid>" },
            Input with { Headline = " " }, Input with { Headline = new string('x', 301) }
        }) Assert.False(validator.Validate(input).IsValid);
        Assert.False(new CreateCandidateValidator().Validate(new CreateCandidateCommand(null!)).IsValid);
        Assert.False(new UpdateCandidateValidator().Validate(new UpdateCandidateCommand(Guid.Empty, Input)).IsValid);
        Assert.False(new DeleteCandidateValidator().Validate(new DeleteCandidateCommand(Guid.Empty)).IsValid);
        Assert.False(new GetCandidateValidator().Validate(new GetCandidateQuery(Guid.Empty)).IsValid);
        foreach (SearchCandidatesQuery query in new[]
        {
            new SearchCandidatesQuery(Page: -1), new SearchCandidatesQuery(Page: 100001),
            new SearchCandidatesQuery(PageSize: 0), new SearchCandidatesQuery(PageSize: 101),
            new SearchCandidatesQuery(new string('x', 201))
        }) Assert.False(new SearchCandidatesValidator().Validate(query).IsValid);
    }

    [Fact]
    public async Task CreateAndUpdate_NormalizeUsernameAndAllowDuplicateFullNames()
    {
        CandidateProfile existing = Profile("other");
        FakeRepository<CandidateProfile> repository = new(x => x.Id, existing);
        Lookup lookup = new(repository);
        FakeUnitOfWork work = new();
        var created = await new CreateCandidateHandler(repository, lookup, work).Handle(new(Input), default);
        Assert.True(created.IsSuccess);
        Assert.Equal("remo_one", created.Value!.Username);
        Assert.Equal(2, repository.Items.Count);
        Assert.Equal(existing.FullName, created.Value.FullName);
        var updated = await new UpdateCandidateHandler(repository, lookup, work).Handle(
            new(created.Value.Id, Input with { Username = " REMO_TWO ", Headline = "Architect" }), default);
        Assert.True(updated.IsSuccess);
        Assert.Equal("remo_two", updated.Value!.Username);
        Assert.Equal("Architect", updated.Value.Headline);
        Assert.Equal(2, work.Saves);
        var fetched = await new GetCandidateHandler(repository).Handle(new(created.Value.Id), default);
        Assert.Equal(updated.Value, fetched.Value);
    }

    [Fact]
    public async Task ConflictingUsernames_DoNotSaveOrMutate()
    {
        CandidateProfile first = Profile();
        CandidateProfile second = Profile("other");
        FakeRepository<CandidateProfile> repository = new(x => x.Id, first, second);
        Lookup lookup = new(repository);
        FakeUnitOfWork work = new();
        var create = await new CreateCandidateHandler(repository, lookup, work).Handle(new(Input), default);
        Assert.Equal(ErrorKind.Conflict, create.Error);
        var update = await new UpdateCandidateHandler(repository, lookup, work).Handle(new(second.Id, Input), default);
        Assert.Equal(ErrorKind.Conflict, update.Error);
        Assert.Equal("other", second.Username);
        Assert.Equal(0, work.Saves);
        Assert.Equal(2, repository.Items.Count);
    }

    [Fact]
    public async Task UpdatingOwnUsername_IsAllowed()
    {
        CandidateProfile profile = Profile();
        FakeRepository<CandidateProfile> repository = new(x => x.Id, profile);
        var result = await new UpdateCandidateHandler(repository, new Lookup(repository), new FakeUnitOfWork())
            .Handle(new(profile.Id, Input), default);
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_PreservesDependentRecords(bool hasDependents)
    {
        CandidateProfile profile = Profile();
        FakeRepository<CandidateProfile> repository = new(x => x.Id, profile);
        FakeUnitOfWork work = new();
        var result = await new DeleteCandidateHandler(repository, new Lookup(repository) { HasDependents = hasDependents }, work)
            .Handle(new(profile.Id), default);
        Assert.Equal(!hasDependents, result.IsSuccess);
        Assert.Equal(hasDependents ? 1 : 0, repository.Items.Count);
        Assert.Equal(hasDependents ? 0 : 1, work.Saves);
        if (hasDependents) Assert.Equal(ErrorKind.Conflict, result.Error);
    }

    [Fact]
    public async Task MissingCandidates_ReturnNotFound()
    {
        FakeRepository<CandidateProfile> repository = new(x => x.Id);
        FakeUnitOfWork work = new();
        Lookup lookup = new(repository);
        Guid id = Guid.NewGuid();
        Assert.Equal(ErrorKind.NotFound, (await new GetCandidateHandler(repository).Handle(new(id), default)).Error);
        Assert.Equal(ErrorKind.NotFound, (await new UpdateCandidateHandler(repository, lookup, work).Handle(new(id, Input), default)).Error);
        Assert.Equal(ErrorKind.NotFound, (await new DeleteCandidateHandler(repository, lookup, work).Handle(new(id), default)).Error);
        Assert.Equal(0, work.Saves);
    }

    private static CandidateProfile Profile(string username = "remo_one") =>
        new(Guid.NewGuid(), Input.FullName, new EmailAddress(Input.Email), Input.Headline, username);

    private sealed class Lookup(FakeRepository<CandidateProfile> repository) : ICandidateLookup
    {
        public bool HasDependents { get; init; }
        public Task<bool> UsernameExistsAsync(string username, Guid exceptId, CancellationToken token) =>
            Task.FromResult(repository.Items.Any(x => x.Id != exceptId && x.Username == username));
        public Task<bool> HasDependentsAsync(Guid candidateId, CancellationToken token) => Task.FromResult(HasDependents);
        public Task<CandidatePage> SearchAsync(SearchCandidatesQuery query, CancellationToken token) =>
            throw new NotSupportedException();
    }
}
