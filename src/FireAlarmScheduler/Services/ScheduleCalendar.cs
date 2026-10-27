// Purpose: Date helpers shared by the schedule pages: today's date and Monday-based weeks.
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
}
