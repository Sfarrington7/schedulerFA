// Purpose: Tests how an approved PTO period is split into days for the calendars: which days it covers
// and the hours text shown on each ("All day", "From …", "Until …", or a range).
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;

namespace FireAlarmScheduler.Tests.Services;

public class PtoDayTests
{
    private static PtoRequest Pto(string start, string startTime, string end, string endTime) => new()
    {
        Id = Guid.NewGuid(), TechnicianId = Guid.NewGuid(), Status = PtoStatus.Approved,
        StartDate = DateOnly.Parse(start), StartTime = TimeOnly.Parse(startTime),
        EndDate = DateOnly.Parse(end), EndTime = TimeOnly.Parse(endTime)
    };

    [Theory]
    [InlineData("2026-11-10", "00:00", "2026-11-12", "23:59", "2026-11-11", "All day")]
    [InlineData("2026-11-10", "00:00", "2026-11-12", "23:59", "2026-11-12", "All day")]
    [InlineData("2026-11-10", "12:00", "2026-11-12", "23:59", "2026-11-10", "From 12:00 PM")]
    [InlineData("2026-11-10", "12:00", "2026-11-12", "10:30", "2026-11-12", "Until 10:30 AM")]
    [InlineData("2026-11-10", "12:00", "2026-11-10", "17:00", "2026-11-10", "12:00 PM – 5:00 PM")]
    [InlineData("2026-11-10", "17:00", "2026-11-11", "00:00", "2026-11-10", "From 5:00 PM")]
    public void For_DayInsidePeriod_DescribesHoursOff(string start, string startTime, string end, string endTime, string day, string expected)
    {
        var shown = PtoDayViewModel.For(Pto(start, startTime, end, endTime), DateOnly.Parse(day), "John Smith");

        Assert.NotNull(shown);
        Assert.Equal(expected, shown.HoursText);
        Assert.Equal("John Smith", shown.TechnicianName);
    }

    [Theory]
    [InlineData("2026-11-09")] // day before
    [InlineData("2026-11-13")] // day after
    [InlineData("2026-11-11")] // ends exactly at midnight going into this day
    public void For_DayOutsidePeriod_ReturnsNull(string day)
    {
        var request = DateOnly.Parse(day) == new DateOnly(2026, 11, 11)
            ? Pto("2026-11-10", "08:00", "2026-11-11", "00:00")
            : Pto("2026-11-10", "00:00", "2026-11-12", "23:59");

        Assert.Null(PtoDayViewModel.For(request, DateOnly.Parse(day), "John Smith"));
    }
}
