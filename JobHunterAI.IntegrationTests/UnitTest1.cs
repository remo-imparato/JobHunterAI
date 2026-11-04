using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using JobHunterAI.Api;
using System.Net;

namespace JobHunterAI.IntegrationTests;

public sealed class ApiFoundationTests
{
    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
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
}
