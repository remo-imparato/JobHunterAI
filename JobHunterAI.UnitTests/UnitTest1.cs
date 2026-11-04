using FluentValidation;
using JobHunterAI.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Text.Json;

namespace JobHunterAI.UnitTests;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_DoesNotExposeExceptionDetails()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        GlobalExceptionHandler handler = new(NullLogger<GlobalExceptionHandler>.Instance);

        bool handled = await handler.TryHandleAsync(context, new InvalidOperationException("private error"), CancellationToken.None);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("private error", body.RootElement.ToString());
    }

    [Theory]
    [InlineData(true, StatusCodes.Status400BadRequest)]
    [InlineData(false, StatusCodes.Status409Conflict)]
    public async Task TryHandleAsync_ReturnsSanitizedClientError(bool isValidation, int expectedStatus)
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        GlobalExceptionHandler handler = new(NullLogger<GlobalExceptionHandler>.Instance);
        Exception exception = isValidation
            ? new ValidationException("private error")
            : new DbUpdateConcurrencyException("private error");

        bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.True(handled);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.DoesNotContain("private error", body.RootElement.ToString());
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsConflictForDuplicateDatabaseKey()
    {
        DefaultHttpContext context = new();
        context.Response.Body = new MemoryStream();
        GlobalExceptionHandler handler = new(NullLogger<GlobalExceptionHandler>.Instance);
        DbUpdateException exception = new("private error", new PostgresException(
            "duplicate", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

        bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        context.Response.Body.Position = 0;
        using JsonDocument body = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        Assert.DoesNotContain("private error", body.RootElement.ToString());
    }
}
