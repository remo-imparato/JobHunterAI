using FluentValidation;
using JobHunterAI.Application.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace JobHunterAI.Api;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status = exception switch
        {
            ValidationException => StatusCodes.Status400BadRequest,
            ResumeFileTooLargeException => StatusCodes.Status413PayloadTooLarge,
            UnsupportedResumeFormatException => StatusCodes.Status400BadRequest,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                StatusCodes.Status409Conflict,
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }
                when httpContext.Request.Path.StartsWithSegments("/api/candidates") => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError("Unhandled request failure ({ErrorType}) for endpoint {Endpoint}",
                exception.GetType().Name, (httpContext.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "unmatched");
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = status switch
            {
                StatusCodes.Status413PayloadTooLarge => exception.Message,
                StatusCodes.Status400BadRequest when exception is UnsupportedResumeFormatException => exception.Message,
                StatusCodes.Status400BadRequest => "Request validation failed.",
                StatusCodes.Status409Conflict when exception is DbUpdateException
                { InnerException: PostgresException { ConstraintName: "IX_candidate_profiles_Username" } } => "That username is already in use.",
                StatusCodes.Status409Conflict when exception is DbUpdateException
                { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } => "This candidate has related records and cannot be deleted.",
                StatusCodes.Status409Conflict => "The resource could not be saved due to a conflict. Reload and try again.",
                _ => "An unexpected error occurred."
            },
            Extensions = { ["correlationId"] = httpContext.TraceIdentifier,
                ["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier }
        }, cancellationToken);

        return true;
    }
}
