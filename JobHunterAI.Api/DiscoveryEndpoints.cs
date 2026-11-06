using JobHunterAI.Application.Common;
using JobHunterAI.Application.Jobs;
using MediatR;
using Hangfire;
using JobHunterAI.Domain;
using JobHunterAI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace JobHunterAI.Api;

public static class DiscoveryEndpoints
{
    public sealed record StartRequest(Guid RunId);
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidates/{candidateId:guid}/discovery").WithTags("Candidate discovery");
        group.MapPost("/", async (Guid candidateId, StartRequest input, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new StartDiscoveryCommand(candidateId, input.RunId), token)));
        group.MapPost("/background", async Task<IResult> (Guid candidateId, StartRequest input, IServiceProvider services,
            JobHunterDbContext db, CancellationToken token) =>
        {
            if (candidateId == Guid.Empty || input.RunId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Candidate and run IDs are required.");
            IBackgroundJobClient? queue = services.GetService<IBackgroundJobClient>();
            if (queue is null) return Results.Problem(statusCode: 409, title: "Enable Background:Enabled to queue searches.");
            if (!await db.CandidateProfiles.AnyAsync(x => x.Id == candidateId, token))
                return Results.Problem(statusCode: 404, title: "Candidate not found.");
            if (!await db.Set<CandidateSearchCriteria>().AnyAsync(x => x.Id == candidateId, token))
                return Results.Problem(statusCode: 409, title: "Save search criteria before starting discovery.");
            string id = queue.Enqueue<ScheduledCandidateSearch>(job => job.RunAsync(candidateId, input.RunId, CancellationToken.None));
            return Results.Accepted($"/api/candidates/{candidateId}/discovery/{input.RunId}", new QueuedDiscovery(input.RunId, id));
        });
        group.MapGet("/", async (Guid candidateId, ISender sender, CancellationToken token, int page = 0, int pageSize = 10) =>
            Respond(await sender.Send(new ListDiscoveryQuery(candidateId, page, pageSize), token)));
        group.MapGet("/{runId:guid}", async (Guid candidateId, Guid runId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetDiscoveryQuery(candidateId, runId), token)));
        return endpoints;
    }
    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) :
        Results.Problem(statusCode: result.Error == ErrorKind.NotFound ? 404 : 409, title: result.Message);
}
