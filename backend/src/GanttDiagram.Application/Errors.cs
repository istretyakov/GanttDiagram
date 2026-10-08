namespace GanttDiagram.Application;

public class NotFoundException(string message) : Exception(message)
{
    public static NotFoundException For(string entity, object id) => new($"{entity} «{id}» не найден(а).");
}

/// <summary>Ошибка проверки данных. Ключ словаря — поле или идентификатор задачи.</summary>
public class ValidationFailedException(string message, IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

/// <summary>Конфликт оптимистичной блокировки: задачи изменены в другой вкладке.</summary>
public class ConflictException(string message, IReadOnlyList<Guid> issueIds) : Exception(message)
{
    public IReadOnlyList<Guid> IssueIds { get; } = issueIds;
}
