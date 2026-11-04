using JobHunterAI.Application.Dashboard;

namespace JobHunterAI.Api;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard").WithTags("Dashboard");
        group.MapGet("/summary", async (Guid candidateId, IDashboardReader reader, CancellationToken token) =>
        {
            if (candidateId == Guid.Empty)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["candidateId"] = ["A candidate ID is required."] });
            }

            return Results.Ok(await reader.SummarizeAsync(candidateId, token));
        });
        group.MapGet("/{resource}", async (string resource, Guid candidateId, IDashboardReader reader,
            CancellationToken token, int page = 0, int pageSize = 10, string? search = null, string? filter = null) =>
        {
            if (!Enum.TryParse<DashboardResource>(resource, true, out var kind) || !Enum.IsDefined(kind))
            {
                return Results.NotFound();
            }

            DashboardQuery query = new(candidateId, page, pageSize, search, filter);
            try
            {
                query.Validate(kind);
            }
            catch (ArgumentException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["query"] = ["Invalid candidate, pagination, search or filter."] });
            }

            return Results.Ok(await reader.ReadAsync(kind, query, token));
        });
        return endpoints;
    }
}
