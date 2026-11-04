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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

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
builder.Services.AddHttpClient<IJobProvider, GreenhouseProvider>(client =>
    client.BaseAddress = new Uri("https://job-boards.greenhouse.io/v1"));
builder.Services.AddHttpClient<IJobProvider, LeverProvider>(client =>
    client.BaseAddress = new Uri("https://api.lever.co/"));
builder.Services.AddHttpClient<IJobProvider, AshbyProvider>(client =>
    client.BaseAddress = new Uri("https://api.ashbyhq.com/posting-api/"));
builder.Services.AddOptions<JobHunterOptions>()
    .Bind(builder.Configuration.GetSection(JobHunterOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.DisplayName), "JobHunterAI:DisplayName is required.")
    .ValidateOnStart();
builder.Services.AddInfrastructure(builder.Configuration.GetConnectionString("JobHunterAI")
    ?? throw new InvalidOperationException("ConnectionStrings:JobHunterAI must be configured."));
builder.Services.AddBackgroundProcessing(builder.Configuration);

WebApplication app = builder.Build();

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

app.Run();

public partial class Program;
