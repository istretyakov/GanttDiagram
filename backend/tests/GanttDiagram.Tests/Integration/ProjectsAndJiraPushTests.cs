using System.Net.Http.Json;
using System.Text.Json;
using GanttDiagram.Application.Dtos;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using static GanttDiagram.Tests.Integration.JiraStub;

namespace GanttDiagram.Tests.Integration;

/// <summary>Несколько проектов Jira в подключении, копирование проектов, отправка сроков в Jira.</summary>
[Collection(ApiCollection.Name)]
public class ProjectsAndJiraPushTests(ApiFixture api) : IDisposable
{
    private const string Updated = "2026-10-01T12:00:00.000+0300";

    public void Dispose() => api.Jira.Reset();

    private static List<StubIssue> Issues() =>
    [
        new("PRJ-11", "Первая", "ivanov", "2026-10-05", "2026-10-06"),
        new("PRJ-12", "Вторая", "petrov", "2026-10-07", "2026-10-09"),
        new("PRJ-13", "Третья", "ivanov", "2026-10-12", "2026-10-12"),
    ];

    private async Task<(ConnectionDto Connection, ProjectSummaryDto Project, GanttDto Gantt)> ImportedProjectAsync(string[]? keys = null)
    {
        api.Jira.Epic("PRJ-1", "Эпик");
        api.Jira.EpicChildren("PRJ-1", Issues(), pageSize: 100);
        var connection = await api.PostAsync<ConnectionDto>("/api/connections", new ConnectionInput(
            "Тест " + Guid.NewGuid().ToString("N")[..6], api.Jira.Url!, "pat", keys ?? ["PRJ"], StartField, null, 8));
        var project = await api.PostAsync<ProjectSummaryDto>("/api/projects", new ProjectInput("Исходный", "Описание", connection.Id));
        await api.PostAsync<ImportSummary>($"/api/projects/{project.Id}/epics", new AttachEpicsRequest(["PRJ-1"]));
        var users = await api.GetAsync<List<UserDto>>($"/api/users?connectionId={connection.Id}");
        foreach (var user in users.OrderByDescending(u => u.Name))
            await api.PostAsync<MemberDto>($"/api/projects/{project.Id}/members", new AddMemberRequest(user.Id));
        var gantt = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        return (connection, project, gantt);
    }

    private async Task SaveAsync(Guid projectId, params GanttChange[] changes) =>
        await api.ReadAsync<GanttSaveResult>(await api.Client.PutAsJsonAsync(
            $"/api/projects/{projectId}/gantt", new GanttSaveRequest(changes), ApiFixture.Json));

    // ---------- несколько проектов

