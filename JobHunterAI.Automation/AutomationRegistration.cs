using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunterAI.Automation;

public static class AutomationRegistration
{
    public static IServiceCollection AddJobHunterAutomation(
        this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<AutomationOptions>().Configure(o => configuration.GetSection(AutomationOptions.SectionName).Bind(o));
        services.AddScoped<IAutomationAuditLog, AutomationAuditLog>();
        services.AddSingleton<IScreenshotStore, FileScreenshotStore>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<FormDetectionService>();
        services.AddSingleton<QuestionDetectionService>();
        services.AddScoped<ResumeUploadService>();
        services.AddScoped<ApplicationWorkflow>();
        services.AddScoped<ApplicationAgent>();
        return services;
    }
}
