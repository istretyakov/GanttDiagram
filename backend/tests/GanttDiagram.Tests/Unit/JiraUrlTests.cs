using GanttDiagram.Application.Jira;

namespace GanttDiagram.Tests.Unit;

public class JiraUrlTests
{
    [Theory]
    [InlineData("https://jira.company.local", "https://jira.company.local", null)]
    [InlineData("https://jira.company.local/", "https://jira.company.local", null)]
    [InlineData("  jira.company.local  ", "https://jira.company.local", null)]
    [InlineData("http://jira.local:8080/jira/", "http://jira.local:8080/jira", null)]
    [InlineData("https://jira.company.local/browse/PRJ-123", "https://jira.company.local", "PRJ")]
    [InlineData("https://jira.company.local/jira/browse/prj-7?focusedCommentId=1", "https://jira.company.local/jira", "PRJ")]
    [InlineData("https://jira.company.local/browse/PRJ", "https://jira.company.local", "PRJ")]
    [InlineData("https://jira.company.local/projects/ABC/issues/ABC-1", "https://jira.company.local", "ABC")]
    [InlineData("https://jira.company.local/secure/RapidBoard.jspa?rapidView=5&projectKey=XYZ", "https://jira.company.local", "XYZ")]
    [InlineData("https://jira.company.local/secure/Dashboard.jspa", "https://jira.company.local", null)]
    public void TryParse_ExtractsBaseUrlAndProject(string input, string baseUrl, string? projectKey)
    {
        Assert.True(JiraUrl.TryParse(input, out var parsed));
        Assert.Equal(baseUrl, parsed.BaseUrl);
        Assert.Equal(projectKey, parsed.ProjectKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://jira.local")]
    [InlineData("https://")]
    public void TryParse_RejectsInvalid(string? input) =>
        Assert.False(JiraUrl.TryParse(input, out _));
}