    [Fact]
    public async Task Connection_WithSeveralProjects_SearchesEpicsAndUsersInAll()
    {
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost()
                .WithBody(new JsonPartialMatcher(new { jql = "project in (\"PRJ\", \"OPS\") AND issuetype = Epic ORDER BY created DESC" })))
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                startAt = 0, total = 2,
                issues = new[] { IssueJson(new StubIssue("PRJ-1", "Эпик PRJ", null, null, null)), IssueJson(new StubIssue("OPS-7", "Эпик OPS", null, null, null)) },
            }));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/search").UsingPost()
                .WithBody(new JsonPartialMatcher(new { jql = "project in (\"OPS\") AND issuetype = Epic ORDER BY created DESC" })))
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                startAt = 0, total = 1, issues = new[] { IssueJson(new StubIssue("OPS-7", "Эпик OPS", null, null, null)) },
            }));
        foreach (var (project, user) in new[] { ("PRJ", "ivanov"), ("OPS", "sidorova") })
            api.Jira.Given(Request.Create().WithPath("/rest/api/2/user/assignable/search").WithParam("project", project).UsingGet())
                .RespondWith(Response.Create().WithBodyAsJson(new[]
                {
                    new { name = user, displayName = user, active = true },
                    new { name = "petrov", displayName = "petrov", active = true },
                }));

        var connection = await api.PostAsync<ConnectionDto>("/api/connections", new ConnectionInput(
            null, api.Jira.Url!, "pat", ["prj", "OPS"], null, null, null));

        var epics = await api.GetAsync<List<EpicSearchItem>>($"/api/connections/{connection.Id}/epics");
        Assert.Equal(["OPS-7", "PRJ-1"], epics.Select(e => e.Key).Order());

        var onlyOps = await api.GetAsync<List<EpicSearchItem>>($"/api/connections/{connection.Id}/epics?project=ops");
        Assert.Equal(["OPS-7"], onlyOps.Select(e => e.Key));

        var sync = await api.PostAsync<UsersSyncResult>($"/api/connections/{connection.Id}/users/sync", new { });
        Assert.Equal(3, sync.Total); // ivanov, sidorova, petrov — без дублей
    }

    [Fact]
    public async Task Connection_WithoutProjects_IsRejected()
    {
        var response = await api.Client.PostAsJsonAsync("/api/connections",
            new ConnectionInput("x", api.Jira.Url!, "pat", [" "], null, null, null), ApiFixture.Json);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- копирование проекта

    [Fact]
    public async Task Copy_CopiesEpicsIssuesAndMembersWithOrder()
    {
        var (_, source, gantt) = await ImportedProjectAsync();

        var copy = await api.PostAsync<ProjectSummaryDto>($"/api/projects/{source.Id}/copy", new CopyProjectRequest(null, null));

        Assert.Equal("Исходный (копия)", copy.Name);
        Assert.Equal("Описание", copy.Description);
        Assert.Equal((1, 3, 2), (copy.EpicCount, copy.IssueCount, copy.MemberCount));
        var copied = await api.GetAsync<GanttDto>($"/api/projects/{copy.Id}/gantt");
        Assert.Equal(gantt.Members.Select(m => m.UserId), copied.Members.Select(m => m.UserId));
        Assert.Equal(gantt.Issues.Select(i => i.Id).Order(), copied.Issues.Select(i => i.Id).Order());

        // Исходный проект не изменился и живёт отдельно.
        await api.Client.DeleteAsync($"/api/projects/{copy.Id}");
        Assert.Equal(3, (await api.GetAsync<GanttDto>($"/api/projects/{source.Id}/gantt")).Issues.Count);
    }

    [Fact]
    public async Task Copy_OnlyMembers_AsTemplate()
    {
        var (_, source, _) = await ImportedProjectAsync();

        var copy = await api.PostAsync<ProjectSummaryDto>($"/api/projects/{source.Id}/copy",
            new CopyProjectRequest("Шаблон", "", IncludeEpics: false, IncludeIssues: false, IncludeMembers: true));

        Assert.Equal("Шаблон", copy.Name);
        Assert.Null(copy.Description);
        Assert.Equal((0, 0, 2), (copy.EpicCount, copy.IssueCount, copy.MemberCount));
    }

    // ---------- отправка сроков в Jira

    [Fact]
    public async Task Push_WritesDates_DetectsConflicts_ReportsErrors()
    {
        var (_, project, gantt) = await ImportedProjectAsync();
        var i11 = gantt.Issues.Single(i => i.Key == "PRJ-11");
        var i12 = gantt.Issues.Single(i => i.Key == "PRJ-12");
        var i13 = gantt.Issues.Single(i => i.Key == "PRJ-13");
        var ivanov = gantt.Members.Single(m => m.Name == "ivanov").UserId;
        await SaveAsync(project.Id,
            new GanttChange(i11.Id, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21), i11.PlanAssigneeId, i11.Version),
            new GanttChange(i12.Id, i12.PlanStartDate, i12.PlanEndDate, ivanov, i12.Version),               // только исполнитель
            new GanttChange(i13.Id, new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13), i13.PlanAssigneeId, i13.Version));

        // Предпросмотр: без исполнителя PRJ-12 не попадает.
        var preview = await api.GetAsync<JiraPushPreview>($"/api/projects/{project.Id}/jira-push");
        Assert.Equal(["PRJ-11", "PRJ-13"], preview.Items.Select(i => i.Key));
        Assert.Equal(
            [new JiraPushChange("start", "2026-10-05", "2026-10-19"), new JiraPushChange("end", "2026-10-06", "2026-10-21")],
            preview.Items[0].Changes);
        var withAssignee = await api.GetAsync<JiraPushPreview>($"/api/projects/{project.Id}/jira-push?includeAssignee=true");
        Assert.Equal(new JiraPushChange("assignee", "User petrov", "User ivanov"), withAssignee.Items.Single(i => i.Key == "PRJ-12").Changes.Single());

        // PRJ-11: в Jira не менялась → запись; после PUT Jira отдаёт новые даты.
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-11").UsingGet())
            .InScenario("PRJ-11").WillSetStateTo("read")
            .RespondWith(Response.Create().WithBodyAsJson(IssueJson(Issues()[0])));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-11").UsingPut())
            .InScenario("PRJ-11").WhenStateIs("read").WillSetStateTo("written")
            .RespondWith(Response.Create().WithStatusCode(204));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-11").UsingGet())
            .InScenario("PRJ-11").WhenStateIs("written")
            .RespondWith(Response.Create().WithBodyAsJson(IssueJson(Issues()[0] with
            {
                Start = "2026-10-19", End = "2026-10-21", Updated = "2026-10-08T15:00:00.000+0300",
            })));
        // PRJ-12: исполнителя назначить нельзя.
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-12").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(IssueJson(Issues()[1])));
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-12/assignee").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(400).WithBodyAsJson(new { errors = new { assignee = "User 'ivanov' cannot be assigned issues." } }));
        // PRJ-13: кто-то поменял задачу в Jira после импорта.
        api.Jira.Given(Request.Create().WithPath("/rest/api/2/issue/PRJ-13").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(IssueJson(Issues()[2] with
            {
                End = "2026-10-14", Updated = "2026-10-07T09:00:00.000+0300",
            })));

        var result = await api.PostAsync<JiraPushResult>($"/api/projects/{project.Id}/jira-push", new JiraPushRequest(null, true));

        Assert.Equal((1, 1, 1), (result.Synced, result.Conflicts, result.Failed));
        Assert.Equal(JiraPushStatus.Synced, result.Items.Single(i => i.Key == "PRJ-11").Status);
        Assert.Equal(JiraPushStatus.Conflict, result.Items.Single(i => i.Key == "PRJ-13").Status);
        var failed = result.Items.Single(i => i.Key == "PRJ-12");
        Assert.Equal(JiraPushStatus.Failed, failed.Status);
        Assert.Contains("cannot be assigned", failed.Message);

        // В Jira ушли только поля дат PRJ-11, в формате date.
        var put = api.Jira.LogEntries.Single(e => e.RequestMessage.Method == "PUT" && e.RequestMessage.Path == "/rest/api/2/issue/PRJ-11");
        var fields = JsonDocument.Parse(put.RequestMessage.Body!).RootElement.GetProperty("fields");
        Assert.Equal("2026-10-19", fields.GetProperty(StartField).GetString());
        Assert.Equal("2026-10-21", fields.GetProperty("duedate").GetString());
        Assert.DoesNotContain(api.Jira.LogEntries, e => e.RequestMessage.Method == "PUT" && e.RequestMessage.Path.Contains("PRJ-13"));

        var after = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        var a11 = after.Issues.Single(i => i.Key == "PRJ-11");
        Assert.False(a11.DiffersFromJira);
        Assert.False(a11.IsLocallyModified);
        Assert.Equal(new DateOnly(2026, 10, 19), a11.JiraStartDate);
        // Конфликт: план сохранён, снимок Jira обновлён.
        var a13 = after.Issues.Single(i => i.Key == "PRJ-13");
        Assert.Equal(new DateOnly(2026, 10, 13), a13.PlanStartDate);
        Assert.Equal(new DateOnly(2026, 10, 14), a13.JiraEndDate);
        // Ошибка: ничего не поменялось.
        Assert.True(after.Issues.Single(i => i.Key == "PRJ-12").IsLocallyModified);

        // Повторная отправка конфликтной задачи после обновления снимка проходит проверку.
        var preview2 = await api.GetAsync<JiraPushPreview>($"/api/projects/{project.Id}/jira-push");
        Assert.Equal(["PRJ-13"], preview2.Items.Select(i => i.Key));
    }

    [Fact]
    public async Task Push_JiraUnreachable_FailsFastWithoutTouchingIssues()
    {
        var (connection, project, gantt) = await ImportedProjectAsync();
        var issue = gantt.Issues.Single(i => i.Key == "PRJ-11");
        await SaveAsync(project.Id,
            new GanttChange(issue.Id, new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 19), issue.PlanAssigneeId, issue.Version));
        // Хост в зоне .invalid гарантированно не резолвится.
        await api.Client.PutAsJsonAsync($"/api/connections/{connection.Id}", new ConnectionInput(
            connection.Name, "https://jira.invalid", null, connection.ProjectKeys, StartField, null, 8), ApiFixture.Json);

        var started = DateTime.UtcNow;
        var response = await api.Client.PostAsJsonAsync($"/api/projects/{project.Id}/jira-push",
            new JiraPushRequest(null, false), ApiFixture.Json);

        Assert.Equal(System.Net.HttpStatusCode.BadGateway, response.StatusCode);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20), "без повторов при неизвестном хосте");
        var after = await api.GetAsync<GanttDto>($"/api/projects/{project.Id}/gantt");
        Assert.True(after.Issues.Single(i => i.Key == "PRJ-11").IsLocallyModified);
    }

    [Fact]
    public async Task Push_Nothing_ReturnsEmpty()
    {
        var (_, project, _) = await ImportedProjectAsync();

        var result = await api.PostAsync<JiraPushResult>($"/api/projects/{project.Id}/jira-push", new JiraPushRequest(null, false));

        Assert.Empty(result.Items);
        Assert.DoesNotContain(api.Jira.LogEntries, e => e.RequestMessage.Method == "PUT");
    }
}
