using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Services;

public class GanttService(IAppDbContext db, ProjectService projects, TimeProvider time)
{
    private const int PaddingBeforeDays = 7;
    private const int PaddingAfterDays = 30;

    public async Task<GanttDto> GetAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .AsNoTracking()
            .Include(p => p.Connection)
            .Include(p => p.Epics).ThenInclude(pe => pe.Epic)
            .Include(p => p.Members).ThenInclude(m => m.JiraUser)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw NotFoundException.For("Проект", projectId);
        var connection = project.Connection!;
        var issues = await projects.LoadIssuesAsync(projectId, ct);
        var parents = IssueHierarchy.Compute(issues, connection.HierarchyRules);
        var today = Today();

        var epics = project.Epics.Select(pe => pe.Epic!)
            .Concat(issues.Select(i => i.Epic).OfType<Epic>())
            .DistinctBy(e => e.Id)
            .OrderBy(e => e.Key)
            .Select(e => e.ToDto())
            .ToList();

        return new GanttDto(
            new GanttProjectHeader(project.Id, project.Name, connection.Id, connection.BaseUrl, connection.HoursPerDay),
            project.Members.OrderBy(m => m.SortOrder).Select(m => m.ToDto()).ToList(),
            issues.Select(i => i.ToDto(connection.BaseUrl, parents.GetValueOrDefault(i.Id))).ToList(),
            epics,
            ComputeRange(issues, today),
            today);
    }

    /// <summary>
    /// Массовое сохранение правок диаграммы в локальную БД: всё или ничего.
    /// Конфликт версий (xmin) → <see cref="ConflictException"/>, ошибки проверки → <see cref="ValidationFailedException"/>.
    /// </summary>
    public async Task<GanttSaveResult> SaveAsync(Guid projectId, GanttSaveRequest request, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == projectId, ct))
            throw NotFoundException.For("Проект", projectId);
        if (request.Changes.Count == 0) return new GanttSaveResult(0);

        var duplicates = request.Changes.GroupBy(c => c.IssueId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new ValidationFailedException("Одна задача передана несколько раз.",
                duplicates.ToDictionary(id => id.ToString(), _ => new[] { "Повторяющаяся правка." }));

        var issueIds = request.Changes.Select(c => c.IssueId).ToList();
        var issues = await db.ProjectIssues
            .Where(pi => pi.ProjectId == projectId && issueIds.Contains(pi.IssueId))
            .Select(pi => pi.Issue!)
            .ToDictionaryAsync(i => i.Id, ct);
        var memberIds = (await db.ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .Select(m => m.JiraUserId)
            .ToListAsync(ct)).ToHashSet();

        var errors = new Dictionary<string, string[]>();
        var conflicts = new List<Guid>();
        foreach (var change in request.Changes)
        {
            var problems = new List<string>();
            if (!issues.TryGetValue(change.IssueId, out var issue))
            {
                problems.Add("Задача не входит в проект.");
            }
            else if (issue.Version != change.Version)
            {
                conflicts.Add(change.IssueId);
                continue;
            }

            if (change.StartDate is null != change.EndDate is null)
                problems.Add("Даты начала и окончания задаются вместе.");
            if (change.StartDate is { } start && change.EndDate is { } end && end < start)
                problems.Add("Дата окончания раньше даты начала.");
            if (change.AssigneeId is { } assigneeId && !memberIds.Contains(assigneeId))
                problems.Add("Исполнитель не является участником проекта.");

            if (problems.Count > 0) errors[change.IssueId.ToString()] = problems.ToArray();
        }

        if (conflicts.Count > 0)
            throw new ConflictException("Задачи изменены в другой вкладке или при синхронизации. Перезагрузите диаграмму.", conflicts);
        if (errors.Count > 0)
            throw new ValidationFailedException("Часть правок не прошла проверку. Ничего не сохранено.", errors);

        var now = time.GetUtcNow();
        foreach (var change in request.Changes)
        {
            var issue = issues[change.IssueId];
            // Исходная версия из запроса — EF добавит её в WHERE, и параллельная правка даст конфликт.
            db.Issues.Entry(issue).Property(i => i.Version).OriginalValue = change.Version;
            issue.ApplyPlan(change.StartDate, change.EndDate, change.AssigneeId, now);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var ids = ex.Entries.Select(e => e.Entity).OfType<Issue>().Select(i => i.Id).ToList();
            throw new ConflictException("Задачи изменены в другой вкладке или при синхронизации. Перезагрузите диаграмму.", ids);
        }

        return new GanttSaveResult(request.Changes.Count);
    }

    public async Task<IssueDto> ResetToJiraAsync(Guid issueId, CancellationToken ct)
    {
        var issue = await db.Issues
            .Include(i => i.Connection)
            .Include(i => i.Epic)
            .Include(i => i.PlanAssignee)
            .Include(i => i.JiraAssignee)
            .FirstOrDefaultAsync(i => i.Id == issueId, ct)
            ?? throw NotFoundException.For("Задача", issueId);
        issue.ResetToJira();
        issue.PlanAssignee = issue.JiraAssignee;
        await db.SaveChangesAsync(ct);
        return issue.ToDto(issue.Connection!.BaseUrl);
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    internal static DateRange ComputeRange(IReadOnlyCollection<Issue> issues, DateOnly today)
    {
        var scheduled = issues.Where(i => i.IsScheduled).ToList();
        var min = scheduled.Count > 0 ? scheduled.Min(i => i.PlanStartDate!.Value) : today;
        var max = scheduled.Count > 0 ? scheduled.Max(i => i.PlanEndDate!.Value) : today;
        if (today < min) min = today;
        if (today > max) max = today;

        // Начинаем с понедельника, чтобы шкала по неделям была ровной.
        var from = min.AddDays(-PaddingBeforeDays);
        from = from.AddDays(-(((int)from.DayOfWeek + 6) % 7));
        return new DateRange(from, max.AddDays(PaddingAfterDays));
    }
}
