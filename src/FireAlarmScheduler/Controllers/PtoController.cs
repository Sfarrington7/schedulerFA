// Purpose: The technician's PTO page: request time off, see each request's status, and cancel pending requests.
// The technician is always the one linked to the signed-in account; form input never chooses it.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.RequestPto)]
public class PtoController : Controller
{
    private readonly PtoService _ptoService;
    private readonly TechnicianService _technicianService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TimeProvider _timeProvider;

    public PtoController(PtoService ptoService, TechnicianService technicianService, UserManager<ApplicationUser> userManager,
        TimeProvider timeProvider)
    {
        _ptoService = ptoService;
        _technicianService = technicianService;
        _userManager = userManager;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        return View(await BuildPageAsync(new PtoFormModel { StartDate = today, EndDate = today }));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(Prefix = nameof(MyPtoViewModel.Form))] PtoFormModel form)
    {
        var technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        if (technicianId is null)
        {
            return Forbid();
        }

        if (ModelState.IsValid)
        {
            try
            {
                await _ptoService.SubmitAsync(technicianId.Value, form.ToInput());
                TempData["Message"] = "PTO request sent for approval.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationFailedException ex)
            {
                foreach (var (field, message) in ex.Errors)
                {
                    ModelState.AddModelError(field.Length == 0 ? "" : $"{nameof(MyPtoViewModel.Form)}.{field}", message);
                }
            }
        }

        return View(await BuildPageAsync(form));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        var result = technicianId is null ? PtoCancelResult.NotFound : await _ptoService.CancelAsync(technicianId.Value, id);

        switch (result)
        {
            case PtoCancelResult.NotFound:
                return NotFound();
            case PtoCancelResult.NotPending:
                TempData["Error"] = "Only pending requests can be cancelled. Ask an administrator to change a reviewed request.";
                break;
            default:
                TempData["Message"] = "PTO request cancelled.";
                break;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<MyPtoViewModel> BuildPageAsync(PtoFormModel form)
    {
        var technicianId = await _userManager.GetLinkedTechnicianIdAsync(User);
        var technician = technicianId is null ? null : await _technicianService.GetAsync(technicianId.Value);
        if (technician is null)
        {
            return new MyPtoViewModel { IsLinked = false, Form = form };
        }

        return new MyPtoViewModel
        {
            IsLinked = true,
            TechnicianName = technician.FullName,
            Form = form,
            Requests = (await _ptoService.GetForTechnicianAsync(technician.Id))
                .Select(p => PtoRequestViewModel.FromEntity(p, technician.FullName))
                .ToList()
        };
    }
}
