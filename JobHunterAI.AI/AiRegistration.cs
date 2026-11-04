using JobHunterAI.Application.AI;
using JobHunterAI.Application.Generation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace JobHunterAI.AI;

public sealed class SemanticKernelTextCompletion(IChatCompletionService chat) : IAiTextCompletion
{
    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        ChatHistory history = new(systemPrompt);
        history.AddUserMessage(userPrompt);
        OpenAIPromptExecutionSettings settings = new() { ResponseFormat = "json_object", Temperature = 0.2 };
        ChatMessageContent reply = await chat.GetChatMessageContentAsync(history, settings, cancellationToken: cancellationToken);
        return reply.Content ?? string.Empty;
    }
}

internal sealed class UnconfiguredTextCompletion : IAiTextCompletion
{
    public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
        Task.FromException<string>(new AiNotConfiguredException("AI is not configured. Set AI:Endpoint, AI:ApiKey and AI:DeploymentName."));
}

public static class DependencyInjection
{
    public static IServiceCollection AddJobHunterAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        AiOptions options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        services.Configure<AiOptions>(o => configuration.GetSection(AiOptions.SectionName).Bind(o));
        if (options.IsConfigured)
        {
            services.AddAzureOpenAIChatCompletion(options.DeploymentName!, options.Endpoint!, options.ApiKey!);
            services.TryAddSingleton<IAiTextCompletion, SemanticKernelTextCompletion>();
            services.TryAddTransient<IResumeDraftingService, AiResumeDraftingService>();
            services.TryAddTransient<ICoverLetterDraftingService, AiCoverLetterDraftingService>();
        }
        else
        {
            services.TryAddSingleton<IAiTextCompletion, UnconfiguredTextCompletion>();
            services.TryAddTransient<IResumeDraftingService, UnavailableResumeDraftingService>();
            services.TryAddTransient<ICoverLetterDraftingService, UnavailableCoverLetterDraftingService>();
        }

        services.TryAddSingleton<IResumeDocumentRenderer, ResumeDocumentRenderer>();
        services.TryAddSingleton<StructuredAiClient>();
        services.TryAddTransient<IJobMatchingService, JobMatchingService>();
        services.TryAddTransient<IResumeTailoringService, ResumeTailoringService>();
        services.TryAddTransient<ICoverLetterService, CoverLetterService>();
        services.TryAddTransient<IApplicationQuestionService, ApplicationQuestionService>();
        return services;
    }
}
