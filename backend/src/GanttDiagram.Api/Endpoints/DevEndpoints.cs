using GanttDiagram.Infrastructure;

namespace GanttDiagram.Api.Endpoints;

/// <summary>Только для Development: демо-данные без настоящей Jira.</summary>
public static class DevEndpoints
{
    public static void MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/dev/seed", async (DemoSeeder seeder, CancellationToken ct) =>
                new SeedResult(await seeder.SeedAsync(ct)))
            .WithTags("Dev");
    }
}

public record SeedResult(Guid ProjectId);
