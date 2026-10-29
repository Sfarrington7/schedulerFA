// Purpose: A technician's request for paid time off (the PtoRequests table): a continuous period from
// StartDate/StartTime to EndDate/EndTime, its review status, and who reviewed it.
namespace FireAlarmScheduler.Entities;

public class PtoRequest
{
    public Guid Id { get; set; }
    public Guid TechnicianId { get; set; }
    public DateOnly StartDate { get; set; }
    public TimeOnly StartTime { get; set; }
    public DateOnly EndDate { get; set; }
    public TimeOnly EndTime { get; set; }
    public string? Reason { get; set; }
    public PtoStatus Status { get; set; } = PtoStatus.Pending;
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>Email of the administrator who last approved or denied the request.</summary>
    public string? ReviewedBy { get; set; }

    public DateTime StartsAt => StartDate.ToDateTime(StartTime);
    public DateTime EndsAt => EndDate.ToDateTime(EndTime);

    /// <summary>Pending and approved requests block the time; denied ones don't.</summary>
    public bool IsActive => Status != PtoStatus.Denied;

    /// <summary>True when this period and [<paramref name="start"/>, <paramref name="end"/>) share any time; touching ends don't count.</summary>
    public bool Overlaps(DateTime start, DateTime end) => StartsAt < end && start < EndsAt;
}
