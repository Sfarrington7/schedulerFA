// Purpose: Team management pages for team leads and administrators: list, add, edit, and remove technicians.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageSchedule)]
public class TechniciansController : Controller
{
    private readonly TechnicianService _technicianService;
    private readonly JobService _jobService;
    private readonly TimeProvider _timeProvider;

    public TechniciansController(TechnicianService technicianService, JobService jobService, TimeProvider timeProvider)
    {
        _technicianService = technicianService;
        _jobService = jobService;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var jobs = await _jobService.GetAllAsync();
        var model = (await _technicianService.GetAllAsync())
            .Select(t => new TechnicianListItemViewModel(t.Id, t.FirstName, t.LastName,
                jobs.Count(j => j.EndDate >= today && j.AssignedTechnicianIds.Contains(t.Id))))
            .ToList();

        return View(model);
    }

    public IActionResult Create() => View(new TechnicianFormModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(TechnicianFormModel form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            var technician = await _technicianService.AddAsync(form.FirstName, form.LastName);
            TempData["Message"] = $"Added {technician.FullName}.";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationFailedException ex)
        {
            ModelState.AddErrors(ex);
            return View(form);
        }
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var technician = await _technicianService.GetAsync(id);
        if (technician is null)
        {
            return NotFound();
        }

        return View(new TechnicianFormModel { Id = technician.Id, FirstName = technician.FirstName, LastName = technician.LastName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, TechnicianFormModel form)
    {
        form.Id = id;
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        try
        {
            if (!await _technicianService.UpdateAsync(id, form.FirstName, form.LastName))
            {
                return NotFound();
            }

            TempData["Message"] = "Technician updated.";
            return RedirectToAction(nameof(Index));
        }
        catch (ValidationFailedException ex)
        {
            ModelState.AddErrors(ex);
            return View(form);
        }
    }

    public async Task<IActionResult> Delete(Guid id)
    {
        var technician = await _technicianService.GetAsync(id);
        if (technician is null)
        {
            return NotFound();
        }

        return View(new TechnicianListItemViewModel(technician.Id, technician.FirstName, technician.LastName, 0));
    }

    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        if (!await _technicianService.DeleteAsync(id))
        {
            return NotFound();
        }

        TempData["Message"] = "Technician removed and unassigned from their jobs.";
        return RedirectToAction(nameof(Index));
    }
}
