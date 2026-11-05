// Purpose: Stored record for one building within a job (the Buildings table). Technicians check each building off as
// they finish it, so the job's building list doubles as its progress checklist.
namespace FireAlarmScheduler.Entities;

public class Building
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string Name { get; set; } = "";
    public string? Notes { get; set; }

    /// <summary>Keeps buildings in the order they were added; SQL rows have no inherent order.</summary>
    public int SortOrder { get; set; }

    /// <summary>When a technician checked the building off (UTC), or null while it still needs doing.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>The technician who checked the building off; cleared if that technician is deleted.</summary>
    public Guid? CompletedByTechnicianId { get; set; }

    public bool IsComplete => CompletedAtUtc is not null;
}
