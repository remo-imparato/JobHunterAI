using JobHunterAI.Application.Candidates;
using JobHunterAI.Application.Common;
using MediatR;

namespace JobHunterAI.Api;

public static class CandidateEndpoints
{
    public static IEndpointRouteBuilder MapCandidateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidates").WithTags("Candidates");
        group.MapGet("/{id:guid}/search-criteria", async (Guid id, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetSearchCriteriaQuery(id), token)));
        group.MapPut("/{id:guid}/search-criteria", async (Guid id, SearchCriteriaInput input, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new SaveSearchCriteriaCommand(id, input), token)));
        group.MapGet("/", async (ISender sender, CancellationToken token,
            string? search = null, int page = 0, int pageSize = 10) =>
            Respond(await sender.Send(new SearchCandidatesQuery(search, page, pageSize), token)));
        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetCandidateQuery(id), token)));
        group.MapPost("/", async (CandidateInput input, ISender sender, CancellationToken token) =>
        {
            Result<CandidateDto> result = await sender.Send(new CreateCandidateCommand(input), token);
            return result.IsSuccess ? Results.Created($"/api/candidates/{result.Value!.Id}", result.Value) : Respond(result);
        });
        group.MapPut("/{id:guid}", async (Guid id, CandidateInput input, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new UpdateCandidateCommand(id, input), token)));
        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken token) =>
        {
            Result<bool> result = await sender.Send(new DeleteCandidateCommand(id), token);
            return result.IsSuccess ? Results.NoContent() : Respond(result);
        });
        return endpoints;
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) :
        Results.Problem(statusCode: result.Error == ErrorKind.NotFound ? 404 : 409, title: result.Message);
}
