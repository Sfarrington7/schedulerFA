// Purpose: Join record linking a job to one assigned technician (the JobAssignments table).
namespace FireAlarmScheduler.Entities;

public class JobAssignment
{
    public Guid JobId { get; set; }
    public Guid TechnicianId { get; set; }
}
