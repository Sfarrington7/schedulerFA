// Purpose: Works out what needs a team lead's attention: PTO requests waiting for review and scheduling conflicts
// from today on, counted the same way as the PTO Requests and Conflicts pages. Used by the dashboard's summary
// cards and the sidebar's count badges; the result is worked out once per request.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

/// <summary>Pending PTO requests and current/future conflicts (earliest first), as of <see cref="Today"/>.</summary>
public record ScheduleAlerts(DateOnly Today, int PendingPtoCount, IReadOnlyList<ScheduleConflict> Conflicts)
{
    public int ConflictCount => Conflicts.Count;
}

public class ScheduleAlertService
{
    private readonly PtoService _ptoService;
    private readonly ScheduleConflictService _conflictService;
    private readonly TimeProvider _timeProvider;
    private Task<ScheduleAlerts>? _alerts;

    public ScheduleAlertService(PtoService ptoService, ScheduleConflictService conflictService, TimeProvider timeProvider)
    {
        _ptoService = ptoService;
        _conflictService = conflictService;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// The current alerts. The service is scoped to one web request, so the dashboard and the sidebar share a single
    /// lookup; a later request sees any changes.
    /// </summary>
    public Task<ScheduleAlerts> GetAsync() => _alerts ??= LoadAsync();

    private async Task<ScheduleAlerts> LoadAsync()
    {
        var today = ScheduleCalendar.Today(_timeProvider);
        var pending = (await _ptoService.GetAllAsync()).Count(p => p.Status == PtoStatus.Pending);
        var conflicts = (await _conflictService.GetConflictsAsync(today))
            .OrderBy(c => c.FirstDate).ThenBy(c => c.Kind)
            .ToList();
        return new ScheduleAlerts(today, pending, conflicts);
    }
}
