// Purpose: Job pages for team leads and administrators: list, view, create, edit, and delete jobs and assign technicians.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageSchedule)]
public class JobsController : Controller
{
    private readonly JobService _jobService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;

    public JobsController(JobService jobService, TechnicianService technicianService, TimeProvider timeProvider)
    {
        _jobService = jobService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        var lookup = (await _technicianService.GetAllAsync()).ToLookup();
        var model = (await _jobService.GetAllAsync()).Select(j => JobSummaryViewModel.FromEntity(j, lookup)).ToList();
        return View(model);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var job = await _jobService.GetAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        var lookup = (await _technicianService.GetAllAsync()).ToLookup();
        return View(JobDetailsViewModel.FromEntity(job, lookup));
    }

    public async Task<IActionResult> Create()
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var form = new JobFormModel { StartDate = today, EndDate = today };
        await FillTechniciansAsync(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(JobFormModel form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var job = await _jobService.CreateAsync(form.ToInput());
                TempData["Message"] = "Job created. Add its buildings below.";
                return RedirectToAction(nameof(Details), new { id = job.Id });
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        await FillTechniciansAsync(form);
        return View(form);
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var job = await _jobService.GetAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        var form = JobFormModel.FromEntity(job);
        await FillTechniciansAsync(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, JobFormModel form)
    {
        form.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                if (!await _jobService.UpdateAsync(id, form.ToInput()))
                {
                    return NotFound();
                }

                TempData["Message"] = "Job updated.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        await FillTechniciansAsync(form);
        return View(form);
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var job = await _jobService.GetAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        var lookup = (await _technicianService.GetAllAsync()).ToLookup();
        return View(JobDetailsViewModel.FromEntity(job, lookup));
    }

    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        if (!await _jobService.DeleteAsync(id))
        {
            return NotFound();
        }

        TempData["Message"] = "Job deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task FillTechniciansAsync(JobFormModel form)
    {
        form.AvailableTechnicians = (await _technicianService.GetAllAsync())
            .Select(t => new TechnicianOption(t.Id, t.FullName))
            .ToList();
    }
}
