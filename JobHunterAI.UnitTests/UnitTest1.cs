using JobHunterAI.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
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
}
