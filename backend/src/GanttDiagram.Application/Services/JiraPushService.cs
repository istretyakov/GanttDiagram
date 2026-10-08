using System.Collections.Concurrent;
using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Jira;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GanttDiagram.Application.Services;

/// <summary>
/// Отправка плановых сроков (и по желанию исполнителя) в Jira — этап 2.
/// Перед записью каждая задача перечитывается: если в Jira она изменилась после последней
/// синхронизации (поле updated новее снимка), задача не записывается и помечается конфликтом.
/// </summary>
public class JiraPushService(
    IAppDbContext db,
    ITokenProtector tokenProtector,
    IJiraClientFactory jiraFactory,
    TimeProvider time,
    ILogger<JiraPushService> logger)
{
    public const string FieldStart = "start";
    public const string FieldEnd = "end";
    public const string FieldAssignee = "assignee";

    private const int MaxParallelRequests = 4;

    public async Task<JiraPushPreview> PreviewAsync(Guid projectId, bool includeAssignee, CancellationToken ct)
    {
        var (connection, issues) = await LoadAsync(projectId, ct);
        var warnings = new List<string>();
        var items = BuildItems(connection, issues, includeAssignee, warnings);
        return new JiraPushPreview(items, connection.StartDateFieldId, connection.EndDateFieldId, warnings);
    }

    public async Task<JiraPushResult> PushAsync(Guid projectId, JiraPushRequest request, CancellationToken ct)
    {
        var (connection, issues) = await LoadAsync(projectId, ct);
        var items = BuildItems(connection, issues, request.IncludeAssignee, []);
        if (request.IssueIds is not null)
            items = items.Where(i => request.IssueIds.Contains(i.IssueId)).ToList();
        if (items.Count == 0)
            return new JiraPushResult([], 0, 0, 0);

        var token = tokenProtector.Unprotect(connection.EncryptedToken);
        var reader = jiraFactory.CreateReader(connection.BaseUrl, token);
        var writer = jiraFactory.CreateWriter(connection.BaseUrl, token);
        var mapping = new JiraFieldMapping(connection.StartDateFieldId, connection.EndDateFieldId);
        var schemaTypes = await LoadSchemaTypesAsync(reader, ct);
        var issuesById = issues.ToDictionary(i => i.Id);

        // Логины исполнителей для записи и обратное сопоставление после перечитывания.
        var users = await db.Users.AsNoTracking().Where(u => u.ConnectionId == connection.Id).ToListAsync(ct);
        var loginById = users.ToDictionary(u => u.Id, u => u.Name);
        var idByLogin = users.ToDictionary(u => u.Name, u => u.Id, StringComparer.OrdinalIgnoreCase);

        var outcomes = new ConcurrentDictionary<Guid, (JiraPushResultItem Result, JiraIssue? Fresh)>();
        using var gate = new SemaphoreSlim(MaxParallelRequests);
        await Task.WhenAll(items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                outcomes[item.IssueId] = await PushOneAsync(
                    item, issuesById[item.IssueId], reader, writer, mapping, schemaTypes, loginById, ct);
            }
            finally
            {
                gate.Release();
            }
        }));

        await ApplyOutcomesAsync(outcomes, idByLogin, ct);

        var results = items.Select(i => outcomes[i.IssueId].Result).ToList();
        logger.LogInformation("Отправка в Jira, проект {Project}: {Synced} записано, {Conflicts} конфликтов, {Failed} ошибок",
            projectId, results.Count(r => r.Status == JiraPushStatus.Synced),
            results.Count(r => r.Status == JiraPushStatus.Conflict), results.Count(r => r.Status == JiraPushStatus.Failed));
        return new JiraPushResult(results,
            results.Count(r => r.Status == JiraPushStatus.Synced),
            results.Count(r => r.Status == JiraPushStatus.Conflict),
            results.Count(r => r.Status == JiraPushStatus.Failed));
    }

    private static async Task<(JiraPushResultItem, JiraIssue?)> PushOneAsync(
        JiraPushItem item, Issue issue, IJiraReader reader, IJiraWriter writer, JiraFieldMapping mapping,
        IReadOnlyDictionary<string, string?> schemaTypes, IReadOnlyDictionary<Guid, string> loginById, CancellationToken ct)
    {
        try
        {
            var current = (await reader.GetIssuesByKeysAsync([item.Key], mapping, ct)).FirstOrDefault();
            if (current is null)
                return (new JiraPushResultItem(item.IssueId, item.Key, JiraPushStatus.Failed, "Задача не найдена в Jira."), null);

            if (issue.JiraUpdatedAt is { } known && current.Updated is { } updated && updated > known)
            {
                return (new JiraPushResultItem(item.IssueId, item.Key, JiraPushStatus.Conflict,
                    $"Задача изменена в Jira после последней синхронизации ({updated.ToLocalTime():dd.MM.yyyy HH:mm}). " +
                    "Снимок Jira обновлён — проверьте план и отправьте снова."), current);
            }

            var fields = new Dictionary<string, object?>();
            foreach (var change in item.Changes)
            {
                if (change.Field == FieldStart)
                    fields[mapping.StartDateFieldId!] = FormatDate(issue.PlanStartDate, schemaTypes.GetValueOrDefault(mapping.StartDateFieldId!));
                else if (change.Field == FieldEnd)
                    fields[mapping.EndDateFieldId] = FormatDate(issue.PlanEndDate, schemaTypes.GetValueOrDefault(mapping.EndDateFieldId));
            }
            if (fields.Count > 0)
                await writer.UpdateFieldsAsync(item.Key, fields, ct);

            if (item.Changes.Any(c => c.Field == FieldAssignee))
            {
                var login = issue.PlanAssigneeId is { } id ? loginById.GetValueOrDefault(id) : null;
                await writer.AssignIssueAsync(item.Key, login, ct);
            }

            var fresh = (await reader.GetIssuesByKeysAsync([item.Key], mapping, ct)).FirstOrDefault();
            return (new JiraPushResultItem(item.IssueId, item.Key, JiraPushStatus.Synced, null), fresh);
        }
        catch (JiraException ex)
        {
            var hint = ex.StatusCode == 400
                ? " Проверьте, что поля дат есть на экране редактирования задачи и у токена есть права Edit Issues / Assign Issues."
                : "";
            return (new JiraPushResultItem(item.IssueId, item.Key, JiraPushStatus.Failed, ex.Message + hint), null);
        }
    }

    /// <summary>Обновляет снимок Jira у записанных и конфликтных задач. План не трогается.</summary>
    private async Task ApplyOutcomesAsync(
        IReadOnlyDictionary<Guid, (JiraPushResultItem Result, JiraIssue? Fresh)> outcomes,
        IReadOnlyDictionary<string, Guid> idByLogin, CancellationToken ct)
    {
        var ids = outcomes.Where(o => o.Value.Fresh is not null).Select(o => o.Key).ToList();
        if (ids.Count == 0) return;

        var now = time.GetUtcNow();
        // Перечитываем из БД непосредственно перед записью — минимальное окно для параллельных правок.
        var tracked = await db.Issues.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        foreach (var issue in tracked)
        {
            var fresh = outcomes[issue.Id].Fresh!;
            Guid? assigneeId = fresh.Assignee is null
                ? null
                : idByLogin.TryGetValue(fresh.Assignee.Name, out var userId) ? userId : issue.JiraAssigneeId;
            issue.ApplyJiraSnapshot(fresh.StartDate, fresh.EndDate, assigneeId, fresh.Updated, now);
            // Всё, что совпало с Jira, больше не локальная правка.
            issue.IsLocallyModified = issue.DiffersFromJira;
        }
        await db.SaveChangesAsync(ct);
    }

    private List<JiraPushItem> BuildItems(JiraConnection connection, List<Issue> issues, bool includeAssignee, List<string> warnings)
    {
        var items = new List<JiraPushItem>();
        var startSkipped = 0;
        foreach (var issue in issues.OrderBy(i => i.Key))
        {
            var changes = new List<JiraPushChange>();
            if (issue.PlanStartDate != issue.JiraStartDate)
            {
                if (connection.StartDateFieldId is null) startSkipped++;
                else changes.Add(new JiraPushChange(FieldStart, Format(issue.JiraStartDate), Format(issue.PlanStartDate)));
            }
            if (issue.PlanEndDate != issue.JiraEndDate)
                changes.Add(new JiraPushChange(FieldEnd, Format(issue.JiraEndDate), Format(issue.PlanEndDate)));
            if (includeAssignee && issue.PlanAssigneeId != issue.JiraAssigneeId)
                changes.Add(new JiraPushChange(FieldAssignee, issue.JiraAssignee?.DisplayName, issue.PlanAssignee?.DisplayName));

            if (changes.Count > 0)
                items.Add(new JiraPushItem(issue.Id, issue.Key, issue.Summary, connection.BrowseUrl(issue.Key), changes));
        }
        if (startSkipped > 0)
            warnings.Add($"Поле «ожидаемое начало» не сопоставлено в подключении — даты начала ({startSkipped}) не отправляются.");
        return items;
    }

    private async Task<(JiraConnection, List<Issue>)> LoadAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().Include(p => p.Connection)
                          .FirstOrDefaultAsync(p => p.Id == projectId, ct)
                      ?? throw NotFoundException.For("Проект", projectId);
        var issues = await db.Issues
            .AsNoTracking()
            .Include(i => i.PlanAssignee)
            .Include(i => i.JiraAssignee)
            .Where(i => db.ProjectIssues.Any(pi => pi.ProjectId == projectId && pi.IssueId == i.Id))
            .ToListAsync(ct);
        return (project.Connection!, issues);
    }

    private async Task<IReadOnlyDictionary<string, string?>> LoadSchemaTypesAsync(IJiraReader reader, CancellationToken ct)
    {
        try
        {
            return (await reader.GetFieldsAsync(ct)).ToDictionary(f => f.Id, f => f.SchemaType);
        }
        catch (JiraException ex) when (ex.StatusCode is not null)
        {
            // Jira ответила, но список полей недоступен (права) — отправляем даты как date.
            // Сетевая ошибка (StatusCode = null) не перехватывается: Jira недоступна, отправку прерываем сразу.
            logger.LogWarning(ex, "Не удалось получить типы полей Jira, даты отправляются как date");
            return new Dictionary<string, string?>();
        }
    }

    /// <summary>Поле типа date — "2026-10-05", datetime — начало дня в UTC.</summary>
    internal static string? FormatDate(DateOnly? date, string? schemaType) => date switch
    {
        null => null,
        { } d when schemaType == "datetime" => $"{d:yyyy-MM-dd}T00:00:00.000+0000",
        { } d => d.ToString("yyyy-MM-dd"),
    };

    private static string? Format(DateOnly? date) => date?.ToString("yyyy-MM-dd");
}
