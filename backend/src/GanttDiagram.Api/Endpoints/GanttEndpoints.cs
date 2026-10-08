using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Services;

namespace GanttDiagram.Api.Endpoints;

public static class GanttEndpoints
{
    public static void MapGanttEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Gantt");

        g.MapGet("/projects/{id:guid}/gantt", (Guid id, GanttService s, CancellationToken ct) => s.GetAsync(id, ct));

        g.MapPut("/projects/{id:guid}/gantt", (Guid id, GanttSaveRequest request, GanttService s, CancellationToken ct) =>
            s.SaveAsync(id, request, ct));

        g.MapPost("/issues/{id:guid}/reset-to-jira", (Guid id, GanttService s, CancellationToken ct) =>
            s.ResetToJiraAsync(id, ct));
    }
}
