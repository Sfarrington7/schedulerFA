// Purpose: The technician-facing, read-only pages: a daily or weekly schedule and assigned job details
// with building spreadsheets. Technician accounts only ever see their own linked technician;
// team leads and administrators can pick any technician.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ViewSchedule)]
public class TechnicianViewController : Controller
{
    /// <summary>Remembers the last chosen technician so a phone returns to the same schedule.</summary>
    private const string SelectedTechnicianCookie = "SelectedTechnicianId";

    private readonly JobService _jobService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;
    private readonly UserManager<ApplicationUser> _userManager;

    public TechnicianViewController(JobService jobService, TechnicianService technicianService, TimeProvider timeProvider,
        UserManager<ApplicationUser> userManager)
    {
        _jobService = jobService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(Guid? technicianId, ScheduleRange range = ScheduleRange.Week, DateOnly? date = null)
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var selectedDate = date ?? today;
        var technicians = await _technicianService.GetAllAsync();
        var canChoose = User.CanManageSchedule();

        if (canChoose)
        {
            technicianId ??= Guid.TryParse(Request.Cookies[SelectedTechnicianCookie], out var remembered) ? remembered : null;
        }
        else
        {
            technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        }

        var selected = technicians.FirstOrDefault(t => t.Id == technicianId);
        if (canChoose && selected is not null)
        {
            Response.Cookies.Append(SelectedTechnicianCookie, selected.Id.ToString(),
                new CookieOptions { HttpOnly = true, IsEssential = true, MaxAge = TimeSpan.FromDays(30) });
        }

        var (from, to, step) = range == ScheduleRange.Day
            ? (selectedDate, selectedDate, 1)
            : (ScheduleCalendar.StartOfWeek(selectedDate), ScheduleCalendar.StartOfWeek(selectedDate).AddDays(6), 7);

        var days = new List<ScheduleDayViewModel>();
        if (selected is not null)
        {
            var lookup = technicians.ToLookup();
            var jobs = (await _jobService.GetJobsForTechnicianAsync(selected.Id, from, to))
                .Select(j => JobSummaryViewModel.FromEntity(j, lookup))
                .ToList();
            days = ScheduleCalendar.DaysBetween(from, to)
                .Select(day => new ScheduleDayViewModel(day, jobs.Where(j => j.StartDate <= day && j.EndDate >= day).ToList()))
                .ToList();
        }

        return View(new TechnicianScheduleViewModel
        {
            CanChooseTechnician = canChoose,
            Technicians = canChoose ? technicians.Select(t => new TechnicianOption(t.Id, t.FullName)).ToList() : [],
            SelectedTechnician = selected is null ? null : new TechnicianOption(selected.Id, selected.FullName),
            Range = range,
            Date = selectedDate,
            Today = today,
            PreviousDate = selectedDate.AddDays(-step),
            NextDate = selectedDate.AddDays(step),
            Days = days
        });
    }

    /// <summary>
    /// Shows a job's details and files, but only if it is assigned to the chosen technician.
    /// Technician accounts are always treated as their own linked technician, whatever the URL says.
    /// </summary>
    public async Task<IActionResult> Job(Guid id, Guid technicianId)
    {
        if (!User.CanManageSchedule())
        {
            technicianId = await _userManager.GetLinkedTechnicianIdAsync(User) ?? Guid.Empty;
        }

        var technician = await _technicianService.GetAsync(technicianId);
        var job = await _jobService.GetAsync(id);
        if (technician is null || job is null || !job.AssignedTechnicianIds.Contains(technicianId))
        {
            return NotFound();
        }

        var lookup = (await _technicianService.GetAllAsync()).ToLookup();
        return View(new TechnicianJobViewModel
        {
            Technician = new TechnicianOption(technician.Id, technician.FullName),
            Job = JobDetailsViewModel.FromEntity(job, lookup)
        });
    }
}
