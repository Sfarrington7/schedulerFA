// Purpose: View and form models for PTO: the technician's request form and request list, the administrators'
// review page, and the scheduling conflicts page.
using System.ComponentModel.DataAnnotations;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Models;

public class PtoFormModel
{
    [Display(Name = "First day off")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "Starting at")]
    public TimeOnly StartTime { get; set; } = new(0, 0);

    [Display(Name = "Last day off")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "Ending at")]
    public TimeOnly EndTime { get; set; } = new(23, 59);

    [StringLength(500), Display(Name = "Reason (optional)")]
    public string? Reason { get; set; }

    public PtoInput ToInput() => new(StartDate, StartTime, EndDate, EndTime, Reason);
}

public class PtoRequestViewModel
{
    public Guid Id { get; init; }
    public string TechnicianName { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public string? Reason { get; init; }
    public PtoStatus Status { get; init; }
    public DateTime RequestedAtUtc { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }

    /// <summary>Jobs the technician is assigned to during this time off (shown to administrators).</summary>
    public List<ConflictJobViewModel> ConflictingJobs { get; init; } = [];

    public string PeriodText => StartsAt.Date == EndsAt.Date
        ? $"{StartsAt:ddd, MMM d, yyyy} · {StartsAt:h:mm tt} – {EndsAt:h:mm tt}"
        : $"{StartsAt:ddd, MMM d, h:mm tt} – {EndsAt:ddd, MMM d, yyyy, h:mm tt}";

    public static PtoRequestViewModel FromEntity(PtoRequest request, string technicianName, List<ConflictJobViewModel>? conflicts = null) => new()
    {
        Id = request.Id,
        TechnicianName = technicianName,
        StartsAt = request.StartsAt,
        EndsAt = request.EndsAt,
        Reason = request.Reason,
        Status = request.Status,
        RequestedAtUtc = request.RequestedAtUtc,
        ReviewedBy = request.ReviewedBy,
        ReviewedAtUtc = request.ReviewedAtUtc,
        ConflictingJobs = conflicts ?? []
    };
}

/// <summary>A job involved in a conflict, with the affected days.</summary>
public record ConflictJobViewModel(Guid JobId, string JobName, string DatesText, string HoursText)
{
    public static ConflictJobViewModel From(Job job, DateOnly first, DateOnly last) => new(
        job.Id,
        job.Name,
        first == last ? first.ToString("ddd, MMM d") : $"{first:ddd, MMM d} – {last:ddd, MMM d}",
        $"{job.StartTime:h:mm tt} – {job.EndTime:h:mm tt}");
}

public class MyPtoViewModel
{
    /// <summary>False when the signed-in account has no linked technician, so it can't request PTO.</summary>
    public bool IsLinked { get; init; }
    public string TechnicianName { get; init; } = "";
    public PtoFormModel Form { get; init; } = new();
    public List<PtoRequestViewModel> Requests { get; init; } = [];
}

public enum PtoRequestFilter
{
    Pending,
    All
}

public class PtoReviewViewModel
{
    public PtoRequestFilter Filter { get; init; }
    public int PendingCount { get; init; }
    public List<PtoRequestViewModel> Requests { get; init; } = [];
}

public record ConflictRowViewModel(string TechnicianName, ConflictJobViewModel Job, ConflictJobViewModel? OtherJob, PtoRequestViewModel? Pto);

public class ConflictsViewModel
{
    public DateOnly From { get; init; }
    public List<ConflictRowViewModel> DoubleBookings { get; init; } = [];
    public List<ConflictRowViewModel> PtoConflicts { get; init; } = [];
}
