using GanttDiagram.Application;
using GanttDiagram.Application.Jira;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GanttDiagram.Api;

/// <summary>Переводит исключения приложения в ProblemDetails.</summary>
public class ExceptionToProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    ILogger<ExceptionToProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails? problem = exception switch
        {
            NotFoundException e => new ProblemDetails { Status = 404, Title = "Не найдено", Detail = e.Message },
            ValidationFailedException e => new HttpValidationProblemDetails(e.Errors)
            {
                Status = 400, Title = "Ошибка проверки данных", Detail = e.Message,
            },
            ConflictException e => new ProblemDetails
            {
                Status = 409, Title = "Конфликт изменений", Detail = e.Message,
                Extensions = { ["issueIds"] = e.IssueIds },
            },
            JiraException e => new ProblemDetails
            {
                Status = 502, Title = "Ошибка Jira", Detail = e.Message,
                Extensions = { ["jiraStatus"] = e.StatusCode },
            },
            BadHttpRequestException e => new ProblemDetails { Status = e.StatusCode, Title = "Некорректный запрос", Detail = e.Message },
            _ => null,
        };
        if (problem is null) return false;

        if (exception is JiraException)
            logger.LogWarning(exception, "Ошибка обращения к Jira");

        context.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
