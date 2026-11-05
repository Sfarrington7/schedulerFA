// Purpose: Tests the wording and numbers of the dashboard's summary cards built from in-memory jobs and alerts:
// an empty schedule, today's job with its day count, the 14-day upcoming window and which job is next, singular and
// plural pending PTO, conflict descriptions and colors, and the sidebar badge's 99+ cap. Today is Nov 3, 2026.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Tests.Services;

public class DashboardSummaryTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly Guid John = Guid.NewGuid();
    private static readonly Guid Sara = Guid.NewGuid();

    private static JobSummaryViewModel Job(string name, string start, string end, string from = "08:00") => new()
    {
        Id = Guid.NewGuid(), Name = name, StartDate = DateOnly.Parse(start), EndDate = DateOnly.Parse(end),
        StartTime = TimeOnly.Parse(from), EndTime = new TimeOnly(16, 0), Status = JobStatus.Scheduled
    };

    private static ScheduleConflict Conflict(ConflictKind kind, Guid technician, string first) =>
        new(kind, technician, new Job(), DateOnly.Parse(first), DateOnly.Parse(first), null, null);

    private static List<SummaryCardViewModel> Build(IReadOnlyList<JobSummaryViewModel> jobs, int pendingPto = 0,
        params ScheduleConflict[] conflicts) =>
        DashboardSummary.Build(jobs, new ScheduleAlerts(Today, pendingPto, conflicts), id => id == John ? "John Smith" : "Sara Lee");

    private static SummaryCardViewModel Card(List<SummaryCardViewModel> cards, string label) => cards.Single(c => c.Label == label);

    [Fact]
    public void Build_EmptySchedule_AllZeroWithEmptyMessages()
    {
        var cards = Build([]);

        Assert.Equal(["Jobs today", "Upcoming (14 days)", "Pending PTO", "Conflicts"], cards.Select(c => c.Label));
        Assert.All(cards, c => Assert.Equal(0, c.Count));
        Assert.Equal("No jobs running today", Card(cards, "Jobs today").Detail);
        Assert.Equal("Nothing booked in the next 14 days", Card(cards, "Upcoming (14 days)").Detail);
        Assert.Equal(("All caught up", SummaryTone.Clear), (Card(cards, "Pending PTO").Detail, Card(cards, "Pending PTO").Tone));
        Assert.Equal(("No scheduling conflicts", SummaryTone.Clear), (Card(cards, "Conflicts").Detail, Card(cards, "Conflicts").Tone));
    }

    [Fact]
    public void Build_CardsLinkToTheirPages()
    {
        var links = Build([]).Select(c => $"{c.Controller}/{c.Action}");

        Assert.Equal(["Schedule/Index", "Jobs/Calendar", "PtoRequests/Index", "Conflicts/Index"], links);
    }

    [Fact]
    public void Build_MultiDayJobRunningToday_ShowsWhichDayItIs()
    {
        var card = Card(Build([Job("Central Plaza", "2026-11-02", "2026-11-06")]), "Jobs today");

        Assert.Equal(1, card.Count);
        Assert.Equal("Central Plaza · day 2 of 5", card.Detail);
    }

    [Fact]
    public void Build_SeveralJobsToday_EarliestStartFirstPlusCount()
    {
        var card = Card(Build([
            Job("Afternoon", "2026-11-03", "2026-11-03", "13:00"),
            Job("Morning", "2026-11-03", "2026-11-03", "07:00"),
            Job("Ended yesterday", "2026-11-01", "2026-11-02")
        ]), "Jobs today");

        Assert.Equal(2, card.Count);
        Assert.Equal("Morning + 1 more", card.Detail);
    }

    [Fact]
    public void Build_Upcoming_CountsStartsAfterTodayWithinFourteenDays()
    {
        var card = Card(Build([
            Job("Running today", "2026-11-02", "2026-11-04"),
            Job("Last day in window", "2026-11-16", "2026-11-16"),
            Job("Just outside window", "2026-11-17", "2026-11-17"),
            Job("Riverside", "2026-11-10", "2026-11-12"),
            Job("Tomorrow", "2026-11-04", "2026-11-04")
        ]), "Upcoming (14 days)");

        Assert.Equal(3, card.Count);
        Assert.Equal("Next: Tomorrow, Wed, Nov 4", card.Detail);
    }

    [Theory]
    [InlineData(1, "1 request needs review")]
    [InlineData(3, "3 requests need review")]
    public void Build_PendingPto_CountsAndNeedsAttention(int pending, string detail)
    {
        var card = Card(Build([], pending), "Pending PTO");

        Assert.Equal(pending, card.Count);
        Assert.Equal((detail, SummaryTone.Attention), (card.Detail, card.Tone));
    }

    [Fact]
    public void Build_Conflicts_DescribesEarliestAndCountsTheRest()
    {
        var card = Card(Build([], 0,
            Conflict(ConflictKind.OnPto, Sara, "2026-11-11"),
            Conflict(ConflictKind.DoubleBooked, John, "2026-11-19")), "Conflicts");

        Assert.Equal(2, card.Count);
        Assert.Equal(("Sara Lee booked during PTO on Wed, Nov 11 + 1 more", SummaryTone.Problem), (card.Detail, card.Tone));
    }

    [Fact]
    public void Build_SingleDoubleBooking_SaysDoubleBooked()
    {
        var card = Card(Build([], 0, Conflict(ConflictKind.DoubleBooked, John, "2026-11-19")), "Conflicts");

        Assert.Equal("John Smith double-booked on Thu, Nov 19", card.Detail);
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void BadgeText_LargeCounts_CappedAt99Plus(int count, string expected)
    {
        Assert.Equal(expected, NavAlertCountsViewModel.BadgeText(count));
    }
}
