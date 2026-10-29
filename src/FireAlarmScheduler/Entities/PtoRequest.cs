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

    /// <summary>
    /// The part of this period that falls on <paramref name="day"/>, as times on that day, or null if none does.
    /// <c>End</c> is null when the time off runs to the end of the day (into the next day or until 11:59 PM).
    /// </summary>
    public (TimeOnly Start, TimeOnly? End)? PortionOn(DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var start = StartsAt > dayStart ? StartsAt : dayStart;
        var end = EndsAt < dayEnd ? EndsAt : dayEnd;
        if (start >= end)
        {
            return null;
        }

        var runsToEndOfDay = end >= dayEnd.AddMinutes(-1);
        return (TimeOnly.FromDateTime(start), runsToEndOfDay ? null : TimeOnly.FromDateTime(end));
    }
}
