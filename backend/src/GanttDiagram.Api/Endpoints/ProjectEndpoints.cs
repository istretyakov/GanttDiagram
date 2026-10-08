using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GanttDiagram.Api.Endpoints;

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/projects").WithTags("Projects");

        g.MapGet("/", (ProjectService s, CancellationToken ct) => s.ListAsync(ct));

        g.MapPost("/", async Task<Created<ProjectSummaryDto>> (ProjectInput input, ProjectService s, CancellationToken ct) =>
        {
            var dto = await s.CreateAsync(input, ct);
            return TypedResults.Created($"/api/projects/{dto.Id}", dto);
        });

        g.MapGet("/{id:guid}", (Guid id, ProjectService s, CancellationToken ct) => s.GetAsync(id, ct));

        g.MapPut("/{id:guid}", async Task<NoContent> (Guid id, ProjectInput input, ProjectService s, CancellationToken ct) =>
        {
            await s.UpdateAsync(id, input, ct);
            return TypedResults.NoContent();
        });

        g.MapDelete("/{id:guid}", async Task<NoContent> (Guid id, ProjectService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/epics", (Guid id, AttachEpicsRequest request, ProjectService s, CancellationToken ct) =>
            s.AttachEpicsAsync(id, request, ct));

        g.MapPost("/{id:guid}/issues", (Guid id, AttachIssuesRequest request, ProjectService s, CancellationToken ct) =>
            s.AttachIssuesAsync(id, request, ct));

        g.MapDelete("/{id:guid}/issues/{issueId:guid}", async Task<NoContent> (Guid id, Guid issueId, ProjectService s, CancellationToken ct) =>
        {
            await s.DetachIssueAsync(id, issueId, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/members", (Guid id, AddMemberRequest request, ProjectService s, CancellationToken ct) =>
            s.AddMemberAsync(id, request, ct));

        g.MapDelete("/{id:guid}/members/{userId:guid}", async Task<NoContent> (Guid id, Guid userId, ProjectService s, CancellationToken ct) =>
        {
            await s.RemoveMemberAsync(id, userId, ct);
            return TypedResults.NoContent();
        });

        g.MapPut("/{id:guid}/members/order", async Task<NoContent> (Guid id, MembersOrderRequest request, ProjectService s, CancellationToken ct) =>
        {
            await s.ReorderMembersAsync(id, request, ct);
            return TypedResults.NoContent();
        });

        g.MapPost("/{id:guid}/sync", (Guid id, ImportService s, CancellationToken ct) => s.SyncProjectAsync(id, ct));

        g.MapPost("/{id:guid}/copy", async Task<Created<ProjectSummaryDto>> (Guid id, CopyProjectRequest request, ProjectService s, CancellationToken ct) =>
        {
            var dto = await s.CopyAsync(id, request, ct);
            return TypedResults.Created($"/api/projects/{dto.Id}", dto);
        });

        // Отправка плановых сроков в Jira (этап 2): предпросмотр и запись.
        g.MapGet("/{id:guid}/jira-push", (Guid id, bool? includeAssignee, JiraPushService s, CancellationToken ct) =>
            s.PreviewAsync(id, includeAssignee ?? false, ct));

        g.MapPost("/{id:guid}/jira-push", (Guid id, JiraPushRequest request, JiraPushService s, CancellationToken ct) =>
            s.PushAsync(id, request, ct));
    }
}
