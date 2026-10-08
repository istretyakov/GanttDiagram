using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GanttDiagram.Tests.Integration;

/// <summary>Ответы Jira Server REST API v2 для WireMock.</summary>
public static class JiraStub
{
    public const string StartField = "customfield_10015";

    public record StubIssue(string Key, string Summary, string? Assignee, string? Start, string? End,
        long? EstimateSeconds = 28800, string Updated = "2026-10-01T12:00:00.000+0300", string Category = "new",
        StubLink[]? Links = null);

    /// <summary>Связь с точки зрения задачи: outward — «эта &lt;outward&gt; другая».</summary>
    public record StubLink(string Type, string Outward, string Inward, bool IsOutward, string Key);

    public static readonly Func<string, StubLink> Includes = key => new("Includes", "includes", "is included in", true, key);
    public static readonly Func<string, StubLink> IncludedIn = key => new("Includes", "includes", "is included in", false, key);
    public static readonly Func<string, StubLink> TestedBy = key => new("Tests", "tests", "is tested by", false, key);
    public static readonly Func<string, StubLink> TestsIssue = key => new("Tests", "tests", "is tested by", true, key);
    public static readonly Func<string, StubLink> Blocks = key => new("Blocks", "blocks", "is blocked by", true, key);

    public static object IssueJson(StubIssue i) => new
    {
        key = i.Key,
        fields = new Dictionary<string, object?>
        {
            ["summary"] = i.Summary,
            ["status"] = new { name = "Статус", statusCategory = new { key = i.Category } },
            ["assignee"] = i.Assignee is null
                ? null
                : new { name = i.Assignee, key = i.Assignee, displayName = "User " + i.Assignee, emailAddress = $"{i.Assignee}@x.local", active = true },
            ["timeoriginalestimate"] = i.EstimateSeconds,
            ["duedate"] = i.End,
            [StartField] = i.Start,
            ["updated"] = i.Updated,
            ["issuelinks"] = (i.Links ?? []).Select(l => new Dictionary<string, object>
            {
                ["type"] = new { name = l.Type, outward = l.Outward, inward = l.Inward },
                [l.IsOutward ? "outwardIssue" : "inwardIssue"] = new { key = l.Key },
            }).ToList(),
        },
    };

    public static void Epic(this WireMockServer jira, string key, string summary) =>
        jira.Given(Request.Create().WithPath($"/rest/api/2/issue/{key}").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(IssueJson(new StubIssue(key, summary, null, null, null))));

    /// <summary>Дочерние задачи эпика, разбитые на страницы по <paramref name="pageSize"/>.</summary>
    public static void EpicChildren(this WireMockServer jira, string epicKey, IReadOnlyList<StubIssue> issues, int pageSize)
    {
        for (var startAt = 0; startAt < Math.Max(1, issues.Count); startAt += pageSize)
        {
            var page = issues.Skip(startAt).Take(pageSize).Select(IssueJson).ToList();
            jira.Given(Request.Create()
                    .WithPath("/rest/api/2/search")
                    .UsingPost()
                    .WithBody(new JsonPartialMatcher(new { startAt, jql = $"\"Epic Link\" = \"{epicKey}\" ORDER BY key ASC" })))
                .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
                {
                    startAt,
                    maxResults = pageSize,
                    total = issues.Count,
                    issues = page,
                }));
        }
    }
}
