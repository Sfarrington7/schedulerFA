// Purpose: Badge styling for PTO request statuses in views.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Models;

public static class PtoStatusDisplay
{
    public static string BadgeClass(this PtoStatus status) => status switch
    {
        PtoStatus.Pending => "text-bg-warning",
        PtoStatus.Approved => "text-bg-success",
        _ => "text-bg-secondary"
    };
}
