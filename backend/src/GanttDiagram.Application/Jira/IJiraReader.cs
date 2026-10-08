namespace GanttDiagram.Application.Jira;

/// <summary>Чтение Jira Server / Data Center через REST API v2 (этап 1).</summary>
public interface IJiraReader
{
    Task<JiraServerInfo> GetServerInfoAsync(CancellationToken ct = default);
    Task<JiraUserInfo> GetMyselfAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JiraField>> GetFieldsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JiraProject>> GetProjectsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JiraLinkType>> GetIssueLinkTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JiraIssue>> SearchEpicsAsync(IReadOnlyCollection<string> projectKeys, string? text, CancellationToken ct = default);
    Task<IReadOnlyList<JiraIssue>> GetIssuesByKeysAsync(IReadOnlyCollection<string> keys, JiraFieldMapping mapping, CancellationToken ct = default);
    Task<IReadOnlyList<JiraIssue>> GetEpicChildrenAsync(string epicKey, JiraFieldMapping mapping, CancellationToken ct = default);
    Task<IReadOnlyList<JiraUserInfo>> GetAssignableUsersAsync(IReadOnlyCollection<string> projectKeys, CancellationToken ct = default);
}

/// <summary>Запись в Jira (этап 2): плановые сроки и исполнитель.</summary>
public interface IJiraWriter
{
    /// <summary>PUT /rest/api/2/issue/{key} — значения полей (null очищает поле).</summary>
    Task UpdateFieldsAsync(string key, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default);

    /// <summary>PUT /rest/api/2/issue/{key}/assignee — логин или null (снять исполнителя).</summary>
    Task AssignIssueAsync(string key, string? userName, CancellationToken ct = default);
}

/// <summary>Создаёт клиент для конкретного подключения (у каждого свой адрес и токен).</summary>
public interface IJiraClientFactory
{
    IJiraReader CreateReader(string baseUrl, string token);
    IJiraWriter CreateWriter(string baseUrl, string token);
}
