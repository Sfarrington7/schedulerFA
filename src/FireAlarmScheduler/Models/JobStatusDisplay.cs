// Purpose: Display text and badge styling for job statuses in views.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Models;

public static class JobStatusDisplay
{
    public static string ToDisplayName(this JobStatus status) => status switch
    {
        JobStatus.InProgress => "In Progress",
        _ => status.ToString()
    };

    public static string BadgeClass(this JobStatus status) => status switch
    {
        JobStatus.Scheduled => "text-bg-primary",
        JobStatus.InProgress => "text-bg-warning",
        JobStatus.Completed => "text-bg-success",
        _ => "text-bg-secondary"
    };
}
