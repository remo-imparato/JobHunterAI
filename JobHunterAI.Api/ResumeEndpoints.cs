using JobHunterAI.Application.Common;
using JobHunterAI.Application.Resumes;
using MediatR;
using Microsoft.AspNetCore.Http.Features;

namespace JobHunterAI.Api;

public static class ResumeEndpoints
{
    public static IEndpointRouteBuilder MapResumeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/candidates/{candidateId:guid}/resumes").WithTags("Resume onboarding");
        group.MapGet("/", async (Guid candidateId, ISender sender, CancellationToken token, int page = 0, int pageSize = 10) =>
            Respond(await sender.Send(new ListCandidateResumesQuery(candidateId, page, pageSize), token)));
        group.MapGet("/{resumeId:guid}", async (Guid candidateId, Guid resumeId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new GetResumePreviewQuery(candidateId, resumeId), token)));
        group.MapPost("/{resumeId:guid}/import", async (Guid candidateId, Guid resumeId, ISender sender, CancellationToken token) =>
            Respond(await sender.Send(new ImportResumeProfileCommand(candidateId, resumeId), token)));
        group.MapPost("/", UploadAsync);
        return endpoints;
    }

    private static async Task<IResult> UploadAsync(Guid candidateId, HttpRequest request, ISender sender, CancellationToken token)
    {
        const long maxRequestBytes = ResumeUploadLimits.MaxFileBytes + 65536;
        if (request.ContentLength > maxRequestBytes) return Results.Problem(statusCode: 413, title: "Resume uploads must not exceed 10 MB.");
        if (!request.HasFormContentType || request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
            return Results.Problem(statusCode: 415, title: "Use a multipart form with one file field named file.");
        IHttpMaxRequestBodySizeFeature? size = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false }) size.MaxRequestBodySize = maxRequestBytes;
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(new FormOptions
            {
                MultipartBodyLengthLimit = ResumeUploadLimits.MaxFileBytes,
                ValueLengthLimit = 1024,
                ValueCountLimit = 4,
                MultipartHeadersLengthLimit = 8192
            }, token);
        }
        catch (InvalidDataException)
        {
            return Results.Problem(statusCode: 400, title: "Invalid multipart upload or file exceeds 10 MB. Upload one PDF or DOCX file.");
        }
        if (form.Files.Count != 1 || form.Files[0].Name != "file")
            return Results.Problem(statusCode: 400, title: "Upload exactly one file using the file field.");
        IFormFile file = form.Files[0];
        if (file.Length == 0) return Results.Problem(statusCode: 400, title: "The resume file is empty.");
        if (file.Length > ResumeUploadLimits.MaxFileBytes) return Results.Problem(statusCode: 413, title: "Resume uploads must not exceed 10 MB.");
        await using Stream content = file.OpenReadStream();
        return Respond(await sender.Send(new UploadResumeCommand(candidateId, file.FileName, content), token));
    }

    private static IResult Respond<T>(Result<T> result) => result.IsSuccess ? Results.Ok(result.Value) :
        Results.Problem(statusCode: result.Error == ErrorKind.NotFound ? 404 : 409, title: result.Message);
}
