using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GanttDiagram.Application.Dtos;
using GanttDiagram.Infrastructure.Jira;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace GanttDiagram.Tests.Integration;

/// <summary>Jira по HTTPS с самоподписанным сертификатом: JIRA_SSL_VERIFY=false отключает проверку.</summary>
[Collection(ApiCollection.Name)]
public class JiraSslVerifyTests(ApiFixture api) : IDisposable
{
    private readonly WireMockServer _httpsJira = StartHttpsJira();

    public void Dispose() => _httpsJira.Stop();

    private static WireMockServer StartHttpsJira()
    {
        var server = WireMockServer.Start(new WireMockServerSettings
        {
            UseSSL = true,
            CertificateSettings = new WireMockCertificateSettings { X509Certificate = UntrustedCertificate() },
        });
        server.Given(Request.Create().WithPath("/rest/api/2/myself").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { name = "ivanov", displayName = "Иван Иванов", active = true }));
        server.Given(Request.Create().WithPath("/rest/api/2/serverInfo").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new { version = "9.12.4" }));
        server.Given(Request.Create().WithPath("/rest/api/2/project").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new[] { new { key = "PRJ", name = "Проект" } }));
        server.Given(Request.Create().WithPath("/rest/api/2/field").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(Array.Empty<object>()));
        return server;
    }

    /// <summary>Самоподписанный сертификат, которому система не доверяет (как внутренний ЦС компании).</summary>
    private static X509Certificate2 UntrustedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Через PFX, чтобы закрытый ключ был доступен TLS-стеку Windows.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    private async Task<JiraDiscoverResult> DiscoverAsync(HttpClient client)
    {
        Assert.StartsWith("https://", _httpsJira.Url);
        var response = await client.PostAsJsonAsync("/api/connections/discover",
            new JiraDiscoverRequest(_httpsJira.Url!, "token", null), ApiFixture.Json);
        return await api.ReadAsync<JiraDiscoverResult>(response);
    }

    [Fact]
    public async Task ByDefault_SelfSignedCertificate_IsRejectedWithHint()
    {
        var result = await DiscoverAsync(api.Client);

        Assert.False(result.Ok);
        Assert.Contains("JIRA_SSL_VERIFY=false", result.Error);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("FALSE")]
    public async Task SslVerifyFalse_AcceptsSelfSignedCertificate(string value)
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?> { [JiraOptions.SslVerifyVariable] = value });
        using var client = factory.CreateClient();

        var result = await DiscoverAsync(client);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Иван Иванов", result.DisplayName);
        Assert.Equal(["PRJ"], result.Projects.Select(p => p.Key));
    }

    [Fact]
    public async Task SslVerifyTrue_KeepsVerification()
    {
        await using var factory = api.CreateFactory(new Dictionary<string, string?> { [JiraOptions.SslVerifyVariable] = "true" });
        using var client = factory.CreateClient();

        Assert.False((await DiscoverAsync(client)).Ok);
    }
}
