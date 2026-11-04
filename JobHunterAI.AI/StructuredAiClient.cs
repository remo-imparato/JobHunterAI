using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.AI;

/// <summary>Calls the model, parses the reply into a typed result, and retries transient and malformed replies.</summary>
public sealed class StructuredAiClient(
    IAiTextCompletion completion, IOptions<AiOptions> options, ILogger<StructuredAiClient> logger)
{
    public async Task<T> RequestAsync<T>(
        string operation, string systemPrompt, string userPrompt, Func<T, T> validate, CancellationToken cancellationToken)
        where T : class
    {
        int attempts = Math.Clamp(options.Value.MaxAttempts, 1, 10);
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogInformation("AI request {Operation} attempt {Attempt}/{Attempts}.", operation, attempt, attempts);
                string reply = await completion.CompleteJsonAsync(systemPrompt, userPrompt, cancellationToken);
                T result = validate(Parse<T>(reply));
                logger.LogInformation("AI request {Operation} succeeded.", operation);
                return result;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or AiNotConfiguredException))
            {
                if (attempt >= attempts)
                {
                    logger.LogError(exception, "AI request {Operation} failed after {Attempts} attempts.", operation, attempts);
                    throw new AiResponseException($"The AI request '{operation}' failed.", exception);
                }

                TimeSpan delay = options.Value.RetryBaseDelay * Math.Pow(2, attempt - 1);
                logger.LogWarning(exception, "AI request {Operation} attempt {Attempt} failed; retrying in {Delay}.",
                    operation, attempt, delay);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }
    }

    private static T Parse<T>(string reply) where T : class
    {
        string json = reply.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            int start = json.IndexOf('\n');
            int end = json.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                json = json[(start + 1)..end].Trim();
            }
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, AiJson.Options)
                ?? throw new AiResponseException("The AI reply was empty.");
        }
        catch (JsonException exception)
        {
            throw new AiResponseException("The AI reply was not valid JSON for the expected shape.", exception);
        }
    }
}
