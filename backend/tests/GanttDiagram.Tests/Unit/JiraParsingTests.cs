using System.Text.Json;
using GanttDiagram.Application.Jira;
using GanttDiagram.Infrastructure.Jira;

namespace GanttDiagram.Tests.Unit;

public class JiraParsingTests
{
    private static readonly JiraFieldMapping Mapping = new("customfield_10015", "duedate");

    [Fact]
    public void ParseIssue_ReadsAllFields()
    {
        var json = JsonDocument.Parse("""
            {
              "key": "PRJ-12",
              "fields": {
                "summary": "Авторизация",
                "status": { "name": "В работе", "statusCategory": { "key": "indeterminate" } },
                "assignee": {
                  "name": "ivanov", "key": "JIRAUSER10100", "displayName": "Иван Иванов",
                  "emailAddress": "ivanov@company.local", "active": true,
                  "avatarUrls": { "48x48": "https://jira/avatar/48", "16x16": "https://jira/avatar/16" }
                },
                "timeoriginalestimate": 57600,
                "duedate": "2026-10-09",
                "customfield_10015": "2026-10-05",
                "updated": "2026-10-01T12:30:00.000+0300"
              }
            }
            """).RootElement;

        var issue = JiraServerClient.ParseIssue(json, Mapping);

        Assert.Equal("PRJ-12", issue.Key);
        Assert.Equal("Авторизация", issue.Summary);
        Assert.Equal(new JiraIssueStatus("В работе", "indeterminate"), issue.Status);
        Assert.Equal("ivanov", issue.Assignee!.Name);
        Assert.Equal("JIRAUSER10100", issue.Assignee.Key);
        Assert.Equal("https://jira/avatar/48", issue.Assignee.AvatarUrl);
        Assert.Equal(57600, issue.OriginalEstimateSeconds);
        Assert.Equal(new DateOnly(2026, 10, 5), issue.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 9), issue.EndDate);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero), issue.Updated);
    }

    [Fact]
    public void ParseIssue_NullFields()
    {
        var json = JsonDocument.Parse("""
            { "key": "PRJ-1", "fields": { "summary": "x", "status": null, "assignee": null,
              "timeoriginalestimate": null, "duedate": null, "updated": null } }
            """).RootElement;

        var issue = JiraServerClient.ParseIssue(json, Mapping);

        Assert.Null(issue.Assignee);
        Assert.Null(issue.OriginalEstimateSeconds);
        Assert.Null(issue.StartDate);
        Assert.Null(issue.EndDate);
        Assert.Null(issue.Updated);
        Assert.Equal("new", issue.Status.Category);
    }

    [Fact]
    public void ParseDate_DateTimeValue_TakesDatePart()
    {
        var json = JsonDocument.Parse("""{ "customfield_1": "2026-10-05T10:00:00.000+0300" }""").RootElement;
        Assert.Equal(new DateOnly(2026, 10, 5), JiraServerClient.ParseDate(json, "customfield_1"));
    }

    [Fact]
    public void Quote_EscapesJqlString() =>
        Assert.Equal("\"a \\\"b\\\" \\\\c\"", JiraServerClient.Quote("a \"b\" \\c"));
}
