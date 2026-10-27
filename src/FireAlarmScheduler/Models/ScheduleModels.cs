// Purpose: View models for date-organized pages: the dashboard, the team lead's weekly grid,
// and the technician's daily/weekly schedule.
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
    Week
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
    public List<ScheduleDayViewModel> Days { get; init; } = [];

    public string RangeTitle => Range == ScheduleRange.Day
        ? Date.ToString("dddd, MMMM d, yyyy")
        : $"Week of {Days[0].Date:MMM d} – {Days[^1].Date:MMM d, yyyy}";
}

public class TechnicianJobViewModel
{
    public TechnicianOption Technician { get; init; } = new(Guid.Empty, "");
    public JobDetailsViewModel Job { get; init; } = new();
}
