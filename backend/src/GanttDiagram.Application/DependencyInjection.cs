using GanttDiagram.Application.Export;
using GanttDiagram.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GanttDiagram.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ConnectionService>();
        services.AddScoped<ImportService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<GanttService>();
        services.AddScoped<WorkloadService>();
        services.AddScoped<UserService>();
        services.AddScoped<ExportService>();
        services.AddScoped<JiraPushService>();
        services.AddSingleton<IGanttExporter, PlantUmlGanttExporter>();
        services.AddSingleton<IGanttExporter, MermaidGanttExporter>();
        return services;
    }
}
