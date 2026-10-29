// Purpose: Administrators' PTO review page: list requests (pending by default), see which jobs each one
// overlaps, and approve or deny them. Denying an approved request revokes it.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManagePto)]
public class PtoRequestsController : Controller
{
    private readonly PtoService _ptoService;
    private readonly ScheduleConflictService _conflictService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;

    public PtoRequestsController(PtoService ptoService, ScheduleConflictService conflictService,
        TechnicianService technicianService, TimeProvider timeProvider)
    {
        _ptoService = ptoService;
        _conflictService = conflictService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index(PtoRequestFilter filter = PtoRequestFilter.Pending)
    {
        var all = await _ptoService.GetAllAsync();
        var shown = filter == PtoRequestFilter.Pending ? all.Where(p => p.Status == PtoStatus.Pending).ToList() : all;
        var technicians = (await _technicianService.GetAllAsync()).ToLookup();
        var conflicts = await _conflictService.GetJobConflictsForRequestsAsync(
            shown.Where(p => p.IsActive), ScheduleCalendar.Today(_timeProvider));

        return View(new PtoReviewViewModel
        {
            Filter = filter,
            PendingCount = all.Count(p => p.Status == PtoStatus.Pending),
            Requests = shown.Select(p => PtoRequestViewModel.FromEntity(
                    p,
                    technicians.TryGetValue(p.TechnicianId, out var tech) ? tech.FullName : "Unknown technician",
                    conflicts.GetValueOrDefault(p.Id)?.Select(c => ConflictJobViewModel.From(c.Job, c.FirstDate, c.LastDate)).ToList()))
                .ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Approve(Guid id, PtoRequestFilter filter = PtoRequestFilter.Pending) =>
        ReviewAsync(id, PtoStatus.Approved, filter, "PTO approved.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Deny(Guid id, PtoRequestFilter filter = PtoRequestFilter.Pending) =>
        ReviewAsync(id, PtoStatus.Denied, filter, "PTO denied.");

    private async Task<IActionResult> ReviewAsync(Guid id, PtoStatus decision, PtoRequestFilter filter, string message)
    {
        if (!await _ptoService.ReviewAsync(id, decision, User.Identity?.Name ?? "unknown"))
        {
            return NotFound();
        }

        TempData["Message"] = message;
        return RedirectToAction(nameof(Index), new { filter });
    }
}
