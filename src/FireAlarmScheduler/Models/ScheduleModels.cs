// Purpose: View models for date-organized pages: the dashboard, the team lead's weekly grid and
// monthly calendar, the Jobs page calendar, and the technician's daily/weekly/monthly schedule, including approved PTO.
// Month grids place each job in a lane so multi-day jobs show as one joined bar per week row.
namespace FireAlarmScheduler.Models;

/// <summary>One day of a schedule list: its jobs and any approved time off (PTO is only filled on technician schedules).</summary>
public record ScheduleDayViewModel(DateOnly Date, List<JobSummaryViewModel> Jobs, List<PtoDayViewModel>? Pto = null)
{
    public List<PtoDayViewModel> TimeOff => Pto ?? [];
}

/// <summary>
/// The part of one technician's approved PTO that falls on one day. <see cref="Tag"/> carries the technician's
/// color and initials where several technicians share a view; it is null on a technician's own schedule.
/// </summary>
public record PtoDayViewModel(Guid TechnicianId, string TechnicianName, string HoursText, TechnicianTagViewModel? Tag)
{
    /// <summary>Returns the PTO shown on <paramref name="day"/> for <paramref name="request"/>, or null if it doesn't cover that day.</summary>
    public static PtoDayViewModel? For(Entities.PtoRequest request, DateOnly day, string technicianName, TechnicianTagViewModel? tag = null)
    {
        if (request.PortionOn(day) is not { } portion)
        {
            return null;
        }

        var hours = (portion.Start, portion.End) switch
        {
            ({ Hour: 0, Minute: 0 }, null) => "All day",
            (_, null) => $"From {portion.Start:h:mm tt}",
            ({ Hour: 0, Minute: 0 }, { } end) => $"Until {end:h:mm tt}",
            var (start, end) => $"{start:h:mm tt} – {end:h:mm tt}"
        };
        return new PtoDayViewModel(request.TechnicianId, technicianName, hours, tag);
    }

    /// <summary>Every approved PTO block on <paramref name="day"/> from <paramref name="requests"/>.</summary>
    public static List<PtoDayViewModel> ForDay(IEnumerable<Entities.PtoRequest> requests, DateOnly day,
        Func<Guid, string> nameOf, Func<Guid, TechnicianTagViewModel?>? tagOf = null) =>
        requests.Select(r => For(r, day, nameOf(r.TechnicianId), tagOf?.Invoke(r.TechnicianId)))
            .OfType<PtoDayViewModel>()
            .ToList();
}

public class DashboardViewModel
{
    public DateOnly Today { get; init; }

    /// <summary>Jobs today, upcoming jobs, pending PTO, and conflicts, shown as cards across the top.</summary>
    public List<SummaryCardViewModel> SummaryCards { get; init; } = [];
    public List<JobSummaryViewModel> CurrentJobs { get; init; } = [];
    public List<JobSummaryViewModel> UpcomingJobs { get; init; } = [];
    public List<TechnicianListItemViewModel> Technicians { get; init; } = [];

    /// <summary>The next two weeks, one entry per day that has at least one job.</summary>
    public List<ScheduleDayViewModel> Days { get; init; } = [];
}

/// <summary>One row of the weekly grid: a technician (or "Unassigned") and their jobs on each day.</summary>
public record WeeklyScheduleRow(string Label, Guid? TechnicianId, List<List<JobSummaryViewModel>> JobsByDay, List<List<PtoDayViewModel>> PtoByDay);

public class WeeklyScheduleViewModel
{
    public DateOnly WeekStart { get; init; }
    public DateOnly Today { get; init; }
    public List<DateOnly> Days { get; init; } = [];
    public List<WeeklyScheduleRow> Rows { get; init; } = [];

    public DateOnly WeekEnd => WeekStart.AddDays(6);
}

public enum ScheduleRange
{
    Day,
    Week,
    Month
}

/// <summary>A technician as shown on the monthly calendar: initials plus a stable color slot.</summary>
public record TechnicianTagViewModel(Guid Id, string FullName, string Initials, int ColorIndex)
{
    /// <summary>Number of distinct technician colors defined in site.css (.tech-color-0 to -7).</summary>
    public const int ColorCount = 8;

