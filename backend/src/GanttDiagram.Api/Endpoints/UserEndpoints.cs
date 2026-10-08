using GanttDiagram.Application.Services;

namespace GanttDiagram.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users").WithTags("Users");

        g.MapGet("/", (Guid? connectionId, string? search, UserService s, CancellationToken ct) =>
            s.ListAsync(connectionId, search, ct));

        g.MapGet("/{id:guid}", (Guid id, UserService s, CancellationToken ct) => s.GetAsync(id, ct));

        g.MapGet("/{id:guid}/workload", (Guid id, DateOnly? from, DateOnly? to, WorkloadService s, CancellationToken ct) =>
            s.GetAsync(id, from, to, ct));
    }
}
