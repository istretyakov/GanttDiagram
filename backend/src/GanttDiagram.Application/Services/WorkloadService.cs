using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Domain;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Services;

/// <summary>Нагрузка исполнителя по всем проектам (по плановым значениям).</summary>
public class WorkloadService(IAppDbContext db, TimeProvider time)
{
    public async Task<WorkloadDto> GetAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var data = await LoadAsync(userId, from, to, ct);
        var connection = data.User.Connection!;
        var hoursPerDay = connection.HoursPerDay;

        var scheduled = data.Issues.Where(i => i.Overlaps(data.From, data.To)).ToList();
        var unscheduled = data.Issues.Where(i => !i.IsScheduled).ToList();

        var hours = CalculateHours(scheduled, data.From, data.To);
        var days = ToDays(hours, data.From, data.To, hoursPerDay);
        var total = hours.Values.Sum();
        var capacity = days.Sum(d => d.Capacity);

        WorkloadTaskDto ToTask(Issue i) => new(i.ToDto(connection.BaseUrl), data.ProjectsByIssue.GetValueOrDefault(i.Id, []));

        return new WorkloadDto(
            data.User.ToDto(),
            new DateRange(data.From, data.To),
            hoursPerDay,
            connection.BaseUrl,
            scheduled.OrderBy(i => i.PlanStartDate).Select(ToTask).ToList(),
            unscheduled.Select(ToTask).ToList(),
            days,
            Math.Round(total, 1),
            capacity,
            capacity > 0 ? Math.Round(total / capacity * 100, 1) : 0,
            scheduled.Count(i => i.OriginalEstimateSeconds is null or 0),
            Today());
    }

    /// <summary>Часы по дням: оценка задачи равномерно делится на её рабочие дни.</summary>
    public static IReadOnlyList<WorkloadDayDto> CalculateDays(IEnumerable<Issue> issues, DateOnly from, DateOnly to, int hoursPerDay) =>
        ToDays(CalculateHours(issues, from, to), from, to, hoursPerDay);

    private static Dictionary<DateOnly, double> CalculateHours(IEnumerable<Issue> issues, DateOnly from, DateOnly to)
    {
        var hours = new Dictionary<DateOnly, double>();
        foreach (var issue in issues)
        {
            if (!issue.IsScheduled || issue.OriginalEstimateSeconds is not > 0) continue;
            var start = issue.PlanStartDate!.Value;
            var end = issue.PlanEndDate!.Value;
            var workingDays = WorkingDays.Count(start, end);
            var perDay = issue.OriginalEstimateSeconds.Value / 3600.0 / Math.Max(1, workingDays);

            foreach (var day in WorkingDays.Range(start > from ? start : from, end < to ? end : to))
            {
                // Задача целиком на выходных — считаем все её дни, иначе только рабочие.
                if (workingDays > 0 && !WorkingDays.IsWorkingDay(day)) continue;
                hours[day] = hours.GetValueOrDefault(day) + perDay;
            }
        }
        return hours;
    }

    private static IReadOnlyList<WorkloadDayDto> ToDays(Dictionary<DateOnly, double> hours, DateOnly from, DateOnly to, int hoursPerDay) =>
        WorkingDays.Range(from, to)
            .Select(day =>
            {
                var value = Math.Round(hours.GetValueOrDefault(day), 2);
                var capacity = WorkingDays.IsWorkingDay(day) ? hoursPerDay : 0;
                return new WorkloadDayDto(day, value, capacity, value > capacity + 0.001);
            })
            .ToList();

    internal record WorkloadData(
        JiraUser User,
        DateOnly From,
        DateOnly To,
        List<Issue> Issues,
        Dictionary<Guid, IReadOnlyList<ProjectRef>> ProjectsByIssue);

    internal async Task<WorkloadData> LoadAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Connection).FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw NotFoundException.For("Пользователь", userId);
        var (rangeFrom, rangeTo) = DefaultRange(from, to);

        var issues = await db.Issues
            .AsNoTracking()
            .Include(i => i.Epic)
            .Include(i => i.PlanAssignee)
            .Include(i => i.JiraAssignee)
            .Where(i => i.PlanAssigneeId == userId)
            .Where(i => i.PlanStartDate == null || i.PlanEndDate == null
                        || (i.PlanStartDate <= rangeTo && i.PlanEndDate >= rangeFrom))
            .OrderBy(i => i.Key)
            .ToListAsync(ct);

        var ids = issues.Select(i => i.Id).ToList();
        var links = await db.ProjectIssues
            .AsNoTracking()
            .Where(pi => ids.Contains(pi.IssueId))
            .Select(pi => new { pi.IssueId, pi.ProjectId, pi.Project!.Name })
            .ToListAsync(ct);
        var projectsByIssue = links
            .GroupBy(l => l.IssueId)
            .ToDictionary(g => g.Key,
                g => (IReadOnlyList<ProjectRef>)g.Select(l => new ProjectRef(l.ProjectId, l.Name)).OrderBy(p => p.Name).ToList());

        return new WorkloadData(user, rangeFrom, rangeTo, issues, projectsByIssue);
    }

    private (DateOnly From, DateOnly To) DefaultRange(DateOnly? from, DateOnly? to)
    {
        var today = Today();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var rangeFrom = from ?? monday.AddDays(-7);
        var rangeTo = to ?? rangeFrom.AddDays(8 * 7 - 1);
        if (rangeTo < rangeFrom)
            throw new ValidationFailedException("Конец периода раньше начала.");
        if (rangeTo.DayNumber - rangeFrom.DayNumber > 3 * 366)
            throw new ValidationFailedException("Период не может быть длиннее трёх лет.");
        return (rangeFrom, rangeTo);
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
