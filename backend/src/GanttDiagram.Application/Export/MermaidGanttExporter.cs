using System.Text;
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Export;

/// <summary>
/// Mermaid Gantt. В Mermaid дата окончания НЕ входит в задачу, поэтому к End прибавляется 1 день.
/// </summary>
public class MermaidGanttExporter : IGanttExporter
{
    public ExportFormat Format => ExportFormat.Mermaid;
    public string FileExtension => "mmd";

    public string Export(GanttExportModel model, ExportOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("gantt\n");
        sb.Append($"    title {Escape(model.Title)}\n");
        sb.Append("    dateFormat YYYY-MM-DD\n");
        sb.Append("    axisFormat %d.%m\n");
        if (options.ExcludeWeekends)
            sb.Append("    excludes weekends\n");
        if (model.ClosedDays.Count > 0)
            sb.Append($"    excludes {string.Join(", ", model.ClosedDays.Select(ExportText.Date))}\n");
        if (!options.MarkToday)
            sb.Append("    todayMarker off\n");

        var tasks = model.Sections.SelectMany(s => s.Tasks).ToList();
        foreach (var section in model.Sections.Where(s => s.Tasks.Count > 0))
        {
            sb.Append('\n');
            sb.Append($"    section {Escape(section.Name)}\n");
            foreach (var task in section.Tasks)
            {
                var tags = Tags(task, model.Today);
                var prefix = tags.Count > 0 ? string.Join(", ", tags) + ", " : "";
                sb.Append($"    {Escape(ExportText.Label(task, options.Label))} :{prefix}{task.Id}, {ExportText.Date(task.Start)}, {ExportText.Date(task.End.AddDays(1))}\n");
            }
        }

        if (tasks.Count == 0)
            sb.Append("\n%% Нет задач с датами за выбранный период\n");
        if (model.Unscheduled.Count > 0)
            sb.Append($"\n%% Не запланировано: {string.Join(", ", model.Unscheduled.Select(u => u.Key))}\n");

        return sb.ToString();
    }

    private static List<string> Tags(ExportTask task, DateOnly today)
    {
        var tags = new List<string>();
        var done = task.StatusCategory == StatusCategories.Done;
        if (!done && task.End < today) tags.Add("crit");
        if (done) tags.Add("done");
        else if (task.StatusCategory == StatusCategories.InProgress) tags.Add("active");
        return tags;
    }

    /// <summary><c>:</c>, <c>#</c> и <c>;</c> ломают синтаксис Mermaid.</summary>
    internal static string Escape(string text) =>
        ExportText.SingleLine(text).Replace(":", " -").Replace("#", "№").Replace(";", ",");
}
