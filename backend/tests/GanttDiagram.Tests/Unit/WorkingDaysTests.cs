using GanttDiagram.Domain;

namespace GanttDiagram.Tests.Unit;

public class WorkingDaysTests
{
    // 2026-10-05 — понедельник.
    private static readonly DateOnly Monday = new(2026, 10, 5);

    [Theory]
    [InlineData(0, 0, 1)]  // пн–пн
    [InlineData(0, 4, 5)]  // пн–пт
    [InlineData(0, 6, 5)]  // пн–вс
    [InlineData(5, 6, 0)]  // сб–вс
    [InlineData(4, 7, 2)]  // пт–пн
    [InlineData(0, 13, 10)]
    [InlineData(2, 22, 15)]
    public void Count_IsInclusive_AndSkipsWeekends(int fromOffset, int toOffset, int expected) =>
        Assert.Equal(expected, WorkingDays.Count(Monday.AddDays(fromOffset), Monday.AddDays(toOffset)));

    [Fact]
    public void Count_EndBeforeStart_IsZero() =>
        Assert.Equal(0, WorkingDays.Count(Monday.AddDays(3), Monday));

    [Theory]
    [InlineData(null, 1)]
    [InlineData(0L, 1)]
    [InlineData(3600L, 1)]          // 1 ч
    [InlineData(8 * 3600L, 1)]      // 8 ч
    [InlineData(9 * 3600L, 2)]      // 9 ч
    [InlineData(16 * 3600L, 2)]
    [InlineData(40 * 3600L, 5)]
    public void DurationFromEstimate_CeilsToWholeDays(long? seconds, int expectedDays) =>
        Assert.Equal(expectedDays, WorkingDays.DurationFromEstimate(seconds, 8));

    [Theory]
    [InlineData(0, 1, 0)]   // пн, 1 день → пн
    [InlineData(0, 5, 4)]   // пн, 5 дней → пт
    [InlineData(3, 3, 7)]   // чт, 3 дня → чт, пт, пн
    [InlineData(4, 2, 7)]   // пт, 2 дня → пт, пн
    [InlineData(5, 2, 8)]   // сб, 2 дня → пн, вт
    [InlineData(0, 10, 11)] // две недели
    public void EndFor_SkipsWeekends(int startOffset, int days, int expectedEndOffset) =>
        Assert.Equal(Monday.AddDays(expectedEndOffset), WorkingDays.EndFor(Monday.AddDays(startOffset), days));
}
