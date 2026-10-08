using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Services;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Export;

/// <summary>Собирает <see cref="GanttExportModel"/> из сохранённых плановых значений и отдаёт текст нужного формата.</summary>
public class ExportService(
    IAppDbContext db,
    ProjectService projects,
    WorkloadService workload,
    IEnumerable<IGanttExporter> exporters,
    TimeProvider time)
{
    public async Task<ExportResult> ExportProjectAsync(
        Guid projectId, ExportFormat format, ExportGroupBy groupBy, DateOnly? from, DateOnly? to,
        ExportOptions options, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Connection)
            .Include(p => p.Members).ThenInclude(m => m.JiraUser)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw NotFoundException.For("Проект", projectId);
        var issues = await projects.LoadIssuesAsync(projectId, ct);
        var members = project.Members.OrderBy(m => m.SortOrder).Select(m => m.JiraUser!).ToList();

        var groups = groupBy switch
        {
            ExportGroupBy.Epic => GroupByEpic(issues),
            _ => GroupByMember(issues, members),
        };
        var model = BuildModel(project.Name, from, to, groups, issues, project.Connection!.BaseUrl);
        return Render(model, format, options, project.Name);
    }

    public async Task<ExportResult> ExportWorkloadAsync(
        Guid userId, ExportFormat format, DateOnly? from, DateOnly? to, ExportOptions options, CancellationToken ct)
    {
        var data = await workload.LoadAsync(userId, from, to, ct);
        var groups = data.Issues
            .GroupBy(i => data.ProjectsByIssue.TryGetValue(i.Id, out var refs) && refs.Count > 0 ? refs[0].Name : "Без проекта")
            .OrderBy(g => g.Key == "Без проекта" ? 1 : 0).ThenBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<Issue>)g.ToList()))
            .ToList();
        var title = $"Нагрузка: {data.User.DisplayName}";
        var model = BuildModel(title, data.From, data.To, groups, data.Issues, data.User.Connection!.BaseUrl);
        return Render(model, format, options, data.User.DisplayName);
    }

    internal GanttExportModel BuildModel(
        string title, DateOnly? from, DateOnly? to,
        IReadOnlyList<(string Name, IReadOnlyList<Issue> Issues)> groups,
        IReadOnlyList<Issue> allIssues, string baseUrl)
    {
        if (from is not null && to is not null && to < from)
            throw new ValidationFailedException("Конец периода раньше начала.");

        var counter = 0;
        var sections = new List<ExportSection>();
        foreach (var (name, issues) in groups)
        {
            var tasks = issues
                .Where(i => i.IsScheduled && InPeriod(i, from, to))
                .OrderBy(i => i.PlanStartDate).ThenBy(i => i.Key)
                .Select(i => new ExportTask(
                    $"t{++counter}", i.Key, i.Summary, i.PlanStartDate!.Value, i.PlanEndDate!.Value,
                    i.Status, i.StatusCategory, i.Epic?.Key, i.PlanAssignee?.DisplayName,
                    $"{baseUrl.TrimEnd('/')}/browse/{i.Key}"))
                .ToList();
            sections.Add(new ExportSection(name, tasks));
        }

        var unscheduled = allIssues
            .Where(i => !i.IsScheduled)
            .Select(i => new ExportUnscheduled(i.Key, i.Summary))
            .ToList();

        return new GanttExportModel(title, from, to, Today(), [], sections, unscheduled);
    }

    private ExportResult Render(GanttExportModel model, ExportFormat format, ExportOptions options, string name)
    {
        var exporter = exporters.FirstOrDefault(e => e.Format == format)
                       ?? throw new ValidationFailedException($"Формат {format} не поддерживается.");
        var safeName = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
        var fileName = $"{safeName}_{model.Today:yyyy-MM-dd}.{exporter.FileExtension}";
        return new ExportResult(format.ToString().ToLowerInvariant(), fileName, exporter.Export(model, options));
    }

    private static bool InPeriod(Issue issue, DateOnly? from, DateOnly? to) =>
        (from is null || issue.PlanEndDate >= from) && (to is null || issue.PlanStartDate <= to);

    private static List<(string, IReadOnlyList<Issue>)> GroupByMember(IReadOnlyList<Issue> issues, IReadOnlyList<JiraUser> members)
    {
        var memberIds = members.Select(m => m.Id).ToHashSet();
        var result = members
            .Select(m => (m.DisplayName, (IReadOnlyList<Issue>)issues.Where(i => i.PlanAssigneeId == m.Id).ToList()))
            .ToList();
        // Исполнители, которых нет среди участников, — отдельными секциями после участников.
        result.AddRange(issues
            .Where(i => i.PlanAssigneeId is not null && !memberIds.Contains(i.PlanAssigneeId.Value))
            .GroupBy(i => i.PlanAssigneeId)
            .Select(g => (g.First().PlanAssignee?.DisplayName ?? "?", (IReadOnlyList<Issue>)g.ToList()))
            .OrderBy(g => g.Item1));
        var unassigned = issues.Where(i => i.PlanAssigneeId is null).ToList();
        if (unassigned.Count > 0) result.Add(("Без исполнителя", unassigned));
        return result;
    }

    private static List<(string, IReadOnlyList<Issue>)> GroupByEpic(IReadOnlyList<Issue> issues) =>
        issues
            .GroupBy(i => i.EpicId)
            .Select(g => (Epic: g.First().Epic, Issues: (IReadOnlyList<Issue>)g.ToList()))
            .OrderBy(g => g.Epic is null ? 1 : 0).ThenBy(g => g.Epic?.Key)
            .Select(g => (g.Epic is null ? "Без эпика" : $"{g.Epic.Key} · {g.Epic.Summary}", g.Issues))
            .ToList();

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
