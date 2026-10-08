namespace GanttDiagram.Domain.Entities;

public class Epic
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public JiraConnection? Connection { get; set; }
    public required string Key { get; set; }
    public required string Summary { get; set; }
    public DateTimeOffset LastSyncedAt { get; set; }
}