    /// <summary>Tags every technician in list order, so each keeps the same color whoever else is selected.</summary>
    public static List<TechnicianTagViewModel> ForAll(IEnumerable<Entities.Technician> technicians) =>
        technicians.Select((t, i) => new TechnicianTagViewModel(
            t.Id, t.FullName, $"{FirstLetter(t.FirstName)}{FirstLetter(t.LastName)}", i % ColorCount)).ToList();

    private static string FirstLetter(string name) => name.Length == 0 ? "" : char.ToUpperInvariant(name[0]).ToString();
}

/// <summary>One job on one calendar day, with where it links to and which shown technicians are on it.</summary>
public record CalendarEntryViewModel(JobSummaryViewModel Job, string Url, List<TechnicianTagViewModel> Technicians);

/// <summary>
/// A job's bar on one calendar day. <see cref="ContinuesBefore"/>/<see cref="ContinuesAfter"/> are true when the job
/// also runs on the day before/after, so the bar joins its neighbors and shows how long the job takes.
/// </summary>
public record CalendarSlotViewModel(CalendarEntryViewModel Entry, bool ContinuesBefore, bool ContinuesAfter);

/// <summary>
/// One day of a month grid. <see cref="Slots"/> holds the day's jobs by lane: a multi-day job keeps the same lane on
/// every day of its week row, and a null slot is an empty gap that keeps the lanes below it lined up.
/// </summary>
public record CalendarDayViewModel(DateOnly Date, bool InMonth, bool IsToday, string? DayUrl, List<CalendarSlotViewModel?> Slots,
    List<PtoDayViewModel> Pto)
{
    public IEnumerable<CalendarEntryViewModel> Entries => Slots.OfType<CalendarSlotViewModel>().Select(s => s.Entry);
}

public static class CalendarLanes
{
    /// <summary>
    /// Gives each job running in the week a lane (line within the week row) so a multi-day job stays on one line.
    /// Earlier and longer jobs get the top lanes; a lane is reused once the job in it has ended.
    /// </summary>
    public static List<(T Item, int Lane)> Assign<T>(IEnumerable<T> items, Func<T, JobSummaryViewModel> jobOf,
        DateOnly weekStart, DateOnly weekEnd)
    {
        var laneEnds = new List<DateOnly>();
        var placed = new List<(T, int)>();
        foreach (var item in items.Where(i => jobOf(i).OverlapsRange(weekStart, weekEnd))
                     .OrderBy(i => jobOf(i).StartDate).ThenByDescending(i => jobOf(i).EndDate)
                     .ThenBy(i => jobOf(i).StartTime).ThenBy(i => jobOf(i).Name, StringComparer.OrdinalIgnoreCase))
        {
            var job = jobOf(item);
            var lane = laneEnds.FindIndex(end => end < job.StartDate);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(job.EndDate);
            }
            else
            {
                laneEnds[lane] = job.EndDate;
            }

            placed.Add((item, lane));
        }

        return placed;
    }
}

public class MonthCalendarViewModel
{
    public DateOnly Month { get; init; }

    /// <summary>Monday–Sunday rows covering the month, including nearby days that fill the first and last rows.</summary>
    public List<List<CalendarDayViewModel>> Weeks { get; init; } = [];

    public string Title => Month.ToString("MMMM yyyy");

    /// <summary>
    /// Places each job on every grid day it runs. <paramref name="entryFor"/> builds the calendar entry
    /// (link and technician tags) for a job; <paramref name="ptoFor"/> lists approved time off on a day;
    /// <paramref name="dayUrl"/> optionally links each day number.
    /// </summary>
    public static MonthCalendarViewModel Build(DateOnly month, DateOnly today, IReadOnlyList<JobSummaryViewModel> jobs,
        Func<JobSummaryViewModel, CalendarEntryViewModel> entryFor, Func<DateOnly, List<PtoDayViewModel>> ptoFor,
        Func<DateOnly, string?>? dayUrl = null)
    {
        var entries = jobs.Select(entryFor).ToList();
        var first = Services.ScheduleCalendar.StartOfMonth(month);

        return new MonthCalendarViewModel
        {
            Month = first,
            Weeks = Services.ScheduleCalendar.MonthGrid(first)
                .Select(week =>
                {
                    var lanes = CalendarLanes.Assign(entries, e => e.Job, week[0], week[^1]);
                    return week.Select(day => new CalendarDayViewModel(
                        day,
                        day.Month == first.Month,
                        day == today,
                        dayUrl?.Invoke(day),
                        SlotsOn(day, lanes),
                        ptoFor(day))).ToList();
                })
                .ToList()
        };
    }

