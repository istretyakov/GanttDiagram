using GanttDiagram.Application.Dtos;
using GanttDiagram.Domain.Entities;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using static GanttDiagram.Tests.Integration.JiraStub;

namespace GanttDiagram.Tests.Integration;

/// <summary>Иерархия задач по связям Jira: «includes» и «is tested by».</summary>
[Collection(ApiCollection.Name)]
public class IssueHierarchyImportTests(ApiFixture api) : IDisposable
{
    public void Dispose() => api.Jira.Reset();

    private static readonly HierarchyLinkDto[] Rules =
    [
        new("Includes", LinkDirection.Outward),
        new("Tests", LinkDirection.Inward),
    ];

    [Fact]
    public async Task Import_BuildsTree_AndPullsLinkedTestsOutsideEpic()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1",
        [
            new("PRJ-11", "История", "ivanov", "2026-10-05", "2026-10-09",
                Links: [Includes("PRJ-12"), TestedBy("QA-5"), TestedBy("QA-6"), Blocks("PRJ-13")]),
            new("PRJ-12", "Подзадача", "ivanov", null, null, Links: [IncludedIn("PRJ-11")]),
            new("PRJ-13", "Другая", null, null, null),
        ], pageSize: 100);
        // Тесты не в эпике — подтягиваются по связям одним запросом key in (...).
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost()
                .WithBody(new JsonPartialMatcher(new { jql = "key in (\"QA-5\", \"QA-6\")" })))
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                startAt = 0, total = 2,
                issues = new[]
                {
                    IssueJson(new StubIssue("QA-5", "Тест входа", "sidorova", null, null, Links: [TestsIssue("PRJ-11")])),
                    IssueJson(new StubIssue("QA-6", "Тест выхода", null, null, null, Links: [TestsIssue("PRJ-11")])),
                },
            }));

        var connection = await api.PostAsync<ConnectionDto>("/api/connections", new ConnectionInput(
            "Иерархия " + Guid.NewGuid().ToString("N")[..6], api.Jira.Url!, "pat", ["PRJ", "QA"], StartField, null, 8, Rules));
        Assert.Equal(Rules, connection.HierarchyLinks);
        var project = await api.PostAsync<ProjectSummaryDto>("/api/projects", new ProjectInput("Проект", null, connection.Id));

        var summary = await api.PostAsync<ImportSummary>($"/api/projects/{project.Id}/epics", new AttachEpicsRequest(["PRJ-1"]));
        Assert.Equal(5, summary.Created);

        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var byKey = gantt.Issues.ToDictionary(i => i.Key);
        var story = byKey["PRJ-11"];
        Assert.Null(story.ParentIssueId);
        Assert.Equal((story.Id, "includes"), (byKey["PRJ-12"].ParentIssueId!.Value, byKey["PRJ-12"].ParentLinkLabel));
        Assert.Equal((story.Id, "is tested by"), (byKey["QA-5"].ParentIssueId!.Value, byKey["QA-5"].ParentLinkLabel));
        Assert.Equal(story.Id, byKey["QA-6"].ParentIssueId);
        Assert.Null(byKey["PRJ-13"].ParentIssueId); // Blocks — не иерархия
        Assert.Equal("PRJ-1", byKey["QA-5"].EpicKey);  // эпик родителя

        // То же в карточке проекта.
        var projectDto = await api.GetAsync<ProjectDto>($"/api/projects/{project.Id}");
        Assert.Equal(story.Id, projectDto.Issues.Single(i => i.Key == "QA-5").ParentIssueId);

        // Без правил иерархии — плоский список (правила меняются в подключении).
        await api.Client.PutAsync($"/api/connections/{connection.Id}", System.Net.Http.Json.JsonContent.Create(
            new ConnectionInput(connection.Name, connection.BaseUrl, null, connection.ProjectKeys, StartField, null, 8, []),
            options: ApiFixture.Json));
        gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.All(gantt.Issues, i => Assert.Null(i.ParentIssueId));
    }

    [Fact]
    public async Task Discover_ReturnsLinkTypes_AndSuggestsHierarchy()
    {
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { name = "ivanov", displayName = "Иван", active = true }));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { version = "9.12.4" }));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issueLinkType").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                issueLinkTypes = new[]
                {
                    new { id = "1", name = "Blocks", outward = "blocks", inward = "is blocked by" },
                    new { id = "2", name = "Включение", outward = "включает в", inward = "включено в" },
                    new { id = "3", name = "Tests", outward = "tests", inward = "Tested by" },
                },
            }));

        var result = await api.PostAsync<JiraDiscoverResult>("/api/connections/discover",
            new JiraDiscoverRequest(api.Jira.Url!, "pat", null));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(3, result.LinkTypes.Count);
        Assert.Equal(
            [new HierarchyLinkDto("Включение", LinkDirection.Outward), new HierarchyLinkDto("Tests", LinkDirection.Inward)],
            result.SuggestedHierarchyLinks);
    }
}
