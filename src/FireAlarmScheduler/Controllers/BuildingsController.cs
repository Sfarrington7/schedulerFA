// Purpose: Team lead actions for a job's buildings: add, rename or change notes, and remove. Technicians check
// buildings off from the technician view instead.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Route("Jobs/{jobId:guid}/Buildings")]
public class BuildingsController : Controller
{
    private readonly JobService _jobService;

    public BuildingsController(JobService jobService)
    {
        _jobService = jobService;
    }

    [HttpPost(""), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Create(Guid jobId, [Bind(Prefix = nameof(JobDetailsViewModel.NewBuilding))] BuildingFormModel form)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Enter a building name.";
            return RedirectToAction("Details", "Jobs", new { id = jobId });
        }

        try
        {
            var building = await _jobService.AddBuildingAsync(jobId, form.Name, form.Notes);
            if (building is null)
            {
                return NotFound();
            }

            TempData["Message"] = $"Added {building.Name}.";
        }
        catch (ValidationFailedException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction("Details", "Jobs", new { id = jobId });
    }

    [HttpGet("{buildingId:guid}/Edit"), Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Edit(Guid jobId, Guid buildingId)
    {
        var job = await _jobService.GetAsync(jobId);
        var building = job?.Buildings.FirstOrDefault(b => b.Id == buildingId);
        if (job is null || building is null)
        {
            return NotFound();
        }

        return View(new BuildingFormModel { JobId = jobId, BuildingId = buildingId, JobName = job.Name, Name = building.Name, Notes = building.Notes });
    }

    [HttpPost("{buildingId:guid}/Edit"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Edit(Guid jobId, Guid buildingId, BuildingFormModel form)
    {
        form.JobId = jobId;
        form.BuildingId = buildingId;
        if (ModelState.IsValid)
        {
            try
            {
                if (!await _jobService.UpdateBuildingAsync(jobId, buildingId, form.Name, form.Notes))
                {
                    return NotFound();
                }

                TempData["Message"] = "Building updated.";
                return RedirectToAction("Details", "Jobs", new { id = jobId });
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        form.JobName = (await _jobService.GetAsync(jobId))?.Name ?? "";
        return View(form);
    }

    [HttpPost("{buildingId:guid}/Delete"), ValidateAntiForgeryToken, Authorize(Policy = AppPolicies.ManageSchedule)]
    public async Task<IActionResult> Delete(Guid jobId, Guid buildingId)
    {
        if (!await _jobService.DeleteBuildingAsync(jobId, buildingId))
        {
            return NotFound();
        }

        TempData["Message"] = "Building removed.";
        return RedirectToAction("Details", "Jobs", new { id = jobId });
    }
}