    /// <summary>The day's jobs in lane order, with null gaps for empty lanes above a used one.</summary>
    private static List<CalendarSlotViewModel?> SlotsOn(DateOnly day, List<(CalendarEntryViewModel Entry, int Lane)> lanes)
    {
        var slots = new List<CalendarSlotViewModel?>();
        foreach (var (entry, lane) in lanes.Where(l => l.Entry.Job.OverlapsRange(day, day)))
        {
            while (slots.Count <= lane)
            {
                slots.Add(null);
            }

            slots[lane] = new CalendarSlotViewModel(entry, entry.Job.StartDate < day, entry.Job.EndDate > day);
        }

        return slots;
    }
}

/// <summary>The team lead's monthly calendar for any number of chosen technicians.</summary>
public class MonthlyScheduleViewModel
{
    public MonthCalendarViewModel Calendar { get; init; } = new();

    /// <summary>Every technician with their color; the checkboxes for choosing whose jobs to show.</summary>
    public List<TechnicianTagViewModel> Technicians { get; init; } = [];
    public HashSet<Guid> SelectedTechnicianIds { get; init; } = [];
    public string PreviousMonthUrl { get; init; } = "";
    public string NextMonthUrl { get; init; } = "";
    public string ThisMonthUrl { get; init; } = "";
}

/// <summary>
/// One job's bar within a week row of the Jobs calendar: it starts in day <see cref="Column"/> (1 = Monday) and covers
/// <see cref="Span"/> days on line <see cref="Lane"/>. <see cref="ContinuesBefore"/>/<see cref="ContinuesAfter"/> mark a
/// job that carries on from the previous week or into the next.
/// </summary>
public record JobBarViewModel(JobSummaryViewModel Job, string Url, int ColorIndex, int Column, int Span, int Lane,
    bool ContinuesBefore, bool ContinuesAfter);

public record JobsCalendarDayViewModel(DateOnly Date, bool InMonth, bool IsToday);

public record JobsCalendarWeekViewModel(List<JobsCalendarDayViewModel> Days, List<JobBarViewModel> Bars)
{
    public int LaneCount => Bars.Count == 0 ? 0 : Bars.Max(b => b.Lane) + 1;
}

/// <summary>A job in the list under the Jobs calendar, with the color of its bar.</summary>
public record ColoredJobViewModel(JobSummaryViewModel Job, int ColorIndex);

/// <summary>
/// The Jobs page's month calendar: each job is one solid-colored bar per week row, spanning the days it runs,
/// plus the month's jobs as a list.
/// </summary>
public class JobsCalendarViewModel
{
    /// <summary>Number of distinct job colors defined in site.css (.job-color-0 to -9).</summary>
    public const int ColorCount = 10;

    public DateOnly Month { get; init; }
    public List<JobsCalendarWeekViewModel> Weeks { get; init; } = [];
    public List<ColoredJobViewModel> MonthJobs { get; init; } = [];
    public DateOnly PreviousMonth => Month.AddMonths(-1);
    public DateOnly NextMonth => Month.AddMonths(1);
    public DateOnly ThisMonth { get; init; }

    public string Title => Month.ToString("MMMM yyyy");

