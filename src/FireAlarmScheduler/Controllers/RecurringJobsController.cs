// Purpose: Team lead's Recurring jobs page: list saved recurring jobs, start a new job from one, or remove one.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageSchedule)]
public class RecurringJobsController : Controller
{
    private readonly RecurringJobService _recurringJobService;

    public RecurringJobsController(RecurringJobService recurringJobService)
    {
        _recurringJobService = recurringJobService;
    }

    public async Task<IActionResult> Index()
    {
        var model = (await _recurringJobService.GetAllAsync())
            .Select(r => new RecurringJobListItemViewModel(r.RecurringJob.Id, r.RecurringJob.Name, r.RecurringJob.CustomerName,
                r.RecurringJob.SiteName, r.RecurringJob.Address, r.JobCount, r.LatestStartDate))
            .ToList();
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!await _recurringJobService.DeleteAsync(id))
        {
            return NotFound();
        }

        TempData["Message"] = "Recurring job removed. Its past jobs are unchanged.";
        return RedirectToAction(nameof(Index));
    }
}
