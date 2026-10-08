using System.Text.RegularExpressions;

namespace GanttDiagram.Application.Jira;

/// <summary>
/// Разбор адреса Jira, вставленного пользователем: можно вставить любую ссылку из Jira
/// (задача, доска, проект) — из неё выделяется адрес сервера (с контекстным путём, например /jira)
/// и, если получится, ключ проекта.
/// </summary>
public static partial class JiraUrl
{
    public record Parsed(string BaseUrl, string? ProjectKey);

    public static bool TryParse(string? input, out Parsed result)
    {
        result = new Parsed("", null);
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
            return false;

        var path = uri.AbsolutePath;
        string? projectKey = null;

        // Всё, что начинается с известных путей Jira, — уже не часть адреса сервера.
        var marker = AppPath().Match(path);
        if (marker.Success)
        {
            var tail = path[marker.Index..];
            projectKey = IssueKey().Match(tail) is { Success: true } issue
                ? issue.Groups["project"].Value
                : ProjectInPath().Match(tail) is { Success: true } project ? project.Groups["project"].Value : null;
            path = path[..marker.Index];
        }

        // Ссылка на доску/фильтр: ?projectKey=PRJ
        projectKey ??= QueryProjectKey().Match(uri.Query) is { Success: true } q ? q.Groups["project"].Value : null;

        var baseUrl = $"{uri.Scheme}://{uri.Authority}{path}".TrimEnd('/');
        result = new Parsed(baseUrl, projectKey?.ToUpperInvariant());
        return true;
    }

    [GeneratedRegex(@"/(browse|secure|projects|issues|plugins|rest|login\.jsp|software|servicedesk)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AppPath();

    [GeneratedRegex(@"/browse/(?<project>[A-Za-z][A-Za-z0-9_]*)(-\d+)?", RegexOptions.IgnoreCase)]
    private static partial Regex IssueKey();

    [GeneratedRegex(@"/projects/(?<project>[A-Za-z][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectInPath();

    [GeneratedRegex(@"[?&]projectKey=(?<project>[A-Za-z][A-Za-z0-9_]*)", RegexOptions.IgnoreCase)]
    private static partial Regex QueryProjectKey();
}
