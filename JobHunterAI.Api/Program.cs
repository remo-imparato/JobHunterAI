using FluentValidation;
using JobHunterAI.AI;
using JobHunterAI.Automation;
using JobHunterAI.Application.Common;
using JobHunterAI.Application.Generation;
using JobHunterAI.Application.Jobs;
using JobHunterAI.Api;
using JobHunterAI.Infrastructure;
using JobHunterAI.Infrastructure.JobProviders;
using MediatR;
using Serilog;
using JobHunterAI.Observability;
using JobHunterAI.Api.Observability;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
    string? directory = context.Configuration["Background:LogDirectory"];
    if (!string.IsNullOrWhiteSpace(directory))
    {
        if (!Path.IsPathFullyQualified(directory) || Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) ==
            Path.GetPathRoot(directory)?.TrimEnd(Path.DirectorySeparatorChar))
            throw new InvalidOperationException("Background:LogDirectory must be an absolute, non-root log directory.");
        Directory.CreateDirectory(directory);
        configuration.WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(), Path.Combine(directory, "jobhunterai-.log"),
            rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 10 * 1024 * 1024, rollOnFileSizeLimit: true,
            retainedFileCountLimit: 30, shared: true);
    }
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("postgresql", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
builder.Services.AddOpenApi();
builder.Services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(GetJobsHandler).Assembly));
builder.Services.AddValidatorsFromAssemblyContaining<GetJobsValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
builder.Services.AddAutoMapper(config =>
{
    string? licenseKey = builder.Configuration["AutoMapper:LicenseKey"];
    if (!string.IsNullOrWhiteSpace(licenseKey))
    {
        config.LicenseKey = licenseKey;
    }
}, typeof(ApplicationMappingProfile));
builder.Services.AddJobHunterAi(builder.Configuration);
builder.Services.AddJobHunterAutomation(builder.Configuration);
builder.Services.AddHttpClient<GreenhouseProvider>(client =>
    client.BaseAddress = new Uri("https://boards-api.greenhouse.io/v1/"));
builder.Services.AddHttpClient<LeverProvider>(client =>
    client.BaseAddress = new Uri("https://api.lever.co/"));
builder.Services.AddHttpClient<AshbyProvider>(client =>
    client.BaseAddress = new Uri("https://api.ashbyhq.com/posting-api/"));
builder.Services.AddTransient<IJobProvider>(services => services.GetRequiredService<GreenhouseProvider>());
builder.Services.AddTransient<IJobProvider>(services => services.GetRequiredService<LeverProvider>());
builder.Services.AddTransient<IJobProvider>(services => services.GetRequiredService<AshbyProvider>());
builder.Services.AddOptions<PublicFeedOptions>().Bind(builder.Configuration.GetSection(PublicFeedOptions.SectionName))
    .Validate(options => options.IsValid(), "Invalid public feed limits.").ValidateOnStart();
builder.Services.AddHttpClient<ArbeitnowFeed>(client =>
{
    client.BaseAddress = new Uri("https://www.arbeitnow.com/api/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient<RemoteOkFeed>(client =>
{
    client.BaseAddress = new Uri("https://remoteok.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddTransient<IPublicJobFeed>(services => services.GetRequiredService<ArbeitnowFeed>());
builder.Services.AddTransient<IPublicJobFeed>(services => services.GetRequiredService<RemoteOkFeed>());
builder.Services.AddOptions<JobHunterOptions>()
    .Bind(builder.Configuration.GetSection(JobHunterOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.DisplayName), "JobHunterAI:DisplayName is required.")
    .ValidateOnStart();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("JobHunterAI")
    ?? throw new InvalidOperationException("ConnectionStrings:JobHunterAI must be configured."));
builder.Services.AddBackgroundProcessing(builder.Configuration);
builder.Services.AddOptions<DiscoveryOptions>().Bind(builder.Configuration.GetSection(DiscoveryOptions.SectionName))
    .Validate(options => options.IsValid(), "Invalid discovery limits.").ValidateOnStart();

WebApplication app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JobHunterDbContext>().Database.MigrateAsync(app.Lifetime.ApplicationStopping);
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:SeedDevelopmentData"))
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    await DevelopmentDataSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<JobHunterDbContext>(), app.Logger, app.Lifetime.ApplicationStopping);
}

app.UseMiddleware<RequestTelemetryMiddleware>();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "JobHunterAI API v1"));
}

app.UseHttpsRedirection();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapDashboardEndpoints();
app.MapCandidateEndpoints();
app.MapResumeEndpoints();
app.MapDiscoveryEndpoints();
app.MapPackageEndpoints();

app.Run();

public partial class Program;
