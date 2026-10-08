namespace GanttDiagram.Domain.Entities;

/// <summary>Пользователь Jira, синхронизированный из исполнителей задач или из списка assignable.</summary>
public class JiraUser
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public JiraConnection? Connection { get; set; }

    /// <summary>Логин (поле name в Jira Server).</summary>
    public required string Name { get; set; }

    /// <summary>Поле key в Jira Server.</summary>
    public string? JiraKey { get; set; }

    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
