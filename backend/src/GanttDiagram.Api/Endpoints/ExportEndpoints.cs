using GanttDiagram.Application;
using GanttDiagram.Application.Export;

namespace GanttDiagram.Api.Endpoints;

public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Export");

        // format=plantuml|mermaid, groupBy=member|epic, label=key|keySummary|keySummaryAssignee
        g.MapGet("/projects/{id:guid}/export", (
                Guid id, string format, string? groupBy, DateOnly? from, DateOnly? to,
                string? label, bool? excludeWeekends, bool? markToday,
                ExportService s, CancellationToken ct) =>
            s.ExportProjectAsync(id, Parse<ExportFormat>(format, "format"), Parse<ExportGroupBy>(groupBy, "groupBy", ExportGroupBy.Member),
                from, to, Options(label, excludeWeekends, markToday), ct));

        g.MapGet("/users/{id:guid}/workload/export", (
                Guid id, string format, DateOnly? from, DateOnly? to,
                string? label, bool? excludeWeekends, bool? markToday,
                ExportService s, CancellationToken ct) =>
            s.ExportWorkloadAsync(id, Parse<ExportFormat>(format, "format"), from, to,
                Options(label, excludeWeekends, markToday), ct));
    }

    private static ExportOptions Options(string? label, bool? excludeWeekends, bool? markToday) =>
        new(Parse<ExportLabel>(label, "label", ExportLabel.KeySummary), excludeWeekends ?? true, markToday ?? true);

    private static T Parse<T>(string? value, string name, T? fallback = null) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value) && fallback is not null) return fallback.Value;
        if (Enum.TryParse<T>(value, ignoreCase: true, out var result) && Enum.IsDefined(result)) return result;
        var allowed = string.Join(", ", Enum.GetNames<T>().Select(n => char.ToLowerInvariant(n[0]) + n[1..]));
        throw new ValidationFailedException($"Недопустимое значение параметра {name}: «{value}».",
            new Dictionary<string, string[]> { [name] = [$"Допустимо: {allowed}."] });
    }
}
