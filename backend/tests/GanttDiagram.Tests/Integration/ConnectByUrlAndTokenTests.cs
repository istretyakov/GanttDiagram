using GanttDiagram.Application.Dtos;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace GanttDiagram.Tests.Integration;

/// <summary>Подключение к Jira по адресу и токену: проверка до сохранения, выбор проекта и полей.</summary>
[Collection(ApiCollection.Name)]
public class ConnectByUrlAndTokenTests(ApiFixture api) : IDisposable
{
    private const string Token = "pat-123";

    public void Dispose() => api.Jira.Reset();

    private static void StubJira(WireMockServer jira, string token = Token)
    {
        var auth = $"Bearer {token}";
        jira.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet().WithHeader("Authorization", auth))
            .RespondWith(Response.Create().WithBodyAsJson(new { name = "ivanov", key = "ivanov", displayName = "Иван Иванов", active = true }));
        jira.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet().WithHeader("Authorization", auth))
            .RespondWith(Response.Create().WithBodyAsJson(new { version = "9.12.4", serverTitle = "Jira компании" }));
        jira.Given(Request.Create().WithPath("/rest/api/2/project").UsingGet().WithHeader("Authorization", auth))
            .RespondWith(Response.Create().WithBodyAsJson(new[]
            {
                new { key = "PRJ", name = "Основной проект" },
                new { key = "OPS", name = "Эксплуатация" },
            }));
        jira.Given(Request.Create().WithPath("/rest/api/2/field").UsingGet().WithHeader("Authorization", auth))
            .RespondWith(Response.Create().WithBodyAsJson(new object[]
            {
                new { id = "summary", name = "Summary", custom = false, schema = new { type = "string" } },
                new { id = "duedate", name = "Due date", custom = false, schema = new { type = "date" } },
                new { id = "customfield_10015", name = "Start date", custom = true, schema = new { type = "date" } },
                new { id = "customfield_10016", name = "Target end", custom = true, schema = new { type = "date" } },
            }));
        jira.Given(Request.Create().WithPath("/rest/api/2/issueLinkType").UsingGet().WithHeader("Authorization", auth))
            .RespondWith(Response.Create().WithBodyAsJson(new { issueLinkTypes = Array.Empty<object>() }));
        // Любой другой токен — 401.
        jira.Given(Request.Create().WithPath("/rest/api/2/*").UsingAnyMethod()).AtPriority(100)
            .RespondWith(Response.Create().WithStatusCode(401));
    }

    [Fact]
    public async Task Discover_ByIssueLink_FillsEverythingForTheForm()
    {
        StubJira(api.Jira);

        var result = await api.PostAsync<JiraDiscoverResult>("/api/connections/discover",
            new JiraDiscoverRequest($"{api.Jira.Url}/browse/prj-42", "Bearer " + Token, null));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(api.Jira.Url, result.BaseUrl);
        Assert.Equal("Иван Иванов", result.DisplayName);
        Assert.Equal("9.12.4", result.ServerVersion);
        Assert.Equal(["OPS", "PRJ"], result.Projects.Select(p => p.Key).Order());
        Assert.Equal("PRJ", result.SuggestedProjectKey);
        Assert.Equal("customfield_10015", result.SuggestedStartFieldId);
        Assert.Equal("customfield_10016", result.SuggestedEndFieldId);
        Assert.Equal(4, result.Fields.Count);
        Assert.Empty(result.Warnings);
        Assert.EndsWith("· PRJ", result.SuggestedName);
    }

    [Fact]
    public async Task Discover_WrongToken_ReturnsError()
    {
        StubJira(api.Jira);

        var result = await api.PostAsync<JiraDiscoverResult>("/api/connections/discover",
            new JiraDiscoverRequest(api.Jira.Url!, "wrong", null));

        Assert.False(result.Ok);
        Assert.Contains("401", result.Error);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public async Task Create_WithUrlTokenAndProjectOnly_ThenRediscoverWithStoredToken()
    {
        StubJira(api.Jira);

        // Только адрес (ссылкой на задачу), токен с префиксом Bearer и проект — название подставится.
        var connection = await api.PostAsync<ConnectionDto>("/api/connections",
            new ConnectionInput(null, $"{api.Jira.Url}/browse/PRJ-1", "Bearer " + Token, ["prj", "ops", "PRJ"], "customfield_10015", null, null));

        Assert.Equal(api.Jira.Url, connection.BaseUrl);
        Assert.Equal(["PRJ", "OPS"], connection.ProjectKeys);
        Assert.Equal("localhost · PRJ, OPS", connection.Name);
        Assert.Equal("duedate", connection.EndDateFieldId);

        var test = await api.PostAsync<ConnectionTestResult>($"/api/connections/{connection.Id}/test", new { });
        Assert.True(test.Ok, test.Error);

        // Редактирование: токен не вводят заново — берётся сохранённый.
        var rediscover = await api.PostAsync<JiraDiscoverResult>("/api/connections/discover",
            new JiraDiscoverRequest(connection.BaseUrl, null, connection.Id));
        Assert.True(rediscover.Ok, rediscover.Error);
    }
}
