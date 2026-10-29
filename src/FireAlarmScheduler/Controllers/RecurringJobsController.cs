// Purpose: Team lead's recurring job pages: list saved recurring jobs, view and edit one's standing building list
// (adding a building also adds it to the series' upcoming jobs), start a new job from one, or remove one.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageSchedule)]
public class RecurringJobsController : Controller
{
    private readonly RecurringJobService _recurringJobService;
    private readonly JobService _jobService;

    public RecurringJobsController(RecurringJobService recurringJobService, JobService jobService)
    {
        _recurringJobService = recurringJobService;
        _jobService = jobService;
    }

    public async Task<IActionResult> Index()
    {
        var model = (await _recurringJobService.GetAllAsync())
            .Select(r => new RecurringJobListItemViewModel(r.RecurringJob.Id, r.RecurringJob.Name, r.RecurringJob.CustomerName,
                r.RecurringJob.SiteName, r.RecurringJob.Address, r.JobCount, r.LatestStartDate,
                r.RecurringJob.Buildings.Select(b => b.Name).ToList()))
            .ToList();
        return View(model);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var recurringJob = await _recurringJobService.GetAsync(id);
        if (recurringJob is null)
        {
            return NotFound();
        }

        return View(new RecurringJobDetailsViewModel
        {
            Id = recurringJob.Id,
            Name = recurringJob.Name,
            CustomerName = recurringJob.CustomerName,
            SiteName = recurringJob.SiteName,
            Address = recurringJob.Address,
            Buildings = recurringJob.Buildings.OrderBy(b => b.SortOrder).Select(b => new BuildingViewModel(b.Id, b.Name, b.Notes, [])).ToList(),
            NewBuilding = new BuildingFormModel { JobName = recurringJob.Name }
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBuilding(Guid id, [Bind(Prefix = nameof(RecurringJobDetailsViewModel.NewBuilding))] BuildingFormModel form)
    {
        try
        {
            if (!await _jobService.AddRecurringBuildingAsync(id, form.Name, form.Notes))
            {
                return NotFound();
            }

            TempData["Message"] = $"Added {form.Name.Trim()} to this recurring job and its upcoming jobs.";
        }
        catch (ValidationFailedException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBuilding(Guid id, Guid buildingId)
    {
        if (!await _recurringJobService.RemoveBuildingAsync(id, buildingId))
        {
            return NotFound();
        }

        TempData["Message"] = "Building removed from the list for future jobs. Existing jobs keep it.";
        return RedirectToAction(nameof(Details), new { id });
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