    /// <summary>
    /// Lays out <paramref name="jobs"/> on the Monday–Sunday grid for <paramref name="month"/>. Jobs are colored in
    /// start order, cycling through the palette, so jobs that start near each other get different colors.
    /// </summary>
    public static JobsCalendarViewModel Build(DateOnly month, DateOnly today, IReadOnlyList<JobSummaryViewModel> jobs,
        Func<JobSummaryViewModel, string> urlFor)
    {
        var first = Services.ScheduleCalendar.StartOfMonth(month);
        var colored = jobs.OrderBy(j => j.StartDate).ThenByDescending(j => j.EndDate).ThenBy(j => j.StartTime)
            .ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase)
            .Select((job, i) => new ColoredJobViewModel(job, i % ColorCount))
            .ToList();

        var weeks = Services.ScheduleCalendar.MonthGrid(first).Select(week =>
        {
            var (weekStart, weekEnd) = (week[0], week[^1]);
            var bars = CalendarLanes.Assign(colored, c => c.Job, weekStart, weekEnd)
                .Select(placed =>
                {
                    var job = placed.Item.Job;
                    var from = job.StartDate > weekStart ? job.StartDate : weekStart;
                    var to = job.EndDate < weekEnd ? job.EndDate : weekEnd;
                    return new JobBarViewModel(job, urlFor(job), placed.Item.ColorIndex,
                        from.DayNumber - weekStart.DayNumber + 1, to.DayNumber - from.DayNumber + 1, placed.Lane,
                        job.StartDate < weekStart, job.EndDate > weekEnd);
                })
                .ToList();
            var days = week.Select(day => new JobsCalendarDayViewModel(day, day.Month == first.Month, day == today)).ToList();
            return new JobsCalendarWeekViewModel(days, bars);
        }).ToList();

        return new JobsCalendarViewModel
        {
            Month = first,
            Weeks = weeks,
            MonthJobs = colored.Where(c => c.Job.OverlapsRange(first, first.AddMonths(1).AddDays(-1))).ToList(),
            ThisMonth = Services.ScheduleCalendar.StartOfMonth(today)
        };
    }
}

public class TechnicianScheduleViewModel
{
    /// <summary>False for technician accounts, which only see their own linked schedule.</summary>
    public bool CanChooseTechnician { get; init; }
    public List<TechnicianOption> Technicians { get; init; } = [];
    public TechnicianOption? SelectedTechnician { get; init; }
    public ScheduleRange Range { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly Today { get; init; }
    public DateOnly PreviousDate { get; init; }
    public DateOnly NextDate { get; init; }
    /// <summary>Every day of the chosen day or week, including days with no jobs. Empty for the month view.</summary>
    public List<ScheduleDayViewModel> Days { get; init; } = [];

    /// <summary>The month grid; only set for the month view.</summary>
    public MonthCalendarViewModel? Calendar { get; init; }

    /// <summary>The month's jobs, each listed once, shown under the calendar for easy reading on phones.</summary>
    public List<CalendarEntryViewModel> MonthJobs { get; init; } = [];

    public string RangeTitle => Range switch
    {
        ScheduleRange.Day => Date.ToString("dddd, MMMM d, yyyy"),
        ScheduleRange.Week => $"Week of {Days[0].Date:MMM d} – {Days[^1].Date:MMM d, yyyy}",
        _ => Date.ToString("MMMM yyyy")
    };
}

public class TechnicianJobViewModel
{
    public TechnicianOption Technician { get; init; } = new(Guid.Empty, "");
    public JobDetailsViewModel Job { get; init; } = new();

    /// <summary>True when the signed-in user is this technician, so they can check buildings off.</summary>
    public bool CanCheckOffBuildings { get; init; }

    /// <summary>The checklist items in each section, keeping each building's place in the job's order.</summary>
    public List<ChecklistItemViewModel> ItemsWhere(bool complete) => Job.Buildings
        .Select((building, position) => new ChecklistItemViewModel(Job.Summary.Id, position, building, CanCheckOffBuildings))
        .Where(item => item.Building.IsComplete == complete)
        .ToList();
}

/// <summary>
/// One building on the technician's checklist. <paramref name="Position"/> is its place in the job's building order,
/// which the checklist script uses to slot it back in the right place when it moves between sections.
/// </summary>
public record ChecklistItemViewModel(Guid JobId, int Position, BuildingViewModel Building, bool CanCheckOff);
