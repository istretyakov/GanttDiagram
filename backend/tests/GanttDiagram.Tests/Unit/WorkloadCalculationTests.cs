using GanttDiagram.Application.Services;
using GanttDiagram.Domain.Entities;

namespace GanttDiagram.Tests.Unit;

public class WorkloadCalculationTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static Issue Task(int startOffset, int endOffset, double hours) => new()
    {
        Key = "PRJ-1",
        Summary = "x",
        PlanStartDate = Monday.AddDays(startOffset),
        PlanEndDate = Monday.AddDays(endOffset),
        OriginalEstimateSeconds = (long)(hours * 3600),
    };

    [Fact]
    public void Estimate_IsSpreadOverWorkingDaysOnly()
    {
        // чт–вт: 4 рабочих дня (чт, пт, пн, вт), 16 ч → по 4 ч.
        var days = WorkloadService.CalculateDays([Task(3, 8, 16)], Monday, Monday.AddDays(13), 8);

        Assert.Equal(4, days.Single(d => d.Date == Monday.AddDays(3)).Hours);
        Assert.Equal(0, days.Single(d => d.Date == Monday.AddDays(5)).Hours); // суббота
        Assert.Equal(0, days.Single(d => d.Date == Monday.AddDays(5)).Capacity);
        Assert.Equal(4, days.Single(d => d.Date == Monday.AddDays(8)).Hours);
        Assert.Equal(16, days.Sum(d => d.Hours));
    }

    [Fact]
    public void Overlapping_Tasks_MarkOverload()
    {
        var days = WorkloadService.CalculateDays([Task(0, 0, 6), Task(0, 1, 8)], Monday, Monday.AddDays(4), 8);

        var monday = days[0];
        Assert.Equal(10, monday.Hours);
        Assert.True(monday.Overloaded);
        Assert.False(days[1].Overloaded);
    }

    [Fact]
    public void Period_ClipsTask_ButKeepsPerDayRate()
    {
        // Задача пн–пт, 40 ч, период — только среда.
        var days = WorkloadService.CalculateDays([Task(0, 4, 40)], Monday.AddDays(2), Monday.AddDays(2), 8);

        Assert.Single(days);
        Assert.Equal(8, days[0].Hours);
    }

    [Fact]
    public void TaskWithoutEstimate_IsIgnored()
    {
        var task = Task(0, 2, 0);
        task.OriginalEstimateSeconds = null;

        var days = WorkloadService.CalculateDays([task], Monday, Monday.AddDays(4), 8);

        Assert.All(days, d => Assert.Equal(0, d.Hours));
    }
}
