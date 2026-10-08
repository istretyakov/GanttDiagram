using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GanttDiagram.Application.Jira;
using GanttDiagram.Domain.Entities;
using Polly.Timeout;

namespace GanttDiagram.Infrastructure.Jira;

/// <summary>
/// Клиент Jira Server / Data Center, REST API v2, только чтение.
/// HttpClient приходит настроенным: BaseAddress и заголовок Authorization: Bearer &lt;PAT&gt;.
/// </summary>
public partial class JiraServerClient(HttpClient http) : IJiraReader, IJiraWriter
{
    private const int SearchPageSize = 100;
    private const int UsersPageSize = 1000;
    private const int MaxPages = 200;
    private const int KeysPerSearch = 50;

    private static readonly string[] BaseIssueFields =
        ["summary", "status", "assignee", "timeoriginalestimate", "duedate", "updated", "issuelinks"];

    public async Task<JiraServerInfo> GetServerInfoAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync("rest/api/2/serverInfo", ct);
        return new JiraServerInfo(Str(json, "version") ?? "?", Str(json, "serverTitle"), Str(json, "baseUrl"));
    }

    public async Task<JiraUserInfo> GetMyselfAsync(CancellationToken ct = default) =>
        ParseUser(await GetJsonAsync("rest/api/2/myself", ct));

    public async Task<IReadOnlyList<JiraField>> GetFieldsAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync("rest/api/2/field", ct);
        return json.EnumerateArray()
            .Select(f => new JiraField(
                Str(f, "id") ?? "",
                Str(f, "name") ?? "",
                f.TryGetProperty("custom", out var custom) && custom.ValueKind == JsonValueKind.True,
                f.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Object ? Str(schema, "type") : null))
            .Where(f => f.Id.Length > 0)
            .ToList();
    }

    /// <summary>Проекты, видимые владельцу токена.</summary>
    public async Task<IReadOnlyList<JiraProject>> GetProjectsAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync("rest/api/2/project", ct);
        return json.EnumerateArray()
            .Select(p => new JiraProject(Str(p, "key") ?? "", Str(p, "name") ?? ""))
            .Where(p => p.Key.Length > 0)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<JiraLinkType>> GetIssueLinkTypesAsync(CancellationToken ct = default)
    {
        var json = await GetJsonAsync("rest/api/2/issueLinkType", ct);
        return json.TryGetProperty("issueLinkTypes", out var types)
            ? types.EnumerateArray()
                .Select(t => new JiraLinkType(Str(t, "name") ?? "", Str(t, "outward") ?? "", Str(t, "inward") ?? ""))
                .Where(t => t.Name.Length > 0)
                .ToList()
            : [];
    }

    public async Task<IReadOnlyList<JiraIssue>> SearchEpicsAsync(IReadOnlyCollection<string> projectKeys, string? text, CancellationToken ct = default)
    {
        var jql = $"project in ({string.Join(", ", projectKeys.Select(Quote))}) AND issuetype = Epic";
        if (!string.IsNullOrWhiteSpace(text))
            jql += $" AND summary ~ {Quote(text.Trim() + "*")}";
        jql += " ORDER BY created DESC";

        var mapping = new JiraFieldMapping(null, "duedate");
        return await SearchAsync(jql, mapping, maxTotal: 100, ct);
    }

    /// <summary>
    /// Загружает задачи по ключам: пачками через JQL <c>key in (...)</c>. Такой запрос падает целиком,
    /// если хотя бы одной задачи нет или к ней нет доступа, — тогда пачка грузится поштучно,
    /// отсутствующие ключи пропускаются.
    /// </summary>
    public async Task<IReadOnlyList<JiraIssue>> GetIssuesByKeysAsync(IReadOnlyCollection<string> keys, JiraFieldMapping mapping, CancellationToken ct = default)
    {
        var result = new List<JiraIssue>();
        foreach (var chunk in keys.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(KeysPerSearch))
        {
            if (chunk.Length > 1)
            {
                try
                {
                    result.AddRange(await SearchAsync($"key in ({string.Join(", ", chunk.Select(Quote))})", mapping, chunk.Length, ct));
                    continue;
                }
                catch (JiraException ex) when (ex.StatusCode is >= 400 and < 500)
                {
                    // Есть удалённые или недоступные задачи — грузим поштучно.
                }
            }
            result.AddRange(await GetIssuesOneByOneAsync(chunk, mapping, ct));
        }
        return result;
    }

    private async Task<List<JiraIssue>> GetIssuesOneByOneAsync(IEnumerable<string> keys, JiraFieldMapping mapping, CancellationToken ct)
    {
        var fields = string.Join(",", IssueFields(mapping));
        var result = new List<JiraIssue>();
        foreach (var key in keys)
        {
            var json = await GetJsonAsync($"rest/api/2/issue/{Uri.EscapeDataString(key)}?fields={fields}", ct, allowNotFound: true);
            if (json.ValueKind == JsonValueKind.Object)
                result.Add(ParseIssue(json, mapping));
        }
        return result;
    }

    public Task<IReadOnlyList<JiraIssue>> GetEpicChildrenAsync(string epicKey, JiraFieldMapping mapping, CancellationToken ct = default) =>
        SearchAsync($"\"Epic Link\" = {Quote(epicKey)} ORDER BY key ASC", mapping, maxTotal: int.MaxValue, ct);

    /// <summary>Пользователи, которым можно назначать задачи хотя бы в одном из проектов (объединение).</summary>
    public async Task<IReadOnlyList<JiraUserInfo>> GetAssignableUsersAsync(IReadOnlyCollection<string> projectKeys, CancellationToken ct = default)
    {
        var users = new List<JiraUserInfo>();
        foreach (var projectKey in projectKeys)
            users.AddRange(await GetAssignableUsersAsync(projectKey, ct));
        return users.DistinctBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<JiraUserInfo>> GetAssignableUsersAsync(string projectKey, CancellationToken ct)
    {
        var users = new List<JiraUserInfo>();
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"rest/api/2/user/assignable/search?project={Uri.EscapeDataString(projectKey)}&username=&startAt={page * UsersPageSize}&maxResults={UsersPageSize}";
            var json = await GetJsonAsync(url, ct);
            var batch = json.EnumerateArray().Select(ParseUser).ToList();
            users.AddRange(batch);
            if (batch.Count < UsersPageSize) break;
        }
        return users;
    }

    // ---------- поиск с пагинацией

    private async Task<IReadOnlyList<JiraIssue>> SearchAsync(string jql, JiraFieldMapping mapping, int maxTotal, CancellationToken ct)
    {
        var fields = IssueFields(mapping);
        var result = new List<JiraIssue>();
        var startAt = 0;
        for (var page = 0; page < MaxPages; page++)
        {
            var body = new { jql, fields, startAt, maxResults = Math.Min(SearchPageSize, maxTotal - result.Count) };
            var json = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, "rest/api/2/search") { Content = JsonContent.Create(body) }, ct);

            var issues = json.TryGetProperty("issues", out var arr) ? arr.EnumerateArray().ToList() : [];
            result.AddRange(issues.Select(i => ParseIssue(i, mapping)));

            var total = json.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv) ? tv : result.Count;
            startAt += issues.Count;
            if (issues.Count == 0 || startAt >= total || result.Count >= maxTotal) break;
        }
        return result;
    }

    private static List<string> IssueFields(JiraFieldMapping mapping)
    {
        var fields = new List<string>(BaseIssueFields);
        if (!string.IsNullOrEmpty(mapping.StartDateFieldId)) fields.Add(mapping.StartDateFieldId);
        if (!string.IsNullOrEmpty(mapping.EndDateFieldId)) fields.Add(mapping.EndDateFieldId);
        return fields.Distinct().ToList();
    }

    // ---------- HTTP

    // ---------- запись (этап 2)

    public Task UpdateFieldsAsync(string key, IReadOnlyDictionary<string, object?> fields, CancellationToken ct = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"rest/api/2/issue/{Uri.EscapeDataString(key)}")
        {
            Content = JsonContent.Create(new { fields }),
        }, ct);

    public Task AssignIssueAsync(string key, string? userName, CancellationToken ct = default) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"rest/api/2/issue/{Uri.EscapeDataString(key)}/assignee")
        {
            Content = JsonContent.Create(new { name = userName }),
        }, ct);

    private Task<JsonElement> GetJsonAsync(string url, CancellationToken ct, bool allowNotFound = false) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct, allowNotFound);

    private async Task<JsonElement> SendAsync(Func<HttpRequestMessage> createRequest, CancellationToken ct, bool allowNotFound = false)
    {
        HttpResponseMessage response;
        using var request = createRequest();
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            throw new JiraException(
                "Не удалось проверить SSL-сертификат Jira. Добавьте корневой сертификат компании в доверенные " +
                "или отключите проверку: JIRA_SSL_VERIFY=false.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new JiraException($"Не удалось подключиться к Jira: {ex.Message}", null, ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new JiraException("Jira не ответила вовремя.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new JiraException("Jira не ответила вовремя.", null, ex);
        }

        using (response)
        {
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return default;

            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new JiraException(DescribeError(response.StatusCode, content), (int)response.StatusCode);
            // PUT в Jira отвечает 204 без тела.
            if (string.IsNullOrWhiteSpace(content))
                return default;

            if (response.Content.Headers.ContentType?.MediaType is { } mediaType && !mediaType.Contains("json"))
                throw new JiraException("Jira вернула не JSON. Проверьте адрес (возможно, открылась страница входа или прокси).", (int)response.StatusCode);

            try
            {
                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new JiraException("Не удалось разобрать ответ Jira.", (int)response.StatusCode, ex);
            }
        }
    }

    private static string DescribeError(HttpStatusCode status, string content)
    {
        var details = ExtractErrorMessages(content);
        var prefix = status switch
        {
            HttpStatusCode.Unauthorized => "Jira отклонила токен (401). Проверьте Personal Access Token.",
            HttpStatusCode.Forbidden => "Недостаточно прав в Jira (403).",
            HttpStatusCode.NotFound => "Ресурс Jira не найден (404). Проверьте адрес и ключ проекта.",
            HttpStatusCode.BadRequest => "Jira отклонила запрос (400).",
            _ => $"Ошибка Jira ({(int)status}).",
        };
        return details is null ? prefix : $"{prefix} {details}";
    }

    private static string? ExtractErrorMessages(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var messages = new List<string>();
            if (doc.RootElement.TryGetProperty("errorMessages", out var list) && list.ValueKind == JsonValueKind.Array)
                messages.AddRange(list.EnumerateArray().Select(m => m.GetString()).OfType<string>());
            if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                messages.AddRange(errors.EnumerateObject().Select(p => $"{p.Name}: {p.Value}"));
            return messages.Count > 0 ? string.Join(" ", messages) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------- разбор ответов

    internal static JiraIssue ParseIssue(JsonElement json, JiraFieldMapping mapping)
    {
        var fields = json.GetProperty("fields");

        var status = new JiraIssueStatus("", "new");
        if (fields.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.Object)
        {
            var category = s.TryGetProperty("statusCategory", out var c) && c.ValueKind == JsonValueKind.Object
                ? Str(c, "key") ?? "new"
                : "new";
            status = new JiraIssueStatus(Str(s, "name") ?? "", category);
        }

        JiraUserInfo? assignee = null;
        if (fields.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object)
            assignee = ParseUser(a);

        long? estimate = fields.TryGetProperty("timeoriginalestimate", out var e) && e.ValueKind == JsonValueKind.Number
            ? e.GetInt64()
            : null;

        return new JiraIssue(
            Str(json, "key") ?? "",
            Str(fields, "summary") ?? "",
            status,
            assignee,
            estimate,
            mapping.StartDateFieldId is null ? null : ParseDate(fields, mapping.StartDateFieldId),
            ParseDate(fields, mapping.EndDateFieldId),
            ParseDateTime(Str(fields, "updated")),
            ParseLinks(fields));
    }

    /// <summary>issuelinks: [{ type: {name, inward, outward}, outwardIssue | inwardIssue: {key} }].</summary>
    internal static IReadOnlyList<JiraIssueLink> ParseLinks(JsonElement fields)
    {
        if (!fields.TryGetProperty("issuelinks", out var links) || links.ValueKind != JsonValueKind.Array) return [];
        var result = new List<JiraIssueLink>();
        foreach (var link in links.EnumerateArray())
        {
            if (!link.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.Object) continue;
            var (direction, other) =
                link.TryGetProperty("outwardIssue", out var outward) && outward.ValueKind == JsonValueKind.Object
                    ? (LinkDirection.Outward, outward)
                    : link.TryGetProperty("inwardIssue", out var inward) && inward.ValueKind == JsonValueKind.Object
                        ? (LinkDirection.Inward, inward)
                        : (LinkDirection.Outward, default);
            var key = Str(other, "key");
            var name = Str(type, "name");
            if (key is null || name is null) continue;
            result.Add(new JiraIssueLink(name, direction, key, Str(type, "outward") ?? "", Str(type, "inward") ?? ""));
        }
        return result;
    }

    internal static JiraUserInfo ParseUser(JsonElement json)
    {
        string? avatar = null;
        if (json.TryGetProperty("avatarUrls", out var avatars) && avatars.ValueKind == JsonValueKind.Object)
            avatar = Str(avatars, "48x48") ?? Str(avatars, "32x32");
        var name = Str(json, "name") ?? Str(json, "key") ?? "";
        return new JiraUserInfo(
            name,
            Str(json, "key"),
            Str(json, "displayName") ?? name,
            Str(json, "emailAddress"),
            avatar,
            !json.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.False);
    }

    /// <summary>Поле даты: "2026-10-05" или дата-время "2026-10-05T10:00:00.000+0300" (берём дату).</summary>
    internal static DateOnly? ParseDate(JsonElement fields, string fieldId)
    {
        var value = Str(fields, fieldId);
        if (value is null || value.Length < 10) return null;
        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Jira Server отдаёт "2026-10-01T12:00:00.000+0300" — смещение без двоеточия.</summary>
    internal static DateTimeOffset? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var normalized = OffsetWithoutColon().Replace(value, "$1:$2");
        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
            ? result.ToUniversalTime()
            : null;
    }

    private static string? Str(JsonElement json, string property) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
