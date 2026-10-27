// Purpose: Team lead dashboard: current and upcoming jobs, the team, and the next two weeks by date.
// Accounts that can't manage the schedule (technicians) are sent to their own schedule instead.
using System.Diagnostics;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

public class HomeController : Controller
{
    private const int DashboardDays = 14;
    private const int MaxUpcomingJobs = 10;

    private readonly JobService _jobService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;

    public HomeController(JobService jobService, TechnicianService technicianService, TimeProvider timeProvider)
    {
        _jobService = jobService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        if (!User.CanManageSchedule())
        {
            return RedirectToAction("Index", "TechnicianView");
        }

        var today = ScheduleCalendar.Today(_timeProvider);
        var technicians = await _technicianService.GetAllAsync();
        var lookup = technicians.ToLookup();
        var jobs = (await _jobService.GetAllAsync()).Select(j => JobSummaryViewModel.FromEntity(j, lookup)).ToList();
        var lastDay = today.AddDays(DashboardDays - 1);

        var model = new DashboardViewModel
        {
            Today = today,
            CurrentJobs = jobs.Where(j => j.StartDate <= today && j.EndDate >= today).ToList(),
            UpcomingJobs = jobs.Where(j => j.StartDate > today).Take(MaxUpcomingJobs).ToList(),
            Technicians = technicians
                .Select(t => new TechnicianListItemViewModel(t.Id, t.FirstName, t.LastName,
                    jobs.Count(j => j.EndDate >= today && j.Technicians.Any(a => a.Id == t.Id))))
                .ToList(),
            Days = ScheduleCalendar.DaysBetween(today, lastDay)
                .Select(day => new ScheduleDayViewModel(day, jobs.Where(j => j.StartDate <= day && j.EndDate >= day).ToList()))
                .Where(d => d.Jobs.Count > 0)
                .ToList()
        };

        return View(model);
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
