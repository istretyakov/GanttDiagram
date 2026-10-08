using GanttDiagram.Application.Abstractions;
using GanttDiagram.Domain;
using GanttDiagram.Domain.Entities;
using GanttDiagram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Infrastructure;

/// <summary>
/// Демо-данные для разработки диаграммы без настоящей Jira (POST /api/dev/seed, только Development).
/// Подключение «Демо» указывает на несуществующий адрес — импорт и проверка для него не работают.
/// </summary>
public class DemoSeeder(AppDbContext db, ITokenProtector tokenProtector, TimeProvider time)
{
    public const string DemoConnectionName = "Демо (без Jira)";

    public async Task<Guid> SeedAsync(CancellationToken ct)
    {
        var existing = await db.Projects.FirstOrDefaultAsync(p => p.Connection!.Name == DemoConnectionName, ct);
        if (existing is not null) return existing.Id;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var connection = new JiraConnection
        {
            Id = Guid.NewGuid(),
            Name = DemoConnectionName,
            BaseUrl = "https://jira.example.local",
            EncryptedToken = tokenProtector.Protect("demo"),
            ProjectKeys = ["PRJ"],
            StartDateFieldId = "customfield_10015",
            CreatedAt = now,
        };
        db.Connections.Add(connection);

        var users = new[]
        {
            ("ivanov", "Иван Иванов"), ("petrov", "Пётр Петров"), ("sidorova", "Анна Сидорова"),
            ("kuznetsov", "Олег Кузнецов"), ("smirnova", "Мария Смирнова"),
        }.Select(u => new JiraUser
        {
            Id = Guid.NewGuid(), ConnectionId = connection.Id, Name = u.Item1, JiraKey = u.Item1,
            DisplayName = u.Item2, Email = $"{u.Item1}@example.local",
        }).ToArray();
        db.Users.AddRange(users);

        var epics = new[] { ("PRJ-1", "Авторизация и профиль"), ("PRJ-2", "Отчёты"), ("PRJ-3", "Интеграции") }
            .Select(e => new Epic { Id = Guid.NewGuid(), ConnectionId = connection.Id, Key = e.Item1, Summary = e.Item2, LastSyncedAt = now })
            .ToArray();
        db.Epics.AddRange(epics);

        // (эпик, название, оценка в часах, исполнитель, сдвиг начала в днях от понедельника | null, статус)
        var specs = new (int Epic, string Summary, int Hours, int? User, int? Offset, string Category)[]
        {
            (0, "Форма входа", 16, 0, 0, StatusCategories.Done),
            (0, "Восстановление пароля", 8, 0, 3, StatusCategories.InProgress),
            (0, "Страница профиля", 24, 1, 2, StatusCategories.InProgress),
            (0, "Аватары пользователей", 8, 1, 8, StatusCategories.New),
            (0, "Двухфакторная аутентификация", 40, null, null, StatusCategories.New),
            (1, "Отчёт по задачам", 24, 2, 0, StatusCategories.InProgress),
            (1, "Экспорт в Excel", 16, 2, 7, StatusCategories.New),
            (1, "Графики нагрузки", 32, 0, 7, StatusCategories.New),
            (1, "Фильтры отчётов", 12, null, null, StatusCategories.New),
            (1, "Планировщик рассылки отчётов", 20, 3, 14, StatusCategories.New),
            (2, "Интеграция с Jira", 40, 1, 9, StatusCategories.New),
            (2, "Webhooks", 16, 3, 1, StatusCategories.InProgress),
            (2, "Импорт из CSV", 8, null, null, StatusCategories.New),
            (2, "SSO через корпоративный каталог", 24, 4, -7, StatusCategories.Done),
            (2, "Логи интеграций", 6, 2, 4, StatusCategories.New),
        };

        var issues = new List<Issue>();
        for (var n = 0; n < specs.Length; n++)
        {
            var s = specs[n];
            DateOnly? start = s.Offset is null ? null : monday.AddDays(s.Offset.Value);
            DateOnly? end = start is null ? null : WorkingDays.EndFor(start.Value, WorkingDays.DurationFromEstimate(s.Hours * 3600, 8));
            var issue = new Issue
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                EpicId = epics[s.Epic].Id,
                Key = $"PRJ-{10 + n}",
                Summary = s.Summary,
                Status = s.Category switch { StatusCategories.Done => "Готово", StatusCategories.InProgress => "В работе", _ => "К выполнению" },
                StatusCategory = s.Category,
                OriginalEstimateSeconds = s.Hours * 3600L,
            };
            issue.ApplyJiraSnapshot(start, end, s.User is null ? null : users[s.User.Value].Id, now, now);
            issues.Add(issue);
        }

        // Одна задача уже передвинута локально — видно значок «отличается от Jira».
        var moved = issues[3];
        moved.ApplyPlan(moved.PlanStartDate!.Value.AddDays(2), moved.PlanEndDate!.Value.AddDays(2), moved.PlanAssigneeId, now);
        db.Issues.AddRange(issues);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Релиз 3.2",
            Description = "Демонстрационный проект",
            ConnectionId = connection.Id,
            CreatedAt = now,
        };
        db.Projects.Add(project);
        db.ProjectEpics.AddRange(epics.Select(e => new ProjectEpic { ProjectId = project.Id, EpicId = e.Id }));
        db.ProjectIssues.AddRange(issues.Select(i => new ProjectIssue { ProjectId = project.Id, IssueId = i.Id }));
        db.ProjectMembers.AddRange(users.Take(4).Select((u, index) => new ProjectMember
        {
            ProjectId = project.Id, JiraUserId = u.Id, SortOrder = index,
        }));

        await db.SaveChangesAsync(ct);
        return project.Id;
    }
}
