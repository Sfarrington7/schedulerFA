// Purpose: Job pages for team leads and administrators: list, view, create (new or from a recurring job), edit, and
// delete jobs and assign technicians.
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
    private readonly RecurringJobService _recurringJobService;
    private readonly TimeProvider _timeProvider;

    public JobsController(JobService jobService, TechnicianService technicianService, RecurringJobService recurringJobService,
        TimeProvider timeProvider)
    {
        _jobService = jobService;
        _technicianService = technicianService;
        _recurringJobService = recurringJobService;
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

    /// <summary>
    /// Shows the new-job form. With <paramref name="recurringJobId"/> the form starts from that recurring job's
    /// saved details and building list, which can still be changed for this job.
    /// </summary>
    public async Task<IActionResult> Create(Guid? recurringJobId)
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var form = new JobFormModel { StartDate = today, EndDate = today };
        if (recurringJobId is { } id && await _recurringJobService.GetAsync(id) is { } recurringJob)
        {
            form.RecurringJobId = recurringJob.Id;
            form.Name = recurringJob.Name;
            form.CustomerName = recurringJob.CustomerName;
            form.SiteName = recurringJob.SiteName;
            form.Address = recurringJob.Address;
            form.Buildings = recurringJob.Buildings.OrderBy(b => b.SortOrder)
                .Select(b => new BuildingRowModel { RecurringBuildingId = b.Id, Name = b.Name, Notes = b.Notes })
                .ToList();
        }

        await FillChoicesAsync(form);
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
                TempData["Message"] = job.Buildings.Count > 0
                    ? $"Job created with {job.Buildings.Count} building(s). Upload each building's spreadsheets below."
                    : "Job created. Add its buildings below.";
                return RedirectToAction(nameof(Details), new { id = job.Id });
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        await FillChoicesAsync(form);
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
        await FillChoicesAsync(form);
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

        await FillChoicesAsync(form);
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

    /// <summary>Fills the form's technician checkboxes and recurring-job choices, and names a linked recurring job on edit.</summary>
    private async Task FillChoicesAsync(JobFormModel form)
    {
        form.AvailableTechnicians = (await _technicianService.GetAllAsync())
            .Select(t => new TechnicianOption(t.Id, t.FullName))
            .ToList();

        if (form.IsNew)
        {
            form.AvailableRecurringJobs = (await _recurringJobService.GetAllAsync())
                .Select(r => new RecurringJobOption(r.RecurringJob.Id, r.RecurringJob.Name,
                    string.Join(" · ", new[] { r.RecurringJob.CustomerName, r.RecurringJob.Address }.Where(x => !string.IsNullOrEmpty(x)))))
                .ToList();

        }
        else if (form.Id is { } jobId && (await _jobService.GetAsync(jobId))?.RecurringJobId is { } linkedId)
        {
            form.LinkedRecurringJobName = (await _recurringJobService.GetAsync(linkedId))?.Name;
        }
    }
}
