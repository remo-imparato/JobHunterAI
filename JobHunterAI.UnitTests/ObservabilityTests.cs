using System.Diagnostics;
using JobHunterAI.AI;
using JobHunterAI.Automation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobHunterAI.UnitTests;

public sealed class ObservabilityTests
{
    [Fact]
    public async Task AiLogs_ExcludePromptsRepliesAndExceptionDetails()
    {
        CaptureLogger<StructuredAiClient> logger = new();
        StructuredAiClient client = new(new FailingCompletion(), Options.Create(new AiOptions { MaxAttempts = 1 }), logger);
        await Assert.ThrowsAsync<AiResponseException>(() => client.RequestAsync<object>("JobMatching", "secret-system", "secret-candidate",
            value => value, CancellationToken.None));
        string logs = string.Join(" ", logger.Messages);
        Assert.Contains("JobMatching", logs);
        Assert.DoesNotContain("secret", logs);
        Assert.All(logger.Exceptions, exception => Assert.Null(exception));
    }

    [Fact]
    public void Audit_CapturesTraceAndCorrelationWithoutLoggingDetails()
    {
        using Activity activity = new Activity("audit-test").Start();
        activity.SetTag("jobhunter.correlation_id", "review-123");
        CaptureLogger<AutomationAuditLog> logger = new();
        AutomationAuditLog audit = new(logger);
        audit.Record("Approve", "private candidate detail");
        AuditEntry entry = Assert.Single(audit.Entries);
        Assert.Equal(activity.TraceId.ToString(), entry.TraceId);
        Assert.Equal("review-123", entry.CorrelationId);
        Assert.DoesNotContain("private candidate detail", string.Join(" ", logger.Messages));
        Assert.Equal("private candidate detail", entry.Detail);
        Assert.IsNotType<List<AuditEntry>>(audit.Entries);
    }

    private sealed class FailingCompletion : IAiTextCompletion
    {
        public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("secret-response-and-credentials");
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
