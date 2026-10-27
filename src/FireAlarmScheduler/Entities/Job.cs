// Purpose: Stored record for a scheduled fire alarm testing job (the Jobs table).
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
    public List<JobAssignment> Assignments { get; set; } = [];
    public List<Building> Buildings { get; set; } = [];

    public IReadOnlyList<Guid> AssignedTechnicianIds => Assignments.Select(a => a.TechnicianId).ToList();

    /// <summary>True when the job runs on any day from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public bool OverlapsRange(DateOnly from, DateOnly to) => StartDate <= to && EndDate >= from;

    /// <summary>
    /// Makes the assignments match <paramref name="technicianIds"/>, removing and adding rows only where they
    /// differ so EF Core updates the join table without re-inserting unchanged assignments.
    /// </summary>
    public void SetAssignedTechnicians(IEnumerable<Guid> technicianIds)
    {
        var wanted = technicianIds.Distinct().ToList();
        Assignments.RemoveAll(a => !wanted.Contains(a.TechnicianId));
        foreach (var id in wanted.Where(id => Assignments.All(a => a.TechnicianId != id)))
        {
            Assignments.Add(new JobAssignment { JobId = Id, TechnicianId = id });
        }
    }
}
