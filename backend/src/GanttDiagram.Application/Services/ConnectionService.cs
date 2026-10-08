using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Application.Jira;
using GanttDiagram.Domain.Entities;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace GanttDiagram.Application.Services;

public class ConnectionService(
    IAppDbContext db,
    ITokenProtector tokenProtector,
    IJiraClientFactory jiraFactory,
    TimeProvider time)
{
    // Названия полей, которые подставляются при сопоставлении по умолчанию (в порядке приоритета).
    private static readonly string[] StartFieldNames = ["Start date", "Target start", "Дата начала", "Начало"];
    private static readonly string[] EndFieldNames = ["Target end", "Due date", "Срок исполнения", "Дата окончания"];

    public async Task<IReadOnlyList<ConnectionDto>> ListAsync(CancellationToken ct) =>
        (await db.Connections.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct))
        .Select(c => c.ToDto()).ToList();

    public async Task<ConnectionDto> GetAsync(Guid id, CancellationToken ct) =>
        (await FindAsync(id, ct)).ToDto();

    public async Task<ConnectionDto> CreateAsync(ConnectionInput input, CancellationToken ct)
    {
        var baseUrl = Validate(input, requireToken: true);
        var connection = new JiraConnection
        {
            Id = Guid.NewGuid(),
            Name = NameOrDefault(input, baseUrl),
            BaseUrl = baseUrl,
            EncryptedToken = tokenProtector.Protect(NormalizeToken(input.Token)!),
            ProjectKeys = NormalizeKeys(input.ProjectKeys),
            StartDateFieldId = NullIfEmpty(input.StartDateFieldId),
            EndDateFieldId = NullIfEmpty(input.EndDateFieldId) ?? JiraConnection.DefaultEndDateField,
            HoursPerDay = input.HoursPerDay ?? JiraConnection.DefaultHoursPerDay,
            HierarchyLinks = SerializeRules(input.HierarchyLinks),
            CreatedAt = time.GetUtcNow(),
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync(ct);
        return connection.ToDto();
    }

    public async Task<ConnectionDto> UpdateAsync(Guid id, ConnectionInput input, CancellationToken ct)
    {
        var baseUrl = Validate(input, requireToken: false);
        var connection = await FindAsync(id, ct);
        connection.Name = NameOrDefault(input, baseUrl);
        connection.BaseUrl = baseUrl;
        connection.ProjectKeys = NormalizeKeys(input.ProjectKeys);
        connection.StartDateFieldId = NullIfEmpty(input.StartDateFieldId);
        connection.EndDateFieldId = NullIfEmpty(input.EndDateFieldId) ?? JiraConnection.DefaultEndDateField;
        connection.HoursPerDay = input.HoursPerDay ?? connection.HoursPerDay;
        if (input.HierarchyLinks is not null)
            connection.HierarchyLinks = SerializeRules(input.HierarchyLinks);
        if (NormalizeToken(input.Token) is { } token)
        {
            connection.EncryptedToken = tokenProtector.Protect(token);
        }
        await db.SaveChangesAsync(ct);
        return connection.ToDto();
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var connection = await FindAsync(id, ct);
        db.Connections.Remove(connection);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Проверка подключения: myself + serverInfo.</summary>
    public async Task<ConnectionTestResult> TestAsync(Guid id, CancellationToken ct)
    {
        var reader = await CreateReaderAsync(id, ct);
        try
        {
            var myself = await reader.GetMyselfAsync(ct);
            var info = await reader.GetServerInfoAsync(ct);
            return new ConnectionTestResult(true, myself.Name, myself.DisplayName, info.Version, info.ServerTitle, null);
        }
        catch (JiraException ex)
        {
            return new ConnectionTestResult(false, null, null, null, null, ex.Message);
        }
    }

    public async Task<FieldsResponse> GetFieldsAsync(Guid id, CancellationToken ct)
    {
        var reader = await CreateReaderAsync(id, ct);
        return BuildFields(await reader.GetFieldsAsync(ct));
    }

    /// <summary>
    /// Подключение по адресу и токену до сохранения: проверяет доступ и возвращает всё,
    /// чтобы заполнить форму выбором — проекты Jira, поля дат, подсказки.
    /// </summary>
    public async Task<JiraDiscoverResult> DiscoverAsync(JiraDiscoverRequest request, CancellationToken ct)
    {
        if (!JiraUrl.TryParse(request.BaseUrl, out var parsed))
            throw new ValidationFailedException("Укажите адрес Jira.",
                new Dictionary<string, string[]> { ["baseUrl"] = ["Укажите адрес Jira вида https://jira.company.local."] });

        var token = NormalizeToken(request.Token);
        if (token is null && request.ConnectionId is { } connectionId)
            token = tokenProtector.Unprotect((await FindAsync(connectionId, ct)).EncryptedToken);
        if (token is null)
            throw new ValidationFailedException("Укажите токен.",
                new Dictionary<string, string[]> { ["token"] = ["Укажите Personal Access Token."] });

        var reader = jiraFactory.CreateReader(parsed.BaseUrl, token);
        JiraUserInfo myself;
        JiraServerInfo info;
        try
        {
            myself = await reader.GetMyselfAsync(ct);
            info = await reader.GetServerInfoAsync(ct);
        }
        catch (JiraException ex)
        {
            return new JiraDiscoverResult(false, ex.Message, parsed.BaseUrl, null, null, null, null, [], [],
                parsed.ProjectKey, null, JiraConnection.DefaultEndDateField,
                DefaultName(parsed.BaseUrl, parsed.ProjectKey is null ? [] : [parsed.ProjectKey]), [], [], []);
        }

        var warnings = new List<string>();
        IReadOnlyList<JiraProject> projects = [];
        var fields = new FieldsResponse([], null, JiraConnection.DefaultEndDateField);
        try
        {
            projects = await reader.GetProjectsAsync(ct);
        }
        catch (JiraException ex)
        {
            warnings.Add($"Не удалось получить список проектов: {ex.Message}");
        }
        try
        {
            fields = BuildFields(await reader.GetFieldsAsync(ct));
        }
        catch (JiraException ex)
        {
            warnings.Add($"Не удалось получить список полей: {ex.Message}");
        }
        IReadOnlyList<JiraLinkType> linkTypes = [];
        try
        {
            linkTypes = await reader.GetIssueLinkTypesAsync(ct);
        }
        catch (JiraException ex)
        {
            warnings.Add($"Не удалось получить типы связей: {ex.Message}");
        }

        // Проект из вставленной ссылки, иначе — единственный доступный.
        var projectKey = projects.FirstOrDefault(p => string.Equals(p.Key, parsed.ProjectKey, StringComparison.OrdinalIgnoreCase))?.Key
                         ?? (projects.Count == 1 ? projects[0].Key : parsed.ProjectKey);

        return new JiraDiscoverResult(
            true, null, parsed.BaseUrl, myself.Name, myself.DisplayName, info.Version, info.ServerTitle,
            projects.Select(p => new JiraProjectDto(p.Key, p.Name)).ToList(),
            fields.Fields,
            projectKey,
            fields.SuggestedStartFieldId,
            fields.SuggestedEndFieldId ?? JiraConnection.DefaultEndDateField,
            DefaultName(parsed.BaseUrl, projectKey is null ? [] : [projectKey]),
            warnings,
            linkTypes.Select(t => new JiraLinkTypeDto(t.Name, t.Outward, t.Inward)).ToList(),
            SuggestHierarchy(linkTypes));
    }

    // Связи, которые по умолчанию образуют иерархию: «включает» и «tested by» (в порядке приоритета).
    private static readonly Regex[] HierarchyVerbs =
    [
        new(@"^(includes?|включает)", RegexOptions.IgnoreCase),
        new(@"(is )?tested by|тестируется|проверяется", RegexOptions.IgnoreCase),
    ];

    /// <summary>Правила иерархии по названиям связей: родитель — та сторона, чей глагол совпал.</summary>
    internal static IReadOnlyList<HierarchyLinkDto> SuggestHierarchy(IReadOnlyList<JiraLinkType> linkTypes)
    {
        var result = new List<HierarchyLinkDto>();
        foreach (var verb in HierarchyVerbs)
        {
            foreach (var type in linkTypes)
            {
                if (verb.IsMatch(type.Outward.Trim())) result.Add(new HierarchyLinkDto(type.Name, LinkDirection.Outward));
                else if (verb.IsMatch(type.Inward.Trim())) result.Add(new HierarchyLinkDto(type.Name, LinkDirection.Inward));
            }
        }
        return result.DistinctBy(r => r.LinkTypeName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> SerializeRules(IEnumerable<HierarchyLinkDto>? rules) =>
        (rules ?? [])
        .Where(r => !string.IsNullOrWhiteSpace(r.LinkTypeName))
        .Select(r => new HierarchyRule(r.LinkTypeName.Trim(), r.ParentSide).Serialize())
        .Distinct()
        .ToList();

    private static FieldsResponse BuildFields(IReadOnlyList<JiraField> fields)
    {
        var dateFields = fields
            .Where(f => f.SchemaType is "date" or "datetime" || f.Id == JiraConnection.DefaultEndDateField)
            .ToList();

        return new FieldsResponse(
            fields.OrderBy(f => f.Name).Select(f => new JiraFieldDto(f.Id, f.Name, f.Custom, f.SchemaType)).ToList(),
            Suggest(dateFields, StartFieldNames),
            Suggest(dateFields, EndFieldNames) ?? JiraConnection.DefaultEndDateField);
    }

    /// <summary>Эпики во всех проектах подключения или в одном из них (<paramref name="projectKey"/>).</summary>
    public async Task<IReadOnlyList<EpicSearchItem>> SearchEpicsAsync(Guid id, string? search, string? projectKey, CancellationToken ct)
    {
        var connection = await FindAsync(id, ct);
        var projectKeys = string.IsNullOrWhiteSpace(projectKey)
            ? connection.ProjectKeys
            : connection.ProjectKeys.Where(k => string.Equals(k, projectKey.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (projectKeys.Count == 0)
            throw new ValidationFailedException($"Проект «{projectKey}» не входит в подключение.");
        var reader = CreateReader(connection);
        var epics = await reader.SearchEpicsAsync(projectKeys, search, ct);
        var keys = epics.Select(e => e.Key).ToList();
        var imported = await db.Epics
            .Where(e => e.ConnectionId == id && keys.Contains(e.Key))
            .Select(e => e.Key)
            .ToListAsync(ct);
        return epics
            .Select(e => new EpicSearchItem(e.Key, e.Summary, e.Status.Name, imported.Contains(e.Key)))
            .ToList();
    }

    public async Task<JiraConnection> FindAsync(Guid id, CancellationToken ct) =>
        await db.Connections.FirstOrDefaultAsync(c => c.Id == id, ct)
        ?? throw NotFoundException.For("Подключение", id);

    public async Task<IJiraReader> CreateReaderAsync(Guid id, CancellationToken ct) =>
        CreateReader(await FindAsync(id, ct));

    public IJiraReader CreateReader(JiraConnection connection) =>
        jiraFactory.CreateReader(connection.BaseUrl, tokenProtector.Unprotect(connection.EncryptedToken));

    private static string? Suggest(IReadOnlyList<JiraField> fields, string[] names)
    {
        foreach (var name in names)
        {
            var match = fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }
        return null;
    }

    /// <returns>Нормализованный адрес Jira.</returns>
    private static string Validate(ConnectionInput input, bool requireToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!JiraUrl.TryParse(input.BaseUrl, out var parsed))
            errors["baseUrl"] = ["Укажите адрес Jira вида https://jira.company.local."];
        if (requireToken && NormalizeToken(input.Token) is null) errors["token"] = ["Укажите Personal Access Token."];
        if (NormalizeKeys(input.ProjectKeys).Count == 0) errors["projectKeys"] = ["Выберите хотя бы один проект Jira."];
        if (input.HoursPerDay is < 1 or > 24) errors["hoursPerDay"] = ["Часов в дне — от 1 до 24."];
        if (errors.Count > 0) throw new ValidationFailedException("Проверьте параметры подключения.", errors);
        return parsed.BaseUrl;
    }

    /// <summary>Токен часто копируют вместе с префиксом «Bearer».</summary>
    private static string? NormalizeToken(string? token)
    {
        var value = token?.Trim();
        if (value is not null && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            value = value["Bearer ".Length..].Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string NameOrDefault(ConnectionInput input, string baseUrl) =>
        string.IsNullOrWhiteSpace(input.Name) ? DefaultName(baseUrl, NormalizeKeys(input.ProjectKeys)) : input.Name.Trim();

    /// <summary>«jira.company.local · PRJ, OPS» — если название не задано.</summary>
    private static string DefaultName(string baseUrl, IReadOnlyList<string> projectKeys)
    {
        var server = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : baseUrl;
        if (projectKeys.Count == 0) return server;
        var keys = string.Join(", ", projectKeys.Take(3));
        return projectKeys.Count > 3 ? $"{server} · {keys} +{projectKeys.Count - 3}" : $"{server} · {keys}";
    }

    private static List<string> NormalizeKeys(IEnumerable<string>? keys) =>
        (keys ?? [])
        .Select(k => k.Trim().ToUpperInvariant())
        .Where(k => k.Length > 0)
        .Distinct()
        .ToList();

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
