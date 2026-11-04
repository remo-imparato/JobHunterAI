using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using JobHunterAI.Application.Common;
using JobHunterAI.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace JobHunterAI.IntegrationTests;

public sealed class ObservabilityTests
{
    [Theory]
    [InlineData("request-123_test", true)]
    [InlineData("contains spaces", false)]
    [InlineData("", false)]
    [InlineData("unsafe\r\nheader", false)]
    public void Correlation_ValidatesSafeBoundedCharacters(string value, bool expected)
    {
        Assert.Equal(expected, RequestTelemetryMiddleware.IsValidCorrelationId(value));
        Assert.False(RequestTelemetryMiddleware.IsValidCorrelationId(new string('x', 65)));
    }

    [Theory]
    [InlineData("client-123", true)]
    [InlineData("invalid value", false)]
    public async Task Http_ReturnsValidatedCorrelationHeader(string supplied, bool retained)
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation(RequestTelemetryMiddleware.HeaderName, supplied);
        using var response = await client.SendAsync(request);
        string correlation = Assert.Single(response.Headers.GetValues(RequestTelemetryMiddleware.HeaderName));
        Assert.True(RequestTelemetryMiddleware.IsValidCorrelationId(correlation));
        Assert.Equal(retained, correlation == supplied);
    }

    [Fact]
    public async Task Health_LivenessRemainsHealthyWhenReadinessFails()
    {
        using var factory = new Factory();
        using var client = factory.CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health"));
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live"));
        using var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        Assert.Equal("Unhealthy", content);
        Assert.DoesNotContain("private dependency detail", content);
    }

    [Fact]
    public void Metrics_EmitCountsAndDurationWithoutSensitiveTags()
    {
        ConcurrentBag<(string Name, double Value, string Tags)> values = [];
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == JobHunterDiagnostics.Name) meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            values.Add((instrument.Name, value, Format(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
            values.Add((instrument.Name, value, Format(tags))));
        listener.Start();
        JobHunterDiagnostics.RecordOperation("ai", "JobMatching", "success", TimeSpan.FromSeconds(2));
        Assert.Contains(values, v => v.Name == "jobhunter.operations" && v.Value == 1 && v.Tags.Contains("JobMatching"));
        Assert.Contains(values, v => v.Name == "jobhunter.operation.duration" && v.Value == 2);
    }

    private static string Format(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string result = string.Empty;
        foreach (var tag in tags) result += tag.Key + "=" + tag.Value + ";";
        return result;
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:JobHunterAI", "Host=localhost;Database=tests;Username=tests");
            builder.UseSetting("Background:Enabled", "false");
            builder.ConfigureServices(services => services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("test-dependency",
                    _ => new FailedCheck(), HealthStatus.Unhealthy, ["ready"]));
            }));
        }
    }

    private sealed class FailedCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy("private dependency detail"));
    }
}
