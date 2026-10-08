using System.Text;
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Export;

/// <summary>PlantUML Gantt. Даты окончания в PlantUML включительные — как в модели.</summary>
public class PlantUmlGanttExporter : IGanttExporter
{
    public ExportFormat Format => ExportFormat.PlantUml;
    public string FileExtension => "puml";

    public string Export(GanttExportModel model, ExportOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("@startgantt\n");
        sb.Append($"title {Escape(model.Title)}\n");

        var tasks = model.Sections.SelectMany(s => s.Tasks).ToList();
        var projectStart = tasks.Count > 0 ? tasks.Min(t => t.Start) : model.From ?? model.Today;
        sb.Append($"Project starts {ExportText.Date(projectStart)}\n");

        if (options.ExcludeWeekends)
        {
            sb.Append("saturday are closed\n");
            sb.Append("sunday are closed\n");
        }
        foreach (var day in model.ClosedDays)
            sb.Append($"{ExportText.Date(day)} is closed\n");
        if (options.MarkToday)
            sb.Append($"today is {ExportText.Date(model.Today)} and is colored in #FFE0E0\n");

        foreach (var section in model.Sections.Where(s => s.Tasks.Count > 0))
        {
            sb.Append('\n');
            sb.Append($"-- {Escape(section.Name)} --\n");
            foreach (var task in section.Tasks)
            {
                var alias = $"[{task.Id.ToUpperInvariant()}]";
                sb.Append($"[{Escape(ExportText.Label(task, options.Label))}] as {alias} starts {ExportText.Date(task.Start)} and ends {ExportText.Date(task.End)}\n");
                sb.Append($"{alias} is colored in {EpicPalette.ColorFor(task.EpicKey)}\n");
                if (task.StatusCategory == StatusCategories.Done)
                    sb.Append($"{alias} is 100% completed\n");
            }
        }

        if (tasks.Count == 0)
            sb.Append("\n' Нет задач с датами за выбранный период\n");
        if (model.Unscheduled.Count > 0)
            sb.Append($"\n' Не запланировано: {string.Join(", ", model.Unscheduled.Select(u => u.Key))}\n");

        sb.Append("@endgantt\n");
        return sb.ToString();
    }

    /// <summary>Квадратные скобки ломают синтаксис задач — заменяем на круглые.</summary>
    internal static string Escape(string text) =>
        ExportText.SingleLine(text).Replace('[', '(').Replace(']', ')');
}
