using System.Diagnostics;
using JobHunterAI.Application.Common;
using MediatR;

namespace JobHunterAI.Api.Observability;

public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        string operation = typeof(TRequest).Name;
        using Activity? activity = JobHunterDiagnostics.Activities.StartActivity("CQRS " + operation);
        long started = Stopwatch.GetTimestamp();
        string outcome = "success";
        try { return await next(); }
        catch (OperationCanceledException) { outcome = "cancelled"; throw; }
        catch { outcome = "failure"; activity?.SetStatus(ActivityStatusCode.Error); throw; }
        finally
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            JobHunterDiagnostics.RecordOperation("cqrs", operation, outcome, elapsed);
            logger.LogInformation("Application operation {Operation} completed with {Outcome} in {ElapsedMs} ms",
                operation, outcome, elapsed.TotalMilliseconds);
        }
    }
}
