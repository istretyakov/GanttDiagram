namespace GanttDiagram.Application.Export;

/// <summary>Общая модель экспорта, не зависит от формата. Даты окончания включительные.</summary>
public record GanttExportModel(
    string Title,
    DateOnly? From,
    DateOnly? To,
    DateOnly Today,
    IReadOnlyList<DateOnly> ClosedDays,
    IReadOnlyList<ExportSection> Sections,
    IReadOnlyList<ExportUnscheduled> Unscheduled);

public record ExportSection(string Name, IReadOnlyList<ExportTask> Tasks);

public record ExportTask(
    string Id,
    string Key,
    string Summary,
    DateOnly Start,
    DateOnly End,
    string Status,
    string StatusCategory,
    string? EpicKey,
    string? AssigneeName,
    string JiraUrl);

public record ExportUnscheduled(string Key, string Summary);

public enum ExportFormat
{
    PlantUml,
    Mermaid,
}

public enum ExportGroupBy
{
    Member,
    Epic,
    Project,
}

public enum ExportLabel
{
    Key,
    KeySummary,
    KeySummaryAssignee,
}

public record ExportOptions(
    ExportLabel Label = ExportLabel.KeySummary,
    bool ExcludeWeekends = true,
    bool MarkToday = true);

public interface IGanttExporter
{
    ExportFormat Format { get; }
    string FileExtension { get; }
    string Export(GanttExportModel model, ExportOptions options);
}

internal static class ExportText
{
    public static string Label(ExportTask task, ExportLabel label) => label switch
    {
        ExportLabel.Key => task.Key,
        ExportLabel.KeySummaryAssignee when task.AssigneeName is not null => $"{task.Key} · {task.Summary} ({task.AssigneeName})",
        ExportLabel.Key or ExportLabel.KeySummary or ExportLabel.KeySummaryAssignee => $"{task.Key} · {task.Summary}",
        _ => task.Key,
    };

    public static string SingleLine(string text) =>
        text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd");
}
