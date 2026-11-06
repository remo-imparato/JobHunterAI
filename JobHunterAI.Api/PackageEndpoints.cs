using JobHunterAI.Application.Common;
using JobHunterAI.Application.Generation;
using JobHunterAI.Domain;
using MediatR;

namespace JobHunterAI.Api;

public static class PackageEndpoints
{
    public sealed record GenerateInput(Guid JobId, Guid ResumeId);
    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidates/{candidateId:guid}/packages").WithTags("Application packages");
        group.MapPost("/", async (Guid candidateId, GenerateInput input, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GeneratePackageCommand(candidateId, input.JobId, input.ResumeId), token)));
        group.MapGet("/selection", async (Guid candidateId, Guid jobId, Guid resumeId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new FindPackageQuery(candidateId, jobId, resumeId), token)));
        group.MapGet("/{packageId:guid}", async (Guid candidateId, Guid packageId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetPackageQuery(candidateId, packageId), token)));
        group.MapGet("/{packageId:guid}/download", async (Guid candidateId, Guid packageId, ISender sender, HttpContext context, CancellationToken token) =>
        {
            var result = await sender.Send(new GetPackageQuery(candidateId, packageId), token);
            if (!result.IsSuccess) return Respond(result);
            if (result.Value!.Status != PackageStatus.Completed) return Results.Problem(statusCode: 409, title: "Both documents must be complete before download.");
            PackageArchive archive = PackageZip.Create(result.Value);
            context.Response.Headers.CacheControl = "no-store";
            return Results.File(archive.Bytes, "application/zip", archive.FileName);
        });
        return endpoints;
    }
    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) :
        Results.Problem(statusCode: result.Error == ErrorKind.NotFound ? 404 : 409, title: result.Message);
}
