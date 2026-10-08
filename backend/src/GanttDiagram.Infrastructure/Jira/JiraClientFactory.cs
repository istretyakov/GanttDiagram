using System.Net.Http.Headers;
using GanttDiagram.Application.Jira;

namespace GanttDiagram.Infrastructure.Jira;

public class JiraOptions
{
    public const string Section = "Jira";
    public const string HttpClientName = "jira";

    /// <summary>Переменная окружения: JIRA_SSL_VERIFY=false отключает проверку сертификата Jira.</summary>
    public const string SslVerifyVariable = "JIRA_SSL_VERIFY";

    /// <summary>
    /// Отключить проверку TLS-сертификата Jira (самоподписанный или внутренний сертификат).
    /// Задаётся через JIRA_SSL_VERIFY=false или Jira:AllowInvalidCertificates=true.
    /// </summary>
    public bool AllowInvalidCertificates { get; set; }

    /// <summary>"false", "0", "no", "off" — проверка выключена.</summary>
    public static bool IsDisabled(string? value) =>
        value?.Trim().ToLowerInvariant() is "false" or "0" or "no" or "off";

    /// <summary>Адрес корпоративного прокси. Если не задан, используются системные настройки и HTTPS_PROXY.</summary>
    public string? Proxy { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

public class JiraClientFactory(IHttpClientFactory httpClientFactory) : IJiraClientFactory
{
    public IJiraReader CreateReader(string baseUrl, string token) => Create(baseUrl, token);

    public IJiraWriter CreateWriter(string baseUrl, string token) => Create(baseUrl, token);

    private JiraServerClient Create(string baseUrl, string token)
    {
        var http = httpClientFactory.CreateClient(JiraOptions.HttpClientName);
        http.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new JiraServerClient(http);
    }
}
