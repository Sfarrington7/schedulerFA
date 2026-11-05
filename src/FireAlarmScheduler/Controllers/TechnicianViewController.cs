// Purpose: The technician-facing pages: a daily, weekly, or monthly schedule (jobs and approved PTO) and assigned job
// details with the job files link and a building checklist the technician ticks off. Technician accounts only ever see
// their own linked technician; team leads and administrators can pick any technician and view their checklist read-only.
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
    private readonly PtoService _ptoService;

    public TechnicianViewController(JobService jobService, TechnicianService technicianService, TimeProvider timeProvider,
        UserManager<ApplicationUser> userManager, PtoService ptoService)
    {
        _ptoService = ptoService;
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

        var weekStart = ScheduleCalendar.StartOfWeek(selectedDate);
        var monthGrid = ScheduleCalendar.MonthGrid(selectedDate);
        var (from, to) = range switch
        {
            ScheduleRange.Day => (selectedDate, selectedDate),
            ScheduleRange.Week => (weekStart, weekStart.AddDays(6)),
            _ => (monthGrid[0][0], monthGrid[^1][^1])
        };

        var jobs = new List<JobSummaryViewModel>();
        var pto = new List<PtoRequest>();
        if (selected is not null)
        {
            var lookup = technicians.ToLookup();
            jobs = (await _jobService.GetJobsForTechnicianAsync(selected.Id, from, to))
                .Select(j => JobSummaryViewModel.FromEntity(j, lookup))
                .ToList();
            pto = (await _ptoService.GetApprovedInRangeAsync(from, to)).Where(p => p.TechnicianId == selected.Id).ToList();
        }

        List<PtoDayViewModel> PtoOn(DateOnly day) => PtoDayViewModel.ForDay(pto, day, _ => selected!.FullName);

        CalendarEntryViewModel EntryFor(JobSummaryViewModel job) =>
            new(job, Url.Action(nameof(Job), new { id = job.Id, technicianId = selected?.Id })!, []);

        MonthCalendarViewModel? calendar = null;
        var monthJobs = new List<CalendarEntryViewModel>();
        var days = new List<ScheduleDayViewModel>();
        if (selected is not null && range == ScheduleRange.Month)
        {
            calendar = MonthCalendarViewModel.Build(selectedDate, today, jobs, EntryFor, PtoOn,
                day => Url.Action(nameof(Index), new { technicianId = selected.Id, range = ScheduleRange.Day, date = day.ToString("yyyy-MM-dd") }));
            var month = ScheduleCalendar.StartOfMonth(selectedDate);
            monthJobs = jobs.Where(j => j.OverlapsRange(month, month.AddMonths(1).AddDays(-1))).Select(EntryFor).ToList();
        }
        else if (selected is not null)
        {
            days = ScheduleCalendar.DaysBetween(from, to)
                .Select(day => new ScheduleDayViewModel(day, jobs.Where(j => j.OverlapsRange(day, day)).ToList(), PtoOn(day)))
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
            PreviousDate = Step(range, selectedDate, -1),
            NextDate = Step(range, selectedDate, 1),
            Days = days,
            Calendar = calendar,
            MonthJobs = monthJobs
        });
    }

    private static DateOnly Step(ScheduleRange range, DateOnly date, int direction) => range switch
    {
        ScheduleRange.Day => date.AddDays(direction),
        ScheduleRange.Week => date.AddDays(7 * direction),
        _ => date.AddMonths(direction)
    };

    /// <summary>
    /// Shows a job's details, files link, and building checklist, but only if it is assigned to the chosen technician.
    /// Technician accounts are always treated as their own linked technician, whatever the URL says.
    /// </summary>
    public async Task<IActionResult> Job(Guid id, Guid technicianId)
    {
        var linkedTechnicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        if (!User.CanManageSchedule())
        {
            technicianId = linkedTechnicianId ?? Guid.Empty;
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
            Job = JobDetailsViewModel.FromEntity(job, lookup),
            CanCheckOffBuildings = linkedTechnicianId == technician.Id
        });
    }

    /// <summary>
    /// Checks a building off the signed-in technician's checklist, or puts it back on the to-do list. Only the user's
    /// own linked technician counts, and they must be assigned to the job; otherwise 404. Answers the checklist script
    /// (which asks for JSON) with the building's new state, and a plain form post by going back to the job page.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetBuildingComplete(Guid id, Guid buildingId, bool complete)
    {
        var technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        var building = technicianId is null ? null : await _jobService.SetBuildingCompleteAsync(id, buildingId, technicianId.Value, complete);
        if (building is null)
        {
            return NotFound();
        }

        if (Request.GetTypedHeaders().Accept.Any(a => a.MediaType == "application/json"))
        {
            var technicians = (await _technicianService.GetAllAsync()).ToLookup();
            var model = BuildingViewModel.FromEntity(building, technicians);
            return Json(new { model.IsComplete, model.CompletedText });
        }

        return RedirectToAction(nameof(Job), null, new { id, technicianId }, "checklist");
    }
}
