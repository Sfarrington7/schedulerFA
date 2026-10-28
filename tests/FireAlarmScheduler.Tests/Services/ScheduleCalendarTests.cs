// Purpose: Tests the Monday-based week math, date ranges, and month grids used by the schedule pages.
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

    [Fact]
    public void MonthGrid_MonthStartingSunday_IncludesLeadingAndTrailingDays()
    {
        // November 2026 starts on a Sunday and ends on a Monday.
        var weeks = ScheduleCalendar.MonthGrid(new DateOnly(2026, 11, 17));

        Assert.Equal(6, weeks.Count);
        Assert.All(weeks, week => Assert.Equal(7, week.Count));
        Assert.Equal(new DateOnly(2026, 10, 26), weeks[0][0]);
        Assert.Equal(new DateOnly(2026, 11, 1), weeks[0][6]);
        Assert.Equal(new DateOnly(2026, 12, 6), weeks[^1][^1]);
        Assert.All(weeks, week => Assert.Equal(DayOfWeek.Monday, week[0].DayOfWeek));
    }

    [Fact]
    public void MonthGrid_FebruaryStartingMonday_IsExactlyFourWeeks()
    {
        var weeks = ScheduleCalendar.MonthGrid(new DateOnly(2027, 2, 1));

        Assert.Equal(4, weeks.Count);
        Assert.Equal(new DateOnly(2027, 2, 1), weeks[0][0]);
        Assert.Equal(new DateOnly(2027, 2, 28), weeks[^1][^1]);
    }

    [Theory]
    [InlineData("2026-11-17", "2026-11-01")]
    [InlineData("2026-12-31", "2026-12-01")]
    public void StartOfMonth_ReturnsFirstDay(string date, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), ScheduleCalendar.StartOfMonth(DateOnly.Parse(date)));
    }
}
