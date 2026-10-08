using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Jira;

public record JiraServerInfo(string Version, string? ServerTitle, string? BaseUrl);

public record JiraUserInfo(string Name, string? Key, string DisplayName, string? Email, string? AvatarUrl, bool Active);

public record JiraField(string Id, string Name, bool Custom, string? SchemaType);

public record JiraProject(string Key, string Name);

public record JiraIssueStatus(string Name, string Category);

/// <summary>Тип связи Jira: «S &lt;outward&gt; T», «T &lt;inward&gt; S».</summary>
public record JiraLinkType(string Name, string Outward, string Inward);

/// <summary>Связь с точки зрения задачи, у которой она прочитана.</summary>
public record JiraIssueLink(string TypeName, LinkDirection Direction, string LinkedKey, string OutwardVerb, string InwardVerb);

public record JiraIssue(
    string Key,
    string Summary,
    JiraIssueStatus Status,
    JiraUserInfo? Assignee,
    long? OriginalEstimateSeconds,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateTimeOffset? Updated,
    IReadOnlyList<JiraIssueLink>? Links = null);

/// <summary>Какие поля задачи читать как даты начала и окончания.</summary>
public record JiraFieldMapping(string? StartDateFieldId, string EndDateFieldId);

/// <summary>Ошибка обращения к Jira.</summary>
public class JiraException(string message, int? statusCode = null, Exception? inner = null) : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
}
