
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Application.Dtos;

// ---------- Подключения

public record ConnectionDto(
    Guid Id,
    string Name,
    string BaseUrl,
    IReadOnlyList<string> ProjectKeys,
    string? StartDateFieldId,
    string EndDateFieldId,
    int HoursPerDay,
    bool HasToken,
    DateTimeOffset CreatedAt,
    IReadOnlyList<HierarchyLinkDto> HierarchyLinks);

/// <summary>Связь Jira, образующая иерархию: родитель — задача на стороне ParentSide.</summary>
public record HierarchyLinkDto(string LinkTypeName, LinkDirection ParentSide);

public record JiraLinkTypeDto(string Name, string Outward, string Inward);

public record ConnectionInput(
    // Пустое значение — название по адресу сервера и ключу проекта.
    string? Name,
    string BaseUrl,
    // Пустое значение при обновлении оставляет прежний токен.
    string? Token,
    // Один или несколько проектов Jira.
    IReadOnlyList<string>? ProjectKeys,
    string? StartDateFieldId,
    string? EndDateFieldId,
    int? HoursPerDay,
    // null — не менять (при создании — без иерархии).
    IReadOnlyList<HierarchyLinkDto>? HierarchyLinks = null);

public record ConnectionTestResult(
    bool Ok,
    string? UserName,
    string? DisplayName,
    string? ServerVersion,
    string? ServerTitle,
    string? Error);

public record JiraFieldDto(string Id, string Name, bool Custom, string? SchemaType);

public record FieldsResponse(
    IReadOnlyList<JiraFieldDto> Fields,
    string? SuggestedStartFieldId,
    string? SuggestedEndFieldId);

/// <summary>
/// Проверка подключения до сохранения: адрес и токен.
/// Пустой токен и ConnectionId — взять сохранённый токен этого подключения (редактирование).
/// </summary>
public record JiraDiscoverRequest(string BaseUrl, string? Token, Guid? ConnectionId);

public record JiraProjectDto(string Key, string Name);

/// <summary>Что удалось узнать о Jira по адресу и токену: пользователь, версия, проекты, поля и подсказки для формы.</summary>
public record JiraDiscoverResult(
    bool Ok,
    string? Error,
    string BaseUrl,
    string? UserName,
    string? DisplayName,
    string? ServerVersion,
    string? ServerTitle,
    IReadOnlyList<JiraProjectDto> Projects,
    IReadOnlyList<JiraFieldDto> Fields,
    string? SuggestedProjectKey,
    string? SuggestedStartFieldId,
    string SuggestedEndFieldId,
    string SuggestedName,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<JiraLinkTypeDto> LinkTypes,
    IReadOnlyList<HierarchyLinkDto> SuggestedHierarchyLinks);

public record EpicSearchItem(string Key, string Summary, string Status, bool Imported);

public record ImportRequest(IReadOnlyList<string> EpicKeys, Guid? ProjectId);

public record ImportSummary(
    int Epics,
    int Created,
    int Updated,
    int Skipped,
    int DiffersFromJira,
    int UsersUpserted,
    IReadOnlyList<string> NotFoundEpics);

public record UsersSyncResult(int Created, int Updated, int Total);

// ---------- Пользователи

public record UserDto(
    Guid Id,
    Guid ConnectionId,
    string Name,
    string DisplayName,
    string? Email,
    string? AvatarUrl,
    bool IsActive);

// ---------- Проекты

public record EpicDto(Guid Id, string Key, string Summary, DateTimeOffset LastSyncedAt);

public record ProjectSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid ConnectionId,
    string ConnectionName,
    DateTimeOffset CreatedAt,
    int IssueCount,
    int MemberCount,
    int EpicCount);

public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    Guid ConnectionId,
    string ConnectionName,
    string JiraBaseUrl,
    DateTimeOffset CreatedAt,
    IReadOnlyList<EpicDto> Epics,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<IssueDto> Issues);

public record ProjectInput(
    string Name,
    string? Description,
    Guid ConnectionId);

public record AttachEpicsRequest(IReadOnlyList<string> EpicKeys);

public record AttachIssuesRequest(IReadOnlyList<string> IssueKeys);

