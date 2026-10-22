// Purpose: Team lead's weekly schedule grid showing which technicians are on which jobs each day.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

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
}
