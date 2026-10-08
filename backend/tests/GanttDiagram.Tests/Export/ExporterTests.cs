using GanttDiagram.Application.Export;
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Tests.Export;

/// <summary>
/// Snapshot-тесты экспорта: эталоны лежат в Export/Snapshots/*.verified.*.
/// Эталоны Mermaid дополнительно проверяет парсер mermaid во frontend-тестах.
/// </summary>
public class ExporterTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);

    private static ExportTask Task(string id, string key, string summary, int startDay, int endDay,
        string category = StatusCategories.New, string? epic = "PRJ-1", string? assignee = "Иванов") =>
        new(id, key, summary, new DateOnly(2026, 10, startDay), new DateOnly(2026, 10, endDay),
            "Статус", category, epic, assignee, $"https://jira.local/browse/{key}");

    private static GanttExportModel ProjectModel() => new(
        "Релиз 3.2", null, null, Today, [],
        [
            new ExportSection("Иванов",
            [
                Task("t1", "PRJ-12", "Авторизация", 5, 9, StatusCategories.InProgress),
                Task("t2", "PRJ-15", "Логи", 12, 14, epic: "PRJ-2"),
            ]),
            new ExportSection("Петров",
            [
                Task("t3", "PRJ-20", "Отчёты", 7, 16, StatusCategories.Done, assignee: "Петров"),
                Task("t4", "PRJ-21", "Просроченная задача", 1, 6, assignee: "Петров"),
            ]),
            new ExportSection("Пустая секция", []),
        ],
        [new ExportUnscheduled("PRJ-31", "Без дат"), new ExportUnscheduled("PRJ-33", "Тоже без дат")]);

    private static GanttExportModel EscapingModel() => new(
        "Проект: #1; [бета]", null, null, Today, [],
        [
            new ExportSection("Отдел: #2",
            [
                Task("t1", "PRJ-1", "Задача [важная]: этап #3; финал", 5, 5),
                Task("t2", "PRJ-2", "Многострочное\nназвание\r\nзадачи", 6, 7),
            ]),
        ],
        []);

    private static GanttExportModel EmptyModel() => new("Пустой проект", null, null, Today, [], [], []);

    private readonly PlantUmlGanttExporter _plantUml = new();
    private readonly MermaidGanttExporter _mermaid = new();

    [Fact]
    public Task PlantUml_Project() => Snapshot(_plantUml.Export(ProjectModel(), new ExportOptions()), "puml");

    [Fact]
    public Task Mermaid_Project() => Snapshot(_mermaid.Export(ProjectModel(), new ExportOptions()), "mmd");

    [Fact]
    public Task PlantUml_Escaping() => Snapshot(_plantUml.Export(EscapingModel(), new ExportOptions()), "puml");

    [Fact]
    public Task Mermaid_Escaping() => Snapshot(_mermaid.Export(EscapingModel(), new ExportOptions()), "mmd");

    [Fact]
    public Task PlantUml_Empty() => Snapshot(_plantUml.Export(EmptyModel(), new ExportOptions()), "puml");

    [Fact]
    public Task Mermaid_Empty() => Snapshot(_mermaid.Export(EmptyModel(), new ExportOptions()), "mmd");

    [Fact]
    public Task Mermaid_LabelWithAssignee_NoWeekendsExclusion_NoToday() =>
        Snapshot(_mermaid.Export(ProjectModel(), new ExportOptions(ExportLabel.KeySummaryAssignee, false, false)), "mmd");

    [Fact]
    public void Mermaid_EndDateIsExclusive_PlusOneDay()
    {
        var text = _mermaid.Export(ProjectModel(), new ExportOptions(ExportLabel.Key));
        // PRJ-12: 05.10–09.10 включительно → в Mermaid конец 10.10.
        Assert.Contains("PRJ-12 :active, t1, 2026-10-05, 2026-10-10", text);
    }

    [Fact]
    public void PlantUml_EndDateIsInclusive_NoShift()
    {
        var text = _plantUml.Export(ProjectModel(), new ExportOptions(ExportLabel.Key));
        Assert.Contains("[PRJ-12] as [T1] starts 2026-10-05 and ends 2026-10-09", text);
    }

    [Fact]
    public void Mermaid_Tags_DoneActiveCrit()
    {
        var text = _mermaid.Export(ProjectModel(), new ExportOptions(ExportLabel.Key));
        Assert.Contains("PRJ-20 :done, t3,", text);
        Assert.Contains("PRJ-21 :crit, t4,", text);  // конец 06.10 < сегодня 08.10, не выполнена
        Assert.Contains("PRJ-15 :t2,", text);
    }

    [Fact]
    public void UnscheduledTasks_GoToComment()
    {
        Assert.Contains("' Не запланировано: PRJ-31, PRJ-33", _plantUml.Export(ProjectModel(), new ExportOptions()));
        Assert.Contains("%% Не запланировано: PRJ-31, PRJ-33", _mermaid.Export(ProjectModel(), new ExportOptions()));
    }

    [Fact]
    public void EpicColors_AreStable()
    {
        Assert.Equal(EpicPalette.ColorFor("PRJ-1"), EpicPalette.ColorFor("PRJ-1"));
        Assert.Equal(EpicPalette.NoEpicColor, EpicPalette.ColorFor(null));
        // Значение фиксировано: тот же хеш считается во frontend (epicPalette.test.ts).
        Assert.Equal(1_665_203_501u, EpicPalette.Hash("PRJ-1"));
    }

    private static Task Snapshot(string text, string extension) =>
        Verify(text, extension).UseDirectory("Snapshots");
}