public record AttachIssuesResult(int Attached, IReadOnlyList<string> NotFound);

/// <summary>
/// Новый проект на основе существующего. Задачи Jira общие: плановые даты задачи одни и те же
/// во всех проектах, куда она входит.
/// </summary>
public record CopyProjectRequest(
    string? Name,
    string? Description,
    bool IncludeEpics = true,
    bool IncludeIssues = true,
    bool IncludeMembers = true);

public record AddMemberRequest(Guid JiraUserId);

public record MembersOrderRequest(IReadOnlyList<Guid> UserIds);

public record MemberDto(Guid UserId, string Name, string DisplayName, string? AvatarUrl, int SortOrder);

// ---------- Задачи и диаграмма

public record IssueDto(
    Guid Id,
    string Key,
    string Summary,
    string Status,
    string StatusCategory,
    Guid? EpicId,
    string? EpicKey,
    string? EpicSummary,
    long? OriginalEstimateSeconds,
    DateOnly? PlanStartDate,
    DateOnly? PlanEndDate,
    Guid? PlanAssigneeId,
    string? PlanAssigneeName,
    DateOnly? JiraStartDate,
    DateOnly? JiraEndDate,
    Guid? JiraAssigneeId,
    string? JiraAssigneeName,
    bool IsLocallyModified,
    bool DiffersFromJira,
    uint Version,
    string JiraUrl,
    // Иерархия по связям Jira внутри проекта: родительская задача и глагол связи («includes», «is tested by»).
    Guid? ParentIssueId = null,
    string? ParentLinkLabel = null);

public record DateRange(DateOnly From, DateOnly To);

public record GanttProjectHeader(Guid Id, string Name, Guid ConnectionId, string JiraBaseUrl, int HoursPerDay);

public record GanttDto(
    GanttProjectHeader Project,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<IssueDto> Issues,
    IReadOnlyList<EpicDto> Epics,
    DateRange Range,
    DateOnly Today);

public record GanttChange(Guid IssueId, DateOnly? StartDate, DateOnly? EndDate, Guid? AssigneeId, uint Version);

public record GanttSaveRequest(IReadOnlyList<GanttChange> Changes);

public record GanttSaveResult(int Saved);

// ---------- Нагрузка

public record ProjectRef(Guid Id, string Name);

public record WorkloadTaskDto(IssueDto Issue, IReadOnlyList<ProjectRef> Projects);

public record WorkloadDayDto(DateOnly Date, double Hours, double Capacity, bool Overloaded);

public record WorkloadDto(
    UserDto User,
    DateRange Range,
    int HoursPerDay,
    string JiraBaseUrl,
    IReadOnlyList<WorkloadTaskDto> Tasks,
    IReadOnlyList<WorkloadTaskDto> Unscheduled,
    IReadOnlyList<WorkloadDayDto> Days,
    double TotalHours,
    double CapacityHours,
    double LoadPercent,
    int WithoutEstimate,
    DateOnly Today);

// ---------- Экспорт

public record ExportResult(string Format, string FileName, string Content);

// ---------- Отправка сроков в Jira (этап 2)

/// <summary>Одно изменение поля: field = start | end | assignee; значения для показа.</summary>
public record JiraPushChange(string Field, string? From, string? To);

public record JiraPushItem(Guid IssueId, string Key, string Summary, string JiraUrl, IReadOnlyList<JiraPushChange> Changes);

public record JiraPushPreview(
    IReadOnlyList<JiraPushItem> Items,
    string? StartDateFieldId,
    string EndDateFieldId,
    IReadOnlyList<string> Warnings);

/// <summary>IssueIds = null — все задачи из предпросмотра.</summary>
public record JiraPushRequest(IReadOnlyList<Guid>? IssueIds, bool IncludeAssignee);

public enum JiraPushStatus
{
    Synced,
    Conflict,
    Failed,
}

public record JiraPushResultItem(Guid IssueId, string Key, JiraPushStatus Status, string? Message);

public record JiraPushResult(IReadOnlyList<JiraPushResultItem> Items, int Synced, int Conflicts, int Failed);
