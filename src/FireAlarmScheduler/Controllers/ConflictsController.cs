// Purpose: Administrators' scheduling conflicts page: technicians double-booked on overlapping jobs, and
// technicians assigned to jobs during their approved PTO, from today onward.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManagePto)]
public class ConflictsController : Controller
{
    private readonly ScheduleConflictService _conflictService;
    private readonly TechnicianService _technicianService;
    private readonly TimeProvider _timeProvider;

    public ConflictsController(ScheduleConflictService conflictService, TechnicianService technicianService, TimeProvider timeProvider)
    {
        _conflictService = conflictService;
        _technicianService = technicianService;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index()
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var technicians = (await _technicianService.GetAllAsync()).ToLookup();
        string NameOf(Guid id) => technicians.TryGetValue(id, out var t) ? t.FullName : "Unknown technician";

        var rows = (await _conflictService.GetConflictsAsync(today))
            .OrderBy(c => c.FirstDate).ThenBy(c => NameOf(c.TechnicianId))
            .Select(c => (c.Kind, Row: new ConflictRowViewModel(
                NameOf(c.TechnicianId),
                ConflictJobViewModel.From(c.Job, c.FirstDate, c.LastDate),
                c.OtherJob is null ? null : ConflictJobViewModel.From(c.OtherJob, c.FirstDate, c.LastDate),
                c.Pto is null ? null : PtoRequestViewModel.FromEntity(c.Pto, NameOf(c.TechnicianId)))))
            .ToList();

        return View(new ConflictsViewModel
        {
            From = today,
            DoubleBookings = rows.Where(r => r.Kind == ConflictKind.DoubleBooked).Select(r => r.Row).ToList(),
            PtoConflicts = rows.Where(r => r.Kind == ConflictKind.OnPto).Select(r => r.Row).ToList()
        });
    }
}
