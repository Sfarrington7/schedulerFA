// Purpose: Tests how the month grid lays out jobs: each job keeps one lane across its days in a week row (gaps keep
// later lanes lined up), lanes are reused once a job ends, and each day's bar knows whether it joins the day
// before/after, including a job that runs over the end of a week. Also covers the Jobs calendar's spanning bars
// (start column, length, lane, carrying over into the next week) and their colors.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;

namespace FireAlarmScheduler.Tests.Services;

public class MonthCalendarTests
{
    private static readonly DateOnly November = new(2026, 11, 1);

    private static JobSummaryViewModel Job(string name, string start, string end) => new()
    {
        Id = Guid.NewGuid(), Name = name, StartDate = DateOnly.Parse(start), EndDate = DateOnly.Parse(end),
        StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(16, 0), Status = JobStatus.Scheduled
    };

    private static MonthCalendarViewModel Build(params JobSummaryViewModel[] jobs) =>
        MonthCalendarViewModel.Build(November, new DateOnly(2026, 11, 3), jobs, job => new CalendarEntryViewModel(job, "/", []), _ => []);

    private static CalendarDayViewModel Day(MonthCalendarViewModel calendar, string date) =>
        calendar.Weeks.SelectMany(w => w).Single(d => d.Date == DateOnly.Parse(date));

    private static List<string?> Lanes(MonthCalendarViewModel calendar, string date) =>
        Day(calendar, date).Slots.Select(s => s?.Entry.Job.Name).ToList();

    [Fact]
    public void Build_OverlappingJobs_EachKeepsItsLaneWithGapsBelowEndedJobs()
    {
        var calendar = Build(Job("A", "2026-11-02", "2026-11-03"), Job("B", "2026-11-03", "2026-11-05"));

        Assert.Equal(["A"], Lanes(calendar, "2026-11-02"));
        Assert.Equal(["A", "B"], Lanes(calendar, "2026-11-03"));
        Assert.Equal([null, "B"], Lanes(calendar, "2026-11-04"));
        Assert.Empty(Lanes(calendar, "2026-11-06"));
    }

    [Fact]
    public void Build_LaneFreedByEndedJob_IsReused()
    {
        var calendar = Build(Job("Long", "2026-11-02", "2026-11-06"), Job("Mon", "2026-11-02", "2026-11-02"),
            Job("Wed", "2026-11-04", "2026-11-04"));

        Assert.Equal(["Long", "Mon"], Lanes(calendar, "2026-11-02"));
        Assert.Equal(["Long", "Wed"], Lanes(calendar, "2026-11-04"));
    }

    [Fact]
    public void Build_MultiDayJob_BarJoinsNeighboringDaysOnly()
    {
        var calendar = Build(Job("A", "2026-11-03", "2026-11-05"));

        var first = Day(calendar, "2026-11-03").Slots.Single()!;
        var middle = Day(calendar, "2026-11-04").Slots.Single()!;
        var last = Day(calendar, "2026-11-05").Slots.Single()!;

        Assert.False(first.ContinuesBefore);
        Assert.True(first.ContinuesAfter);
        Assert.True(middle.ContinuesBefore && middle.ContinuesAfter);
        Assert.True(last.ContinuesBefore);
        Assert.False(last.ContinuesAfter);
    }

    [Fact]
    public void Build_JobOverWeekend_ContinuesIntoNextRowOnTopLane()
    {
        var calendar = Build(Job("Short", "2026-11-06", "2026-11-06"), Job("Over", "2026-11-06", "2026-11-10"));

        Assert.Equal(["Over", "Short"], Lanes(calendar, "2026-11-06"));
        var monday = Day(calendar, "2026-11-09").Slots.Single()!;
        Assert.Equal("Over", monday.Entry.Job.Name);
        Assert.True(monday.ContinuesBefore);
        Assert.True(Day(calendar, "2026-11-08").Slots.Single()!.ContinuesAfter);
    }

    private static JobsCalendarViewModel BuildJobs(params JobSummaryViewModel[] jobs) =>
        JobsCalendarViewModel.Build(November, new DateOnly(2026, 11, 3), jobs, job => $"/Jobs/Details/{job.Name}");

    private static List<JobBarViewModel> Bars(JobsCalendarViewModel calendar, string weekStart) =>
        calendar.Weeks.Single(w => w.Days[0].Date == DateOnly.Parse(weekStart)).Bars;

    [Fact]
    public void JobsBuild_MultiDayJob_IsOneBarSpanningItsDays()
    {
        var calendar = BuildJobs(Job("A", "2026-11-03", "2026-11-05"));

        var bar = Assert.Single(Bars(calendar, "2026-11-02"));
        Assert.Equal((2, 3, 0), (bar.Column, bar.Span, bar.Lane));
        Assert.False(bar.ContinuesBefore || bar.ContinuesAfter);
        Assert.Equal("/Jobs/Details/A", bar.Url);
        Assert.Empty(Bars(calendar, "2026-11-09"));
    }

    [Fact]
    public void JobsBuild_JobOverWeekend_SplitsIntoTwoContinuingBars()
    {
        var calendar = BuildJobs(Job("Over", "2026-11-06", "2026-11-10"));

        var first = Assert.Single(Bars(calendar, "2026-11-02"));
        var second = Assert.Single(Bars(calendar, "2026-11-09"));
        Assert.Equal((5, 3, true), (first.Column, first.Span, first.ContinuesAfter));
        Assert.False(first.ContinuesBefore);
        Assert.Equal((1, 2, true), (second.Column, second.Span, second.ContinuesBefore));
        Assert.False(second.ContinuesAfter);
        Assert.Equal(first.ColorIndex, second.ColorIndex);
    }

    [Fact]
    public void JobsBuild_OverlappingJobs_StackOnLinesWithDifferentColors()
    {
        var calendar = BuildJobs(Job("Long", "2026-11-02", "2026-11-06"), Job("Wed", "2026-11-04", "2026-11-04"));

        var bars = Bars(calendar, "2026-11-02");
        var (longBar, wed) = (bars.Single(b => b.Job.Name == "Long"), bars.Single(b => b.Job.Name == "Wed"));
        Assert.Equal(0, longBar.Lane);
        Assert.Equal((3, 1), (wed.Column, wed.Lane));
        Assert.NotEqual(longBar.ColorIndex, wed.ColorIndex);
        Assert.Equal(2, Bars(calendar, "2026-11-02").Count);
        Assert.Equal(2, calendar.Weeks.Single(w => w.Days[0].Date == new DateOnly(2026, 11, 2)).LaneCount);
    }

    [Fact]
    public void JobsBuild_MonthJobs_OnlyThisMonthsJobsInStartOrder()
    {
        var calendar = BuildJobs(Job("Late", "2026-11-20", "2026-11-20"), Job("Early", "2026-11-02", "2026-11-02"),
            Job("October", "2026-10-27", "2026-10-28"));

        Assert.Equal(["Early", "Late"], calendar.MonthJobs.Select(j => j.Job.Name));
        Assert.Equal([1, 2], calendar.MonthJobs.Select(j => j.ColorIndex));
        Assert.Single(Bars(calendar, "2026-10-26"), b => b.Job.Name == "October");
    }
}
