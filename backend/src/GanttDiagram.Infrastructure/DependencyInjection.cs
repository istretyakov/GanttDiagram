using System.Net;
using GanttDiagram.Application.Abstractions;
using GanttDiagram.Application.Jira;
using GanttDiagram.Infrastructure.Jira;
using GanttDiagram.Infrastructure.Persistence;
using GanttDiagram.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace GanttDiagram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Ключи шифрования токенов Jira лежат в папке на диске. Потеря ключей = токены нужно ввести заново.
        var keysPath = config["DataProtection:KeysPath"] ?? "keys";
        services.AddDataProtection()
            .SetApplicationName("GanttDiagram")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(keysPath)));
        services.AddSingleton<ITokenProtector, DataProtectionTokenProtector>();

        services.AddOptions<JiraOptions>()
            .Bind(config.GetSection(JiraOptions.Section))
            .PostConfigure(o =>
            {
                if (JiraOptions.IsDisabled(config[JiraOptions.SslVerifyVariable])) o.AllowInvalidCertificates = true;
            });
        services.AddHttpClient(JiraOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var options = sp.GetRequiredService<IOptions<JiraOptions>>().Value;
                var handler = new SocketsHttpHandler();
                if (options.AllowInvalidCertificates)
                    handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                if (!string.IsNullOrWhiteSpace(options.Proxy))
                {
                    handler.Proxy = new WebProxy(options.Proxy) { UseDefaultCredentials = true };
                    handler.UseProxy = true;
                }
                return handler;
            })
            .AddStandardResilienceHandler(o =>
            {
                // Повторы при 429 и 5xx (с учётом Retry-After) — поведение стандартного обработчика.
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                // Неизвестный хост — не временный сбой: повторы только затягивают ответ.
                o.Retry.ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    && args.Outcome.Exception is not HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError });
            });
        services.AddSingleton<IJiraClientFactory, JiraClientFactory>();

        services.AddScoped<DemoSeeder>();
        return services;
    }
}
