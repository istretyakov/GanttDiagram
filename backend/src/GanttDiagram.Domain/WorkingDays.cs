namespace GanttDiagram.Domain;

/// <summary>Расчёт рабочих дней: Пн–Пт, без производственного календаря. Концы периодов включительные.</summary>
public static class WorkingDays
{
    public static bool IsWorkingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>Число рабочих дней в [start, end].</summary>
    public static int Count(DateOnly start, DateOnly end)
    {
        if (end < start) return 0;

        var days = end.DayNumber - start.DayNumber + 1;
        var fullWeeks = days / 7;
        var count = fullWeeks * 5;
        for (var d = start.AddDays(fullWeeks * 7); d <= end; d = d.AddDays(1))
        {
            if (IsWorkingDay(d)) count++;
        }
        return count;
    }

    /// <summary>
    /// Длительность в рабочих днях по оценке: ceil(часы / часов в дне), минимум 1 день.
    /// </summary>
    public static int DurationFromEstimate(long? estimateSeconds, int hoursPerDay)
    {
        if (estimateSeconds is null or <= 0 || hoursPerDay <= 0) return 1;
        var hours = estimateSeconds.Value / 3600.0;
        return Math.Max(1, (int)Math.Ceiling(hours / hoursPerDay - 1e-9));
    }

    /// <summary>
    /// Дата окончания (включительно) для задачи длительностью <paramref name="workingDays"/> рабочих дней,
    /// начинающейся в <paramref name="start"/>. Выходные внутри периода не считаются.
    /// </summary>
    public static DateOnly EndFor(DateOnly start, int workingDays)
    {
        if (workingDays <= 1) return start;

        var end = start;
        var counted = IsWorkingDay(start) ? 1 : 0;
        while (counted < workingDays)
        {
            end = end.AddDays(1);
            if (IsWorkingDay(end)) counted++;
        }
        return end;
    }

    public static IEnumerable<DateOnly> Range(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1)) yield return d;
    }
}
