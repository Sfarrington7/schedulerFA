// Purpose: Date helpers shared by the schedule pages: today's date, Monday-based weeks, and month grids.
namespace FireAlarmScheduler.Services;

public static class ScheduleCalendar
{
    public static DateOnly Today(TimeProvider timeProvider) => DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);

    /// <summary>Returns the Monday on or before <paramref name="date"/>.</summary>
    public static DateOnly StartOfWeek(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Returns every date from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public static List<DateOnly> DaysBetween(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();

    public static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>
    /// Returns the Monday–Sunday weeks a month calendar shows: from the Monday on or before the 1st
    /// to the Sunday on or after the last day, so leading/trailing days of nearby months are included.
    /// </summary>
    public static List<List<DateOnly>> MonthGrid(DateOnly anyDateInMonth)
    {
        var first = StartOfMonth(anyDateInMonth);
        var gridStart = StartOfWeek(first);
        var gridEnd = StartOfWeek(first.AddMonths(1).AddDays(-1)).AddDays(6);

        return DaysBetween(gridStart, gridEnd).Chunk(7).Select(week => week.ToList()).ToList();
    }
}
