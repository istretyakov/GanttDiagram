namespace GanttDiagram.Domain.Entities;

/// <summary>Подключение к Jira Server / Data Center (только чтение на этапе 1).</summary>
public class JiraConnection
{
    public const string DefaultEndDateField = "duedate";
    public const int DefaultHoursPerDay = 8;

    public Guid Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Например, https://jira.company.local (без завершающего слеша).</summary>
    public required string BaseUrl { get; set; }

    /// <summary>Personal Access Token, зашифрованный через Data Protection.</summary>
    public required string EncryptedToken { get; set; }

    /// <summary>Ключи проектов (пространств) Jira, из которых берутся эпики и пользователи.</summary>
    public List<string> ProjectKeys { get; set; } = [];

    /// <summary>Поле «ожидаемое начало», например customfield_10015. Стандартного поля в Jira нет.</summary>
    public string? StartDateFieldId { get; set; }

    /// <summary>Поле «ожидаемое окончание», по умолчанию duedate.</summary>
    public string EndDateFieldId { get; set; } = DefaultEndDateField;

    public int HoursPerDay { get; set; } = DefaultHoursPerDay;

    /// <summary>Связи Jira, которые образуют иерархию задач: строки «Тип связи|outward|inward».</summary>
    public List<string> HierarchyLinks { get; set; } = [];

    public IReadOnlyList<HierarchyRule> HierarchyRules =>
        HierarchyLinks.Select(HierarchyRule.Parse).OfType<HierarchyRule>().ToList();
    public DateTimeOffset CreatedAt { get; set; }

    public string BrowseUrl(string issueKey) => $"{BaseUrl.TrimEnd('/')}/browse/{issueKey}";
}
