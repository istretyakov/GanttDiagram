using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GanttDiagram.Api.Endpoints;

public static class ConnectionEndpoints
{
    public static void MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/connections").WithTags("Connections");

        g.MapGet("/", (ConnectionService s, CancellationToken ct) => s.ListAsync(ct));

        g.MapGet("/{id:guid}", (Guid id, ConnectionService s, CancellationToken ct) => s.GetAsync(id, ct));

        // Подключение по адресу и токену до сохранения: пользователь, версия, проекты и поля Jira.
        g.MapPost("/discover", (JiraDiscoverRequest request, ConnectionService s, CancellationToken ct) =>
            s.DiscoverAsync(request, ct));

        g.MapPost("/", async Task<Created<ConnectionDto>> (ConnectionInput input, ConnectionService s, CancellationToken ct) =>
        {
            var dto = await s.CreateAsync(input, ct);
            return TypedResults.Created($"/api/connections/{dto.Id}", dto);
        });

        g.MapPut("/{id:guid}", (Guid id, ConnectionInput input, ConnectionService s, CancellationToken ct) =>
            s.UpdateAsync(id, input, ct));

        g.MapDelete("/{id:guid}", async Task<NoContent> (Guid id, ConnectionService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/test", (Guid id, ConnectionService s, CancellationToken ct) => s.TestAsync(id, ct));

        g.MapGet("/{id:guid}/fields", (Guid id, ConnectionService s, CancellationToken ct) => s.GetFieldsAsync(id, ct));

        g.MapGet("/{id:guid}/epics", (Guid id, string? search, string? project, ConnectionService s, CancellationToken ct) =>
            s.SearchEpicsAsync(id, search, project, ct));

        g.MapPost("/{id:guid}/import", (Guid id, ImportRequest request, ImportService s, CancellationToken ct) =>
            s.ImportAsync(id, request.EpicKeys, request.ProjectId, ct));

        g.MapPost("/{id:guid}/users/sync", (Guid id, ImportService s, CancellationToken ct) => s.SyncUsersAsync(id, ct));
    }
}
