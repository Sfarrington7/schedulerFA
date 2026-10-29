// Purpose: The editable fields of a job, passed from controllers to JobService for create and update, plus its
// recurring-job choice (an existing recurring job to link to, or whether to save it as a new one) and, on create,
// its buildings.
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
    bool SaveAsRecurring = false,
    IReadOnlyList<BuildingInput>? Buildings = null);

/// <summary>
/// A building entered on the Create Job form. Rows with a blank name are ignored. <paramref name="RecurringBuildingId"/>
/// marks a row that came from the recurring job's building list; its name is fixed to that building's saved name.
/// </summary>
public record BuildingInput(string? Name, string? Notes, Guid? RecurringBuildingId = null);
