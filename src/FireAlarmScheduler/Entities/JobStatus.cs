// Purpose: Lifecycle states a fire alarm testing job can be in.
namespace FireAlarmScheduler.Entities;

public enum JobStatus
{
    Scheduled,
    InProgress,
    Completed
}
