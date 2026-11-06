using System.Net;
using System.Net.Http.Json;
using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Persistence;
using JobHunterAI.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace JobHunterAI.IntegrationTests;

public sealed class CandidateApiTests
{
    private static readonly CandidateInput Input = new("Sample Candidate", " REMO ", "candidate@example.invalid", "Engineer");

    [Fact]
    public async Task Crud_SearchAndPagination_WorkThroughValidationAndHandlers()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/candidates", Input);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        CandidateDto first = (await created.Content.ReadFromJsonAsync<CandidateDto>())!;
        Assert.Equal("remo", first.Username);
        Assert.Equal($"/api/candidates/{first.Id}", created.Headers.Location!.OriginalString);
        using var secondResponse = await client.PostAsJsonAsync("/api/candidates", Input with { Username = "second" });
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        CandidateDto second = (await secondResponse.Content.ReadFromJsonAsync<CandidateDto>())!;
        CandidatePage page = (await client.GetFromJsonAsync<CandidatePage>("/api/candidates?search=SAMPLE&page=1&pageSize=1"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.Equal(new SearchCandidatesQuery("SAMPLE", 1, 1), factory.Store.LastSearch);
        CandidatePage username = (await client.GetFromJsonAsync<CandidatePage>("/api/candidates?search=REMO"))!;
        Assert.Equal(first.Id, Assert.Single(username.Items).Id);
        Assert.Empty((await client.GetFromJsonAsync<CandidatePage>("/api/candidates?search=missing"))!.Items);
        using var updated = await client.PutAsJsonAsync($"/api/candidates/{first.Id}", Input with { Headline = "Architect" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Architect", (await client.GetFromJsonAsync<CandidateDto>($"/api/candidates/{first.Id}"))!.Headline);
        using var deleted = await client.DeleteAsync($"/api/candidates/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(string.Empty, await deleted.Content.ReadAsStringAsync());
        using var missing = await client.GetAsync($"/api/candidates/{first.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("page=-1")]
    [InlineData("page=100001")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task InvalidSearch_IsRejectedBeforeLookup(string query)
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/candidates?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Store.LastSearch);
    }

    [Fact]
    public async Task InvalidProfilesAndEmptyIds_AreRejected()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        foreach (CandidateInput input in new[]
        {
            Input with { FullName = " " }, Input with { Username = "bad/name" },
            Input with { Email = "bad" }, Input with { Headline = new string('x', 301) }
        })
        {
            using var invalid = await client.PostAsJsonAsync("/api/candidates", input);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        foreach (HttpMethod method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Delete })
        {
            using HttpRequestMessage request = new(method, $"/api/candidates/{Guid.Empty}");
            if (method == HttpMethod.Put) request.Content = JsonContent.Create(Input);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Empty(factory.Store.Items);
    }

    [Fact]
    public async Task DuplicateUsernameAndDependentDeletion_ReturnConflict()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        CandidateProfile profile = new(Guid.NewGuid(), Input.FullName, new EmailAddress(Input.Email), Input.Headline, "remo");
        factory.Store.Items.Add(profile);
        using var duplicate = await client.PostAsJsonAsync("/api/candidates", Input with { Username = "ReMo" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        factory.Store.HasDependents = true;
        using var dependent = await client.DeleteAsync($"/api/candidates/{profile.Id}");
        Assert.Equal(HttpStatusCode.Conflict, dependent.StatusCode);
        Assert.Contains("cannot be deleted", await dependent.Content.ReadAsStringAsync());
        Assert.Single(factory.Store.Items);
    }

    [Fact]
    public async Task MissingUpdateAndDelete_ReturnNotFound()
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        using var update = await client.PutAsJsonAsync($"/api/candidates/{Guid.NewGuid()}", Input);
        using var delete = await client.DeleteAsync($"/api/candidates/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation, "IX_candidate_profiles_Username", "username is already in use")]
    [InlineData(PostgresErrorCodes.ForeignKeyViolation, "FK_resumes_candidate_profiles_CandidateProfileId", "cannot be deleted")]
    public async Task DatabaseRaces_ReturnSafeConflict(string sqlState, string constraint, string message)
    {
        using Factory factory = new();
        using HttpClient client = factory.CreateClient();
        factory.Store.SaveError = new DbUpdateException("private database details",
            new PostgresException("private database details", "ERROR", "ERROR", sqlState, constraintName: constraint));
        using var response = await client.PostAsJsonAsync("/api/candidates", Input);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(message, body);
        Assert.DoesNotContain("private database details", body);
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public Store Store { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Background:Enabled", "false");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRepository<CandidateProfile>>();
                services.RemoveAll<ICandidateLookup>();
                services.RemoveAll<IUnitOfWork>();
                services.AddSingleton<IRepository<CandidateProfile>>(Store);
                services.AddSingleton<ICandidateLookup>(Store);
                services.AddSingleton<IUnitOfWork>(Store);
            });
        }
    }

    private sealed class Store : IRepository<CandidateProfile>, ICandidateLookup, IUnitOfWork
    {
        public List<CandidateProfile> Items { get; } = [];
        public bool HasDependents { get; set; }
        public Exception? SaveError { get; set; }
        public SearchCandidatesQuery? LastSearch { get; private set; }
        public Task<CandidateProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(x => x.Id == id));
        public Task<IReadOnlyList<CandidateProfile>> ListAsync(int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CandidateProfile>>(Items.Skip(skip).Take(take).ToArray());
        public void Add(CandidateProfile entity) => Items.Add(entity);
        public void Remove(CandidateProfile entity) => Items.Remove(entity);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            SaveError is null ? Task.FromResult(1) : Task.FromException<int>(SaveError);
        public Task<bool> UsernameExistsAsync(string username, Guid exceptId, CancellationToken token) =>
            Task.FromResult(Items.Any(x => x.Username == username && x.Id != exceptId));
        public Task<bool> HasDependentsAsync(Guid candidateId, CancellationToken token) => Task.FromResult(HasDependents);
        public Task<CandidatePage> SearchAsync(SearchCandidatesQuery query, CancellationToken token)
        {
            LastSearch = query;
            string search = query.Search?.Trim() ?? string.Empty;
            CandidateDto[] matches = Items.Where(x => x.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    x.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.FullName).ThenBy(x => x.Username).ThenBy(x => x.Id).Select(CandidateDto.From).ToArray();
            return Task.FromResult(new CandidatePage(matches.Skip(query.Page * query.PageSize).Take(query.PageSize).ToArray(), matches.Length));
        }
    }
}
