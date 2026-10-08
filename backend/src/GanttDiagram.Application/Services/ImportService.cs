using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Jira;
using GanttDiagram.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GanttDiagram.Application.Services;

/// <summary>Импорт Jira → БД. Локальные правки плана не затираются, обновляется только снимок Jira.</summary>
public class ImportService(
    IAppDbContext db,
    ConnectionService connections,
    TimeProvider time,
    ILogger<ImportService> logger)
{
    /// <summary>Глубина и предел подтягивания дочерних задач по связям иерархии.</summary>
    private const int MaxLinkDepth = 5;
    private const int MaxLinkedIssues = 1000;

    public async Task<ImportSummary> ImportAsync(Guid connectionId, IReadOnlyList<string> epicKeys, Guid? projectId, CancellationToken ct)
    {
        var keys = epicKeys
            .Select(k => k.Trim().ToUpperInvariant())
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();
        if (keys.Count == 0)
            throw new ValidationFailedException("Выберите хотя бы один эпик.");

        var connection = await connections.FindAsync(connectionId, ct);
        Project? project = null;
        if (projectId is not null)
        {
            project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
                      ?? throw NotFoundException.For("Проект", projectId);
            if (project.ConnectionId != connectionId)
                throw new ValidationFailedException("Проект привязан к другому подключению Jira.");
        }

        var reader = connections.CreateReader(connection);
        var mapping = new JiraFieldMapping(connection.StartDateFieldId, connection.EndDateFieldId);
        var now = time.GetUtcNow();

        // 1. Эпики
        var jiraEpics = await reader.GetIssuesByKeysAsync(keys, mapping, ct);
        var notFound = keys.Except(jiraEpics.Select(e => e.Key), StringComparer.OrdinalIgnoreCase).ToList();

        var epicsByKey = await db.Epics
            .Where(e => e.ConnectionId == connectionId && keys.Contains(e.Key))
            .ToDictionaryAsync(e => e.Key, ct);

        // 2. Дочерние задачи
        var children = new List<(Epic Epic, JiraIssue Issue, bool ViaLink)>();
        foreach (var jiraEpic in jiraEpics)
        {
            if (!epicsByKey.TryGetValue(jiraEpic.Key, out var epic))
            {
                epic = new Epic { Id = Guid.NewGuid(), ConnectionId = connectionId, Key = jiraEpic.Key, Summary = jiraEpic.Summary };
                db.Epics.Add(epic);
                epicsByKey[epic.Key] = epic;
            }
            epic.Summary = jiraEpic.Summary;
            epic.LastSyncedAt = now;

            var issues = await reader.GetEpicChildrenAsync(jiraEpic.Key, mapping, ct);
            logger.LogInformation("Эпик {Epic}: загружено задач {Count}", jiraEpic.Key, issues.Count);
            children.AddRange(issues.Select(i => (epic, i, false)));
        }

        // 2a. Дочерние по связям иерархии (например, тесты из «is tested by»), даже если они вне эпика.
        await AddLinkedChildrenAsync(children, connection.HierarchyRules, reader, mapping, ct);

        // 3. Пользователи (исполнители)
        var usersByName = await db.Users
            .Where(u => u.ConnectionId == connectionId)
            .ToDictionaryAsync(u => u.Name, StringComparer.OrdinalIgnoreCase, ct);
        var usersUpserted = 0;
        foreach (var assignee in children.Select(c => c.Issue.Assignee).OfType<JiraUserInfo>().DistinctBy(a => a.Name))
        {
            UpsertUser(connectionId, assignee, usersByName);
            usersUpserted++;
        }

        // 4. Задачи: снимок Jira + план
        var issueKeys = children.Select(c => c.Issue.Key).Distinct().ToList();
        var issuesByKey = await db.Issues
            .Include(i => i.Links)
            .Where(i => i.ConnectionId == connectionId && issueKeys.Contains(i.Key))
            .ToDictionaryAsync(i => i.Key, ct);

        int created = 0, updated = 0, skipped = 0;
        var touched = new List<Issue>();
        foreach (var (epic, jiraIssue, viaLink) in children.DistinctBy(c => c.Issue.Key))
        {
            var assigneeId = jiraIssue.Assignee is null ? (Guid?)null : usersByName[jiraIssue.Assignee.Name].Id;

            if (!issuesByKey.TryGetValue(jiraIssue.Key, out var issue))
            {
                issue = new Issue { Id = Guid.NewGuid(), ConnectionId = connectionId, Key = jiraIssue.Key, Summary = jiraIssue.Summary };
                db.Issues.Add(issue);
                issuesByKey[issue.Key] = issue;
                created++;
            }
            else if (issue.JiraUpdatedAt is not null && issue.JiraUpdatedAt == jiraIssue.Updated && (viaLink || issue.EpicId == epic.Id))
            {
                skipped++;
            }
            else
            {
                updated++;
            }

            // Задача, подтянутая по связи, остаётся в своём эпике, если он уже известен.
            if (!viaLink || issue.EpicId is null) issue.EpicId = epic.Id;
            issue.Summary = jiraIssue.Summary;
            ReplaceLinks(issue, jiraIssue.Links);
            issue.Status = jiraIssue.Status.Name;
            issue.StatusCategory = jiraIssue.Status.Category;
            issue.OriginalEstimateSeconds = jiraIssue.OriginalEstimateSeconds;
            issue.ApplyJiraSnapshot(jiraIssue.StartDate, jiraIssue.EndDate, assigneeId, jiraIssue.Updated, now);
            touched.Add(issue);
        }

        // 5. Привязка к проекту
        if (project is not null)
        {
            var epicIds = epicsByKey.Values.Select(e => e.Id).ToList();
            var linkedEpics = await db.ProjectEpics
                .Where(pe => pe.ProjectId == project.Id)
                .Select(pe => pe.EpicId)
                .ToListAsync(ct);
            foreach (var epicId in epicIds.Except(linkedEpics))
                db.ProjectEpics.Add(new ProjectEpic { ProjectId = project.Id, EpicId = epicId });

            var linkedIssues = await db.ProjectIssues
                .Where(pi => pi.ProjectId == project.Id)
                .Select(pi => pi.IssueId)
                .ToListAsync(ct);
            foreach (var issueId in touched.Select(i => i.Id).Except(linkedIssues))
                db.ProjectIssues.Add(new ProjectIssue { ProjectId = project.Id, IssueId = issueId });
        }

        await db.SaveChangesAsync(ct);

        return new ImportSummary(
            jiraEpics.Count, created, updated, skipped,
            touched.Count(i => i.DiffersFromJira),
            usersUpserted, notFound);
    }

    /// <summary>
    /// Подтягивает дочерние задачи по правилам иерархии подключения (в ширину, до <see cref="MaxLinkDepth"/> уровней).
    /// Дочерняя получает эпик родителя, если своего ещё нет.
    /// </summary>
    private async Task AddLinkedChildrenAsync(
        List<(Epic Epic, JiraIssue Issue, bool ViaLink)> children, IReadOnlyList<HierarchyRule> rules,
        IJiraReader reader, JiraFieldMapping mapping, CancellationToken ct)
    {
        if (rules.Count == 0) return;
        var known = children.Select(c => c.Issue.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var frontier = children.ToList();
        var linkedCount = 0;
        for (var depth = 0; depth < MaxLinkDepth && frontier.Count > 0; depth++)
        {
            var wanted = new Dictionary<string, Epic>(StringComparer.OrdinalIgnoreCase);
            foreach (var (epic, issue, _) in frontier)
            {
                foreach (var link in issue.Links ?? [])
                {
                    if (!known.Contains(link.LinkedKey) && rules.Any(r => r.LinkedIsChild(link.TypeName, link.Direction)))
                        wanted.TryAdd(link.LinkedKey, epic);
                }
            }
            var keys = wanted.Keys.Take(MaxLinkedIssues - linkedCount).ToList();
            if (keys.Count == 0) break;

            var loaded = await reader.GetIssuesByKeysAsync(keys, mapping, ct);
            logger.LogInformation("Связи иерархии, уровень {Depth}: загружено задач {Count}", depth + 1, loaded.Count);
            frontier = loaded.Select(i => (wanted[i.Key], i, true)).ToList();
            children.AddRange(frontier);
            known.UnionWith(keys);
            linkedCount += keys.Count;
        }
    }

    private static void ReplaceLinks(Issue issue, IReadOnlyList<JiraIssueLink>? links)
    {
        if (links is null) return;
        issue.Links.Clear();
        issue.Links.AddRange(links
            .DistinctBy(l => (l.TypeName, l.Direction, l.LinkedKey))
            // Id не задаём: связь найдена через навигацию, EF вставит её как новую.
            .Select(l => new IssueLink
            {
                IssueId = issue.Id,
                LinkTypeName = l.TypeName,
                Direction = l.Direction,
                LinkedIssueKey = l.LinkedKey,
                OutwardVerb = l.OutwardVerb,
                InwardVerb = l.InwardVerb,
            }));
    }

    /// <summary>Повторная синхронизация эпиков проекта.</summary>
    public async Task<ImportSummary> SyncProjectAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, ct)
                      ?? throw NotFoundException.For("Проект", projectId);
        var epicKeys = await db.ProjectEpics
            .Where(pe => pe.ProjectId == projectId)
            .Select(pe => pe.Epic!.Key)
            .ToListAsync(ct);
        if (epicKeys.Count == 0)
            throw new ValidationFailedException("К проекту не привязано ни одного эпика.");
        return await ImportAsync(project.ConnectionId, epicKeys, projectId, ct);
    }

    /// <summary>«Обновить пользователей»: все assignable-пользователи проекта Jira.</summary>
    public async Task<UsersSyncResult> SyncUsersAsync(Guid connectionId, CancellationToken ct)
    {
        var connection = await connections.FindAsync(connectionId, ct);
        var jiraUsers = await connections.CreateReader(connection).GetAssignableUsersAsync(connection.ProjectKeys, ct);

        var usersByName = await db.Users
            .Where(u => u.ConnectionId == connectionId)
            .ToDictionaryAsync(u => u.Name, StringComparer.OrdinalIgnoreCase, ct);
        int created = 0, updated = 0;
        foreach (var jiraUser in jiraUsers.DistinctBy(u => u.Name))
        {
            if (UpsertUser(connectionId, jiraUser, usersByName)) created++;
            else updated++;
        }
        await db.SaveChangesAsync(ct);
        return new UsersSyncResult(created, updated, usersByName.Count);
    }

    /// <returns>true, если пользователь создан.</returns>
    private bool UpsertUser(Guid connectionId, JiraUserInfo info, Dictionary<string, JiraUser> usersByName)
    {
        var isNew = !usersByName.TryGetValue(info.Name, out var user);
        if (user is null)
        {
            user = new JiraUser { Id = Guid.NewGuid(), ConnectionId = connectionId, Name = info.Name, DisplayName = info.DisplayName };
            db.Users.Add(user);
            usersByName[info.Name] = user;
        }
        user.JiraKey = info.Key;
        user.DisplayName = info.DisplayName;
        user.Email = info.Email;
        user.AvatarUrl = info.AvatarUrl;
        user.IsActive = info.Active;
        return isNew;
    }
}
