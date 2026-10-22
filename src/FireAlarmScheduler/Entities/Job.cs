// Purpose: Stored record for a scheduled fire alarm testing job.
// Holds its schedule, assigned technicians, and the buildings (with their files) it covers.
namespace FireAlarmScheduler.Entities;

public class Job
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? CustomerName { get; set; }
    public string? SiteName { get; set; }
    public string Address { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Daily start time on each day of the job.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Daily end time on each day of the job.</summary>
    public TimeOnly EndTime { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Scheduled;
    public string? Notes { get; set; }
    public List<Guid> AssignedTechnicianIds { get; set; } = [];
    public List<Building> Buildings { get; set; } = [];

    /// <summary>True when the job runs on any day from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public bool OverlapsRange(DateOnly from, DateOnly to) => StartDate <= to && EndDate >= from;
}
