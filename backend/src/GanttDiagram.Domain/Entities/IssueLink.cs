namespace GanttDiagram.Domain.Entities;

/// <summary>
/// Связь задачи Jira (issue link) с точки зрения задачи <see cref="IssueId"/>.
/// Связь в Jira: «S &lt;outward&gt; T» и «T &lt;inward&gt; S».
/// Direction = outward — эта задача S («эта includes другая»), inward — эта задача T.
/// </summary>
public class IssueLink
{
    public Guid Id { get; set; }
    public Guid IssueId { get; set; }
    public Issue? Issue { get; set; }

    public required string LinkTypeName { get; set; }
    public required LinkDirection Direction { get; set; }
    public required string LinkedIssueKey { get; set; }
    public string OutwardVerb { get; set; } = "";
    public string InwardVerb { get; set; } = "";
}

public enum LinkDirection
{
    Outward,
    Inward,
}

/// <summary>
/// Правило иерархии: связи типа <see cref="LinkTypeName"/> делают родителем задачу на стороне
/// <see cref="ParentSide"/>. Например, «Includes» + Outward: «A includes B» → A родитель;
/// «Tests» + Inward: «A is tested by B» → A родитель, тест B — дочерняя.
/// </summary>
public record HierarchyRule(string LinkTypeName, LinkDirection ParentSide)
{
    /// <summary>Хранится строкой «Имя типа|outward».</summary>
    public string Serialize() => $"{LinkTypeName}|{ParentSide.ToString().ToLowerInvariant()}";

    public static HierarchyRule? Parse(string value)
    {
        var i = value.LastIndexOf('|');
        if (i <= 0) return null;
        return Enum.TryParse<LinkDirection>(value[(i + 1)..], ignoreCase: true, out var side)
            ? new HierarchyRule(value[..i], side)
            : null;
    }

    /// <summary>Связанная задача — родитель задачи-владельца связи.</summary>
    public bool LinkedIsParent(string linkTypeName, LinkDirection direction) =>
        Matches(linkTypeName) && direction != ParentSide;

    /// <summary>Связанная задача — дочерняя для задачи-владельца связи.</summary>
    public bool LinkedIsChild(string linkTypeName, LinkDirection direction) =>
        Matches(linkTypeName) && direction == ParentSide;

    private bool Matches(string linkTypeName) =>
        string.Equals(linkTypeName, LinkTypeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Глагол связи с точки зрения родителя: «родитель &lt;verb&gt; дочерняя».</summary>
    public string Verb(string outwardVerb, string inwardVerb) => ParentSide == LinkDirection.Outward ? outwardVerb : inwardVerb;
}
