// Purpose: The editable fields of a job, passed from controllers to JobService for create and update,
// plus its recurring-job choice: an existing recurring job to link to, or whether to save it as a new one.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public record JobInput(
    string? Name,
    string? CustomerName,
    string? SiteName,
    string? Address,
    DateOnly StartDate,
    DateOnly EndDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    JobStatus Status,
    string? Notes,
    IReadOnlyCollection<Guid> AssignedTechnicianIds,
    Guid? RecurringJobId = null,
    bool SaveAsRecurring = false);
