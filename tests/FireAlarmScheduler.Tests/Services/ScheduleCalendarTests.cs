// Purpose: Tests the Monday-based week math and date ranges used by the schedule pages.
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Tests.Services;

public class ScheduleCalendarTests
{
    [Theory]
    [InlineData("2026-11-02", "2026-11-02")] // Monday
    [InlineData("2026-11-04", "2026-11-02")] // Wednesday
    [InlineData("2026-11-08", "2026-11-02")] // Sunday belongs to the week that started Monday
    [InlineData("2027-01-01", "2026-12-28")] // across a year boundary
    public void StartOfWeek_ReturnsMondayOnOrBefore(string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), ScheduleCalendar.StartOfWeek(DateOnly.Parse(date)));
    }

    [Fact]
    public void DaysBetween_IncludesBothEnds()
    {
        var days = ScheduleCalendar.DaysBetween(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 11, 8), days[^1]);
    }
}
