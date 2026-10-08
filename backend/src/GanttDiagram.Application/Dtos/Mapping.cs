using GanttDiagram.Application.Services;
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Dtos;

public static class Mapping
{
    public static ConnectionDto ToDto(this JiraConnection c) => new(
        c.Id, c.Name, c.BaseUrl, c.ProjectKeys, c.StartDateFieldId, c.EndDateFieldId,
        c.HoursPerDay, !string.IsNullOrEmpty(c.EncryptedToken), c.CreatedAt,
        c.HierarchyRules.Select(r => new HierarchyLinkDto(r.LinkTypeName, r.ParentSide)).ToList());

    public static UserDto ToDto(this JiraUser u) => new(
        u.Id, u.ConnectionId, u.Name, u.DisplayName, u.Email, u.AvatarUrl, u.IsActive);

    public static EpicDto ToDto(this Epic e) => new(e.Id, e.Key, e.Summary, e.LastSyncedAt);

    public static MemberDto ToDto(this ProjectMember m) => new(
        m.JiraUserId, m.JiraUser!.Name, m.JiraUser.DisplayName, m.JiraUser.AvatarUrl, m.SortOrder);

    /// <summary>Требует загруженных Epic, PlanAssignee и JiraAssignee.</summary>
    public static IssueDto ToDto(this Issue i, string jiraBaseUrl, IssueParent? parent = null) => new(
        i.Id, i.Key, i.Summary, i.Status, i.StatusCategory,
        i.EpicId, i.Epic?.Key, i.Epic?.Summary,
        i.OriginalEstimateSeconds,
        i.PlanStartDate, i.PlanEndDate, i.PlanAssigneeId, i.PlanAssignee?.DisplayName,
        i.JiraStartDate, i.JiraEndDate, i.JiraAssigneeId, i.JiraAssignee?.DisplayName,
        i.IsLocallyModified, i.DiffersFromJira, i.Version,
        $"{jiraBaseUrl.TrimEnd('/')}/browse/{i.Key}",
        parent?.ParentId,
        parent?.Verb);
}
