using FluentValidation;
using JobHunterAI.AI;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using JobHunterAI.Api;
using JobHunterAI.Domain;
using MediatR;
using System.Net;

namespace JobHunterAI.IntegrationTests;

public sealed class ApiFoundationTests
{
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Database:SeedDevelopmentData", "false");
            builder.UseSetting("Background:Enabled", "false");
        }
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        using WebApplicationFactory<Program> factory = new ApiFactory();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_IsOnlyAvailableInDevelopment()
    {
        using WebApplicationFactory<Program> factory = new ApiFactory();
        using WebApplicationFactory<Program> development = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using WebApplicationFactory<Program> production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

        using HttpClient developmentClient = development.CreateClient();
        using HttpClient productionClient = production.CreateClient();
        HttpResponseMessage developmentResponse = await developmentClient.GetAsync("/openapi/v1.json");
        HttpResponseMessage productionResponse = await productionClient.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, developmentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, productionResponse.StatusCode);
    }

    [Fact]
    public void MissingDisplayName_FailsOptionsValidation()
    {
        using WebApplicationFactory<Program> factory = new ApiFactory();
        using WebApplicationFactory<Program> invalid = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("JobHunterAI:DisplayName", " "));

        Assert.Throws<OptionsValidationException>(() =>
            invalid.Services.GetRequiredService<IOptions<JobHunterOptions>>().Value);
    }

    [Fact]
    public async Task InvalidQuery_IsRejectedByValidationPipelineWithoutDatabaseAccess()
    {
        using WebApplicationFactory<Program> factory = new ApiFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await Assert.ThrowsAsync<ValidationException>(() =>
            sender.Send(new GetJobsQuery(-1, 101)));
    }

    [Fact]
    public async Task UnconfiguredDraftingServices_FailWithoutProducingDocuments()
    {
        using WebApplicationFactory<Program> factory = new ApiFactory();
        using IServiceScope scope = factory.Services.CreateScope();
        IResumeDraftingService resumeService = scope.ServiceProvider.GetRequiredService<IResumeDraftingService>();
        ICoverLetterDraftingService letterService = scope.ServiceProvider.GetRequiredService<ICoverLetterDraftingService>();
        CandidateProfile candidate = new(Guid.NewGuid(), "Sample Candidate", new EmailAddress("sample@example.com"), "Leader");
        JobPosting job = new(Guid.NewGuid(), Guid.NewGuid(), "Greenhouse", "sample-001", "Architect",
            "Lead engineers", new JobLocation(null, null, true), "https://example.com/apply");
        Resume resume = new(Guid.NewGuid(), candidate.Id, "Base resume");
        ResumeVersion version = resume.AddVersion(Guid.NewGuid(), "# Verified experience",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.IsType<UnavailableResumeDraftingService>(resumeService);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            resumeService.DraftAsync(candidate, job, version, CancellationToken.None));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            letterService.DraftAsync(candidate, job, CancellationToken.None));
    }
}
