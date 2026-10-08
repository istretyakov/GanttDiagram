using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;
using WireMock.Server;

namespace GanttDiagram.Tests.Integration;

/// <summary>PostgreSQL в Testcontainers + WireMock.Net в роли Jira Server + API в памяти.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), "gantt-tests-keys-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    public WireMockServer Jira { get; private set; } = null!;

    /// <summary>Отдельный экземпляр API с дополнительными настройками (та же БД).</summary>
    public WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            b.UseSetting("DataProtection:KeysPath", _keysPath);
            foreach (var (key, value) in settings) b.UseSetting(key, value);
        });
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Jira = WireMockServer.Start();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
            b.UseSetting("DataProtection:KeysPath", _keysPath);
        });
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_factory is not null) await _factory.DisposeAsync();
        Jira.Stop();
        await _postgres.DisposeAsync();
        try { Directory.Delete(_keysPath, recursive: true); } catch (IOException) { }
    }

    public async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public async Task<T> PostAsync<T>(string url, object body) =>
        await ReadAsync<T>(await Client.PostAsJsonAsync(url, body, Json));

    public async Task<T> GetAsync<T>(string url) =>
        await ReadAsync<T>(await Client.GetAsync(url));
}

[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
