using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GanttDiagram.Application.Dtos;
using static GanttDiagram.Tests.Integration.JiraStub;

namespace GanttDiagram.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ImportAndGanttTests(ApiFixture api) : IDisposable
{
    public void Dispose() => api.Jira.Reset();

    private async Task<(ConnectionDto Connection, ProjectSummaryDto Project)> CreateProjectAsync()
    {
        var connection = await api.PostAsync<ConnectionDto>("/api/connections", new ConnectionInput(
            "Тест " + Guid.NewGuid().ToString("N")[..6], api.Jira.Url!, "secret-pat", ["prj"], StartField, null, 8));
        var project = await api.PostAsync<ProjectSummaryDto>("/api/projects",
            new ProjectInput("Проект", null, connection.Id));
        return (connection, project);
    }

    private static List<StubIssue> FiveIssues() =>
    [
        new("PRJ-11", "Первая", "ivanov", "2026-10-05", "2026-10-06"),
        new("PRJ-12", "Вторая", "petrov", "2026-10-07", "2026-10-09"),
        new("PRJ-13", "Третья", "ivanov", null, null),
        new("PRJ-14", "Четвёртая", null, "2026-10-12", "2026-10-12"),
        new("PRJ-15", "Пятая", "petrov", "2026-10-13", "2026-10-16", Category: "done"),
    ];

    [Fact]
    public async Task Connection_TokenIsNeverReturned()
    {
        var (connection, _) = await CreateProjectAsync();

        var raw = await api.Client.GetStringAsync($"/api/connections/{connection.Id}");

        Assert.True(connection.HasToken);
        Assert.DoesNotContain("secret-pat", raw);
        Assert.Equal(["PRJ"], connection.ProjectKeys);
    }

