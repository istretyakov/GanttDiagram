using System.Text.Json;
using System.Text.Json.Serialization;
using GanttDiagram.Api;
using GanttDiagram.Api.Endpoints;
using GanttDiagram.Application;
using GanttDiagram.Infrastructure;
using GanttDiagram.Infrastructure.Jira;
using GanttDiagram.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    // Без этого OpenAPI описывает числа как "integer | string", и в TS-типах получается number | string.
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ExceptionToProblemDetailsHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

if (app.Services.GetRequiredService<IOptions<JiraOptions>>().Value.AllowInvalidCertificates)
{
    app.Logger.LogWarning("Проверка SSL-сертификата Jira отключена ({Variable}=false).", JiraOptions.SslVerifyVariable);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.MapOpenApi();
app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
        new HealthDto("ok", await db.Database.CanConnectAsync(ct)))
    .WithTags("Health");

app.MapConnectionEndpoints();
app.MapUserEndpoints();
app.MapProjectEndpoints();
app.MapGanttEndpoints();
app.MapExportEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapDevEndpoints();
}

app.Run();

public record HealthDto(string Status, bool Database);

public partial class Program;
