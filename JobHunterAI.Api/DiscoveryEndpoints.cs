using JobHunterAI.Application.Common;
using JobHunterAI.Application.Jobs;
using MediatR;

namespace JobHunterAI.Api;

public static class DiscoveryEndpoints
{
    public sealed record StartRequest(Guid RunId);
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidates/{candidateId:guid}/discovery").WithTags("Candidate discovery");
        group.MapPost("/", async (Guid candidateId, StartRequest input, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new StartDiscoveryCommand(candidateId, input.RunId), token)));
        group.MapGet("/", async (Guid candidateId, ISender sender, CancellationToken token, int page = 0, int pageSize = 10) =>
            Respond(await sender.Send(new ListDiscoveryQuery(candidateId, page, pageSize), token)));
        group.MapGet("/{runId:guid}", async (Guid candidateId, Guid runId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetDiscoveryQuery(candidateId, runId), token)));
        return endpoints;
    }
    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) :
        Results.Problem(statusCode: result.Error == ErrorKind.NotFound ? 404 : 409, title: result.Message);
}
