using System.Diagnostics;
using JobHunterAI.Application.Common;
using Microsoft.AspNetCore.Routing;

namespace JobHunterAI.Observability;

public sealed class RequestTelemetryMiddleware(RequestDelegate next, ILogger<RequestTelemetryMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, IHostEnvironment environment)
    {
        string? supplied = context.Request.Headers[HeaderName].Count == 1 ? context.Request.Headers[HeaderName][0] : null;
        string correlationId = IsValidCorrelationId(supplied) ? supplied! : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("jobhunter.correlation_id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString(),
            ["SpanId"] = Activity.Current?.SpanId.ToString()
        });
        long started = Stopwatch.GetTimestamp();
        bool failed = false;
        try { await next(context); }
        catch { failed = true; throw; }
        finally
        {
            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            string route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
            int status = failed ? 500 : context.Response.StatusCode;
            string method = context.Request.Method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS"
                ? context.Request.Method : "OTHER";
            JobHunterDiagnostics.RecordRequest(environment.ApplicationName, method, route, status, elapsed);
            logger.LogInformation("HTTP {RequestMethod} {Route} completed with {StatusCode} in {ElapsedMs} ms",
                method, route, status, elapsed.TotalMilliseconds);
        }
    }

    public static bool IsValidCorrelationId(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
        foreach (char c in value)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')) return false;
        }
        return true;
    }
}
