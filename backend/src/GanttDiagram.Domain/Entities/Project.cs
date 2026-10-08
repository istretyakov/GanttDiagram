namespace GanttDiagram.Domain.Entities;

/// <summary>Локальный проект: набор задач и участников, отображаемый диаграммой Ганта.</summary>
public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid ConnectionId { get; set; }
    public JiraConnection? Connection { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<ProjectEpic> Epics { get; set; } = [];
    public List<ProjectIssue> Issues { get; set; } = [];
    public List<ProjectMember> Members { get; set; } = [];
}

/// <summary>Эпики, которые пересинхронизируются вместе с проектом.</summary>
public class ProjectEpic
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid EpicId { get; set; }
    public Epic? Epic { get; set; }
}

public class ProjectIssue
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid IssueId { get; set; }
    public Issue? Issue { get; set; }
}

/// <summary>Строка диаграммы.</summary>
public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid JiraUserId { get; set; }
    public JiraUser? JiraUser { get; set; }
    public int SortOrder { get; set; }
}
