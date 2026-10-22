// Purpose: The editable fields of a job, passed from controllers to JobService for create and update.
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
    IReadOnlyCollection<Guid> AssignedTechnicianIds);
