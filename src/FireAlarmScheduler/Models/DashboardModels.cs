// Purpose: The dashboard's summary cards (jobs today, upcoming jobs, pending PTO, conflicts) and the count badges
// shown beside PTO Requests and Conflicts in the sidebar.
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Models;

/// <summary>How a summary card's detail line is colored: plain, all clear (green), needs review (amber), or a problem (red).</summary>
public enum SummaryTone
{
    Neutral,
    Clear,
    Attention,
    Problem
}

/// <summary>One summary card: a label, a big number, a one-line detail, and the page it links to.</summary>
public record SummaryCardViewModel(string Label, int Count, string Detail, SummaryTone Tone, string Controller, string Action);

public static class DashboardSummary
{
    /// <summary>How far ahead the "Upcoming" card looks, counting today as day 1.</summary>
    public const int UpcomingDays = 14;

    /// <summary>
    /// Builds the four cards. Jobs today are the jobs running today (as listed under "Current jobs"); upcoming jobs start
    /// after today and within <see cref="UpcomingDays"/> days; PTO and conflict counts come from <paramref name="alerts"/>.
    /// </summary>
    public static List<SummaryCardViewModel> Build(IReadOnlyList<JobSummaryViewModel> jobs, ScheduleAlerts alerts,
        Func<Guid, string> technicianName)
    {
        var today = alerts.Today;
        var running = jobs.Where(j => j.OverlapsRange(today, today)).OrderBy(j => j.StartTime).ThenBy(j => j.Name).ToList();
        var upcoming = jobs.Where(j => j.StartDate > today && j.StartDate < today.AddDays(UpcomingDays))
            .OrderBy(j => j.StartDate).ThenBy(j => j.StartTime).ThenBy(j => j.Name).ToList();

        return
        [
            new("Jobs today", running.Count, TodayDetail(running, today), SummaryTone.Neutral, "Schedule", "Index"),
            new($"Upcoming ({UpcomingDays} days)", upcoming.Count,
                upcoming.Count == 0 ? $"Nothing booked in the next {UpcomingDays} days" : $"Next: {upcoming[0].Name}, {upcoming[0].StartDate:ddd, MMM d}",
                SummaryTone.Neutral, "Jobs", "Calendar"),
            new("Pending PTO", alerts.PendingPtoCount,
                alerts.PendingPtoCount == 0 ? "All caught up" : alerts.PendingPtoCount == 1 ? "1 request needs review" : $"{alerts.PendingPtoCount} requests need review",
                alerts.PendingPtoCount == 0 ? SummaryTone.Clear : SummaryTone.Attention, "PtoRequests", "Index"),
            new("Conflicts", alerts.ConflictCount, ConflictDetail(alerts.Conflicts, technicianName),
                alerts.ConflictCount == 0 ? SummaryTone.Clear : SummaryTone.Problem, "Conflicts", "Index")
        ];
    }

    private static string TodayDetail(List<JobSummaryViewModel> running, DateOnly today)
    {
        if (running.Count == 0)
        {
            return "No jobs running today";
        }

        var first = running[0];
        var detail = first.DayCount == 1
            ? first.Name
            : $"{first.Name} · day {today.DayNumber - first.StartDate.DayNumber + 1} of {first.DayCount}";
        return running.Count == 1 ? detail : $"{detail} + {running.Count - 1} more";
    }

    /// <summary>Describes the earliest conflict, e.g. "John Smith double-booked on Thu, Nov 19 + 1 more".</summary>
    private static string ConflictDetail(IReadOnlyList<ScheduleConflict> conflicts, Func<Guid, string> technicianName)
    {
        if (conflicts.Count == 0)
        {
            return "No scheduling conflicts";
        }

        var first = conflicts[0];
        var what = first.Kind == ConflictKind.DoubleBooked ? "double-booked" : "booked during PTO";
        var detail = $"{technicianName(first.TechnicianId)} {what} on {first.FirstDate:ddd, MMM d}";
        return conflicts.Count == 1 ? detail : $"{detail} + {conflicts.Count - 1} more";
    }
}

/// <summary>Counts shown beside PTO Requests and Conflicts in the sidebar; a zero count shows no badge.</summary>
public record NavAlertCountsViewModel(int PendingPto, int Conflicts)
{
    /// <summary>Badge text: the number, or "99+" so a large count can't stretch the sidebar.</summary>
    public static string BadgeText(int count) => count > 99 ? "99+" : count.ToString();
}
