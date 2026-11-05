// Purpose: The sidebar's "Time off" links (PTO Requests, Conflicts) with badges counting requests waiting for review
// and current conflicts. The layout shows it only to accounts that may review PTO.
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

public class TimeOffNavViewComponent : ViewComponent
{
    private readonly ScheduleAlertService _alertService;

    public TimeOffNavViewComponent(ScheduleAlertService alertService) => _alertService = alertService;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var alerts = await _alertService.GetAsync();
        return View(new NavAlertCountsViewModel(alerts.PendingPtoCount, alerts.ConflictCount));
    }
}
