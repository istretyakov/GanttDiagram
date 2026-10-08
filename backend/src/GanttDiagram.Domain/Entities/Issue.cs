namespace GanttDiagram.Domain.Entities;

/// <summary>
/// Задача Jira. Хранит раздельно снимок Jira (Jira*, обновляется только импортом)
/// и локальный план (Plan*, редактируется на диаграмме).
/// </summary>
public class Issue
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public JiraConnection? Connection { get; set; }
    public Guid? EpicId { get; set; }
    public Epic? Epic { get; set; }

    public required string Key { get; set; }
    public required string Summary { get; set; }
    public string Status { get; set; } = "";

    /// <summary>Категория статуса Jira: new, indeterminate, done.</summary>
    public string StatusCategory { get; set; } = StatusCategories.New;

    public long? OriginalEstimateSeconds { get; set; }

    // --- снимок Jira
    public DateOnly? JiraStartDate { get; set; }
    public DateOnly? JiraEndDate { get; set; }
    public Guid? JiraAssigneeId { get; set; }
    public JiraUser? JiraAssignee { get; set; }
    public DateTimeOffset? JiraUpdatedAt { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }

    // --- локальный план
    public DateOnly? PlanStartDate { get; set; }
    public DateOnly? PlanEndDate { get; set; }
    public Guid? PlanAssigneeId { get; set; }
    public JiraUser? PlanAssignee { get; set; }
    public bool IsLocallyModified { get; set; }
    public DateTimeOffset? LocalModifiedAt { get; set; }

    /// <summary>Связи задачи в Jira (снимок, обновляется импортом).</summary>
    public List<IssueLink> Links { get; set; } = [];

    /// <summary>Токен оптимистичной блокировки (xmin в PostgreSQL).</summary>
    public uint Version { get; set; }

    public bool DiffersFromJira =>
        PlanStartDate != JiraStartDate || PlanEndDate != JiraEndDate || PlanAssigneeId != JiraAssigneeId;

    public bool IsScheduled => PlanStartDate is not null && PlanEndDate is not null;

    /// <summary>
    /// Записывает значения из Jira в снимок. План заполняется из снимка, только если
    /// задача новая или ещё не правилась локально.
    /// </summary>
    public void ApplyJiraSnapshot(DateOnly? start, DateOnly? end, Guid? assigneeId, DateTimeOffset? updatedAt, DateTimeOffset syncedAt)
    {
        JiraStartDate = start;
        JiraEndDate = end;
        JiraAssigneeId = assigneeId;
        JiraUpdatedAt = updatedAt;
        LastSyncedAt = syncedAt;

        if (!IsLocallyModified)
        {
            PlanStartDate = start;
            PlanEndDate = end;
            PlanAssigneeId = assigneeId;
        }
    }

    /// <summary>Применяет правку с диаграммы. Флаг локальной правки ставится, только если план разошёлся с Jira.</summary>
    public void ApplyPlan(DateOnly? start, DateOnly? end, Guid? assigneeId, DateTimeOffset now)
    {
        PlanStartDate = start;
        PlanEndDate = end;
        PlanAssigneeId = assigneeId;
        IsLocallyModified = DiffersFromJira;
        LocalModifiedAt = now;
    }

    public void ResetToJira()
    {
        PlanStartDate = JiraStartDate;
        PlanEndDate = JiraEndDate;
        PlanAssigneeId = JiraAssigneeId;
        IsLocallyModified = false;
        LocalModifiedAt = null;
    }

    public bool Overlaps(DateOnly from, DateOnly to) =>
        IsScheduled && PlanStartDate <= to && PlanEndDate >= from;
}

public static class StatusCategories
{
    public const string New = "new";
    public const string InProgress = "indeterminate";
    public const string Done = "done";
}
