// Purpose: Team lead's schedule pages: a weekly grid of technicians by day, and a monthly calendar
// showing the jobs of whichever technicians the team lead chooses.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageSchedule)]
public class ScheduleController : Controller
{
    private readonly JobService _jobService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;

    public ScheduleController(JobService jobService, TechnicianService technicianService, TimeProvider timeProvider)
    {
        _jobService = jobService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
    }

    /// <summary>Shows the Monday–Sunday week containing <paramref name="week"/> (default: this week).</summary>
    public async Task<IActionResult> Index(DateOnly? week)
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var weekStart = ScheduleCalendar.StartOfWeek(week ?? today);
        var days = ScheduleCalendar.DaysBetween(weekStart, weekStart.AddDays(6));

        var technicians = await _technicianService.GetAllAsync();
        var lookup = technicians.ToLookup();
        var jobs = (await _jobService.GetJobsInRangeAsync(days[0], days[^1]))
            .Select(j => JobSummaryViewModel.FromEntity(j, lookup))
            .ToList();

        List<List<JobSummaryViewModel>> JobsByDay(Func<JobSummaryViewModel, bool> include) =>
            days.Select(day => jobs.Where(j => j.StartDate <= day && j.EndDate >= day && include(j)).ToList()).ToList();

        var rows = technicians
            .Select(t => new WeeklyScheduleRow(t.FullName, t.Id, JobsByDay(j => j.Technicians.Any(a => a.Id == t.Id))))
            .ToList();

        var unassigned = JobsByDay(j => j.Technicians.Count == 0);
        if (unassigned.Any(d => d.Count > 0))
        {
            rows.Add(new WeeklyScheduleRow("Unassigned", null, unassigned));
        }

        return View(new WeeklyScheduleViewModel { WeekStart = weekStart, Today = today, Days = days, Rows = rows });
    }

    /// <summary>
    /// Shows the calendar month containing <paramref name="month"/> (default: this month) for the chosen
    /// technicians. With no choice made yet (<paramref name="filtered"/> false) every technician is shown;
    /// once the picker is submitted, exactly the checked technicians are shown, which may be none.
    /// </summary>
    public async Task<IActionResult> Month(DateOnly? month, List<Guid> technicianIds, bool filtered = false)
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var first = ScheduleCalendar.StartOfMonth(month ?? today);
        var grid = ScheduleCalendar.MonthGrid(first);

        var technicians = await _technicianService.GetAllAsync();
        var tags = TechnicianTagViewModel.ForAll(technicians);
        var selected = filtered
            ? technicianIds.Where(id => technicians.Any(t => t.Id == id)).ToHashSet()
            : technicians.Select(t => t.Id).ToHashSet();

        var lookup = technicians.ToLookup();
        var jobs = (await _jobService.GetJobsForTechniciansAsync(selected, grid[0][0], grid[^1][^1]))
            .Select(j => JobSummaryViewModel.FromEntity(j, lookup))
            .ToList();

        var calendar = MonthCalendarViewModel.Build(first, today, jobs, job => new CalendarEntryViewModel(
            job,
            Url.Action("Details", "Jobs", new { id = job.Id })!,
            tags.Where(t => selected.Contains(t.Id) && job.Technicians.Any(a => a.Id == t.Id)).ToList()));

        return View(new MonthlyScheduleViewModel
        {
            Calendar = calendar,
            Technicians = tags,
            SelectedTechnicianIds = selected,
            PreviousMonthUrl = MonthUrl(first.AddMonths(-1), selected, filtered),
            NextMonthUrl = MonthUrl(first.AddMonths(1), selected, filtered),
            ThisMonthUrl = MonthUrl(ScheduleCalendar.StartOfMonth(today), selected, filtered)
        });
    }

    /// <summary>Builds a Month link that keeps the current technician choice (one query value per technician).</summary>
    private string MonthUrl(DateOnly month, IEnumerable<Guid> technicianIds, bool filtered)
    {
        var query = new List<KeyValuePair<string, string?>> { new("month", month.ToString("yyyy-MM-dd")) };
        if (filtered)
        {
            query.Add(new("filtered", "true"));
            query.AddRange(technicianIds.Select(id => new KeyValuePair<string, string?>("technicianIds", id.ToString())));
        }

        return Url.Action(nameof(Month)) + QueryString.Create(query);
    }
}
