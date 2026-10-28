// Purpose: View models for date-organized pages: the dashboard, the team lead's weekly grid and
// monthly calendar, and the technician's daily/weekly/monthly schedule.
namespace FireAlarmScheduler.Models;

public record ScheduleDayViewModel(DateOnly Date, List<JobSummaryViewModel> Jobs);

public class DashboardViewModel
{
    public DateOnly Today { get; init; }
    public List<JobSummaryViewModel> CurrentJobs { get; init; } = [];
    public List<JobSummaryViewModel> UpcomingJobs { get; init; } = [];
    public List<TechnicianListItemViewModel> Technicians { get; init; } = [];

    /// <summary>The next two weeks, one entry per day that has at least one job.</summary>
    public List<ScheduleDayViewModel> Days { get; init; } = [];
}

/// <summary>One row of the weekly grid: a technician (or "Unassigned") and their jobs on each day.</summary>
public record WeeklyScheduleRow(string Label, Guid? TechnicianId, List<List<JobSummaryViewModel>> JobsByDay);

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

public record CalendarDayViewModel(DateOnly Date, bool InMonth, bool IsToday, string? DayUrl, List<CalendarEntryViewModel> Entries);

public class MonthCalendarViewModel
{
    public DateOnly Month { get; init; }

    /// <summary>Monday–Sunday rows covering the month, including nearby days that fill the first and last rows.</summary>
    public List<List<CalendarDayViewModel>> Weeks { get; init; } = [];

    public string Title => Month.ToString("MMMM yyyy");

    /// <summary>
    /// Places each job on every grid day it runs. <paramref name="entryFor"/> builds the calendar entry
    /// (link and technician tags) for a job; <paramref name="dayUrl"/> optionally links each day number.
    /// </summary>
    public static MonthCalendarViewModel Build(DateOnly month, DateOnly today, IReadOnlyList<JobSummaryViewModel> jobs,
        Func<JobSummaryViewModel, CalendarEntryViewModel> entryFor, Func<DateOnly, string?>? dayUrl = null)
    {
        var entries = jobs.Select(entryFor).ToList();
        var first = Services.ScheduleCalendar.StartOfMonth(month);

        return new MonthCalendarViewModel
        {
            Month = first,
            Weeks = Services.ScheduleCalendar.MonthGrid(first)
                .Select(week => week.Select(day => new CalendarDayViewModel(
                    day,
                    day.Month == first.Month,
                    day == today,
                    dayUrl?.Invoke(day),
                    entries.Where(e => e.Job.OverlapsRange(day, day)).ToList())).ToList())
                .ToList()
        };
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
}