    [Fact]
    public async Task Import_WithPagination_CreatesIssuesUsersAndLinks()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 2); // 3 страницы
        var (connection, project) = await CreateProjectAsync();

        var summary = await api.PostAsync<ImportSummary>($"/api/connections/{connection.Id}/import",
            new ImportRequest(["prj-1", "PRJ-404"], project.Id));

        Assert.Equal(1, summary.Epics);
        Assert.Equal(5, summary.Created);
        Assert.Equal(2, summary.UsersUpserted);
        Assert.Equal(["PRJ-404"], summary.NotFoundEpics);

        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.Equal(5, gantt.Issues.Count);
        var first = gantt.Issues.Single(i => i.Key == "PRJ-11");
        Assert.Equal(new DateOnly(2026, 10, 5), first.PlanStartDate);
        Assert.Equal(new DateOnly(2026, 10, 6), first.JiraEndDate);
        Assert.Equal("User ivanov", first.PlanAssigneeName);
        Assert.Equal(28800, first.OriginalEstimateSeconds);
        Assert.Equal("PRJ-1", first.EpicKey);
        Assert.EndsWith("/browse/PRJ-11", first.JiraUrl);
        Assert.Single(gantt.Epics);

        var users = await api.GetAsync<List<UserDto>>($"/api/users?connectionId={connection.Id}");
        Assert.Equal(["ivanov", "petrov"], users.Select(u => u.Name).Order());
    }

    [Fact]
    public async Task Resync_KeepsLocalEdits_UpdatesSnapshot()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 100);
        var (connection, project) = await CreateProjectAsync();
        await api.PostAsync<ImportSummary>($"/api/projects/{project.Id}/epics", new AttachEpicsRequest(["PRJ-1"]));

        var users = await api.GetAsync<List<UserDto>>($"/api/users?connectionId={connection.Id}");
        var petrov = users.Single(u => u.Name == "petrov");
        await api.PostAsync<MemberDto>($"/api/projects/{project.Id}/members", new AddMemberRequest(petrov.Id));

        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var edited = gantt.Issues.Single(i => i.Key == "PRJ-11");
        var save = await api.Client.PutAsJsonAsync($"/api/projects/{project.Id}/gantt", new GanttSaveRequest(
        [
            new GanttChange(edited.Id, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21), petrov.Id, edited.Version),
        ]), ApiFixture.Json);
        Assert.Equal(1, (await api.ReadAsync<GanttSaveResult>(save)).Saved);

        // В Jira сдвинули обе задачи.
        api.Jira.Reset();
        api.Jira.Epic("PRJ-1", "Эпик (новое имя)");
        var moved = FiveIssues();
        moved[0] = moved[0] with { Start = "2026-11-02", End = "2026-11-03", Updated = "2026-10-08T10:00:00.000+0300" };
        moved[1] = moved[1] with { Start = "2026-11-04", End = "2026-11-05", Updated = "2026-10-08T10:00:00.000+0300" };
        api.Jira.EpicChildren("PRJ-1", moved, pageSize: 100);

        var summary = await api.PostAsync<ImportSummary>($"/api/projects/{project.Id}/sync", new { });
        Assert.Equal(2, summary.Updated);
        Assert.Equal(3, summary.Skipped);
        Assert.Equal(1, summary.DiffersFromJira);

        gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var keptLocal = gantt.Issues.Single(i => i.Key == "PRJ-11");
        Assert.Equal(new DateOnly(2026, 10, 19), keptLocal.PlanStartDate);
        Assert.Equal(petrov.Id, keptLocal.PlanAssigneeId);
        Assert.Equal(new DateOnly(2026, 11, 2), keptLocal.JiraStartDate);
        Assert.True(keptLocal.DiffersFromJira);
        Assert.True(keptLocal.IsLocallyModified);

        var followsJira = gantt.Issues.Single(i => i.Key == "PRJ-12");
        Assert.Equal(new DateOnly(2026, 11, 4), followsJira.PlanStartDate);
        Assert.False(followsJira.DiffersFromJira);
        Assert.Equal("Эпик (новое имя)", gantt.Epics.Single().Summary);

        // «Сбросить к Jira»
        var reset = await api.PostAsync<IssueDto>($"/api/issues/{keptLocal.Id}/reset-to-jira", new { });
        Assert.Equal(new DateOnly(2026, 11, 2), reset.PlanStartDate);
        Assert.False(reset.DiffersFromJira);
    }

    [Fact]
    public async Task Save_StaleVersion_Returns409_AndSavesNothing()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 100);
        var (connection, project) = await CreateProjectAsync();
        await api.PostAsync<ImportSummary>($"/api/connections/{connection.Id}/import", new ImportRequest(["PRJ-1"], project.Id));
        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var a = gantt.Issues.Single(i => i.Key == "PRJ-11");
        var b = gantt.Issues.Single(i => i.Key == "PRJ-12");

        // Первая вкладка сохранила задачу A.
        var first = await api.Client.PutAsJsonAsync($"/api/projects/{project.Id}/gantt", new GanttSaveRequest(
            [new GanttChange(a.Id, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 20), null, a.Version)]), ApiFixture.Json);
        first.EnsureSuccessStatusCode();

        // Вторая вкладка со старой версией A и правкой B.
        var second = await api.Client.PutAsJsonAsync($"/api/projects/{project.Id}/gantt", new GanttSaveRequest(
        [
            new GanttChange(b.Id, new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), null, b.Version),
            new GanttChange(a.Id, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21), null, a.Version),
        ]), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(a.Id.ToString(), problem.GetProperty("issueIds")[0].GetString());

        gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.Equal(new DateOnly(2026, 10, 20), gantt.Issues.Single(i => i.Key == "PRJ-11").PlanStartDate);
        Assert.Equal(new DateOnly(2026, 10, 7), gantt.Issues.Single(i => i.Key == "PRJ-12").PlanStartDate);
    }

    [Fact]
    public async Task Save_AssigneeNotMember_Returns400_PerIssueErrors()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 100);
        var (connection, project) = await CreateProjectAsync();
        await api.PostAsync<ImportSummary>($"/api/connections/{connection.Id}/import", new ImportRequest(["PRJ-1"], project.Id));
        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var issue = gantt.Issues.Single(i => i.Key == "PRJ-11");
        var other = gantt.Issues.Single(i => i.Key == "PRJ-12");

        var response = await api.Client.PutAsJsonAsync($"/api/projects/{project.Id}/gantt", new GanttSaveRequest(
        [
            new GanttChange(other.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), null, other.Version),
            new GanttChange(issue.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 4), issue.PlanAssigneeId, issue.Version),
        ]), ApiFixture.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var messages = problem.GetProperty("errors").GetProperty(issue.Id.ToString()).EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("Исполнитель не является участником проекта.", messages);
        Assert.Contains("Дата окончания раньше даты начала.", messages);

        // Всё или ничего: валидная правка тоже не сохранилась.
        gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.Equal(new DateOnly(2026, 10, 7), gantt.Issues.Single(i => i.Key == "PRJ-12").PlanStartDate);
    }

    [Fact]
    public async Task Members_CannotRemoveWithIssues_Reorder()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 100);
        var (connection, project) = await CreateProjectAsync();
        await api.PostAsync<ImportSummary>($"/api/connections/{connection.Id}/import", new ImportRequest(["PRJ-1"], project.Id));
        var users = await api.GetAsync<List<UserDto>>($"/api/users?connectionId={connection.Id}");
        var ivanov = users.Single(u => u.Name == "ivanov");
        var petrov = users.Single(u => u.Name == "petrov");
        await api.PostAsync<MemberDto>($"/api/projects/{project.Id}/members", new AddMemberRequest(ivanov.Id));
        await api.PostAsync<MemberDto>($"/api/projects/{project.Id}/members", new AddMemberRequest(petrov.Id));

        var remove = await api.Client.DeleteAsync($"/api/projects/{project.Id}/members/{ivanov.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, remove.StatusCode);

        (await api.Client.PutAsJsonAsync($"/api/projects/{project.Id}/members/order",
            new MembersOrderRequest([petrov.Id, ivanov.Id]), ApiFixture.Json)).EnsureSuccessStatusCode();
        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.Equal([petrov.Id, ivanov.Id], gantt.Members.Select(m => m.UserId));
    }

    [Fact]
    public async Task Workload_AcrossProjects_AndExport()
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", FiveIssues(), pageSize: 100);
        var (connection, project) = await CreateProjectAsync();
        await api.PostAsync<ImportSummary>($"/api/connections/{connection.Id}/import", new ImportRequest(["PRJ-1"], project.Id));
        var second = await api.PostAsync<ProjectSummaryDto>("/api/projects", new ProjectInput("Второй", null, connection.Id));
        await api.PostAsync<AttachIssuesResult>($"/api/projects/{second.Id}/issues", new AttachIssuesRequest(["PRJ-11"]));
        var ivanov = (await api.GetAsync<List<UserDto>>($"/api/users?connectionId={connection.Id}")).Single(u => u.Name == "ivanov");

        var workload = await api.GetAsync<WorkloadDto>($"/api/users/{ivanov.Id}/workload?from=2026-10-05&to=2026-10-11");

        var task = Assert.Single(workload.Tasks);
        Assert.Equal("PRJ-11", task.Issue.Key);
        Assert.Equal(["Второй", "Проект"], task.Projects.Select(p => p.Name));
        Assert.Equal("PRJ-13", Assert.Single(workload.Unscheduled).Issue.Key);
        Assert.Equal(8, workload.TotalHours); // 8 ч на 2 дня
        Assert.Equal(40, workload.CapacityHours);
        Assert.Equal(20, workload.LoadPercent);

        var export = await api.GetAsync<ExportResult>($"/api/users/{ivanov.Id}/workload/export?format=mermaid&from=2026-10-05&to=2026-10-11");
        Assert.Contains("section Второй", export.Content);
        Assert.Contains("2026-10-05, 2026-10-07", export.Content);
        Assert.EndsWith(".mmd", export.FileName);
    }

    [Fact]
    public async Task Jira_Unauthorized_Returns502WithMessage()
    {
        api.Jira.Given(WireMock.RequestBuilders.Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(401));
        var (connection, _) = await CreateProjectAsync();

        var result = await api.PostAsync<ConnectionTestResult>($"/api/connections/{connection.Id}/test", new { });
        Assert.False(result.Ok);
        Assert.Contains("401", result.Error);

        var fields = await api.Client.GetAsync($"/api/connections/{connection.Id}/fields");
        Assert.Equal(HttpStatusCode.BadGateway, fields.StatusCode);
    }
}
