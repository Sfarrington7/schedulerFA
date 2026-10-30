// Purpose: Finds scheduling conflicts: a technician on two jobs whose daily hours overlap on the same day,
// and a technician assigned to a job during their PTO. Completed jobs and past days are ignored. Also checks which
// technicians would conflict if assigned to a job being created or edited.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public enum ConflictKind
{
    DoubleBooked,
    OnPto
}

/// <summary>
/// One conflict for one technician over the days from <see cref="FirstDate"/> to <see cref="LastDate"/>.
/// <see cref="OtherJob"/> is set for double bookings; <see cref="Pto"/> for PTO conflicts.
/// </summary>
public record ScheduleConflict(
    ConflictKind Kind, Guid TechnicianId, Job Job, DateOnly FirstDate, DateOnly LastDate, Job? OtherJob, PtoRequest? Pto);

/// <summary>The days and daily hours of a job being created or edited; <see cref="JobId"/> is null for a new job.</summary>
public record JobSlot(Guid? JobId, DateOnly StartDate, DateOnly EndDate, TimeOnly StartTime, TimeOnly EndTime)
{
    public bool IsValid => EndDate >= StartDate && EndTime > StartTime;
}

public class ScheduleConflictService
{
    private readonly IJobRepository _jobs;
    private readonly IPtoRepository _pto;

    public ScheduleConflictService(IJobRepository jobs, IPtoRepository pto)
    {
        _jobs = jobs;
        _pto = pto;
    }

    /// <summary>Double bookings and jobs during approved PTO, on or after <paramref name="from"/>.</summary>
    public async Task<List<ScheduleConflict>> GetConflictsAsync(DateOnly from)
    {
        var jobs = await _jobs.GetAllAsync();
        var approved = (await _pto.GetAllAsync()).Where(p => p.Status == PtoStatus.Approved);
        return [.. FindDoubleBookings(jobs, from), .. FindPtoConflicts(jobs, approved, from)];
    }

    /// <summary>For each given PTO request, the jobs its technician is assigned to during that time.</summary>
    public async Task<Dictionary<Guid, List<ScheduleConflict>>> GetJobConflictsForRequestsAsync(IEnumerable<PtoRequest> requests, DateOnly from)
    {
        var jobs = await _jobs.GetAllAsync();
        return FindPtoConflicts(jobs, requests, from)
            .GroupBy(c => c.Pto!.Id)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// The conflicts each of <paramref name="technicianIds"/> would have if assigned to a job in <paramref name="slot"/>:
    /// other active jobs whose hours overlap, and approved PTO. The job being edited is left out so it doesn't clash
    /// with itself. Technicians who are free have no entries.
    /// </summary>
    public async Task<ILookup<Guid, ScheduleConflict>> FindAssignmentConflictsAsync(JobSlot slot, IEnumerable<Guid> technicianIds)
    {
        var jobs = await _jobs.GetAllAsync();
        var approved = (await _pto.GetAllAsync()).Where(p => p.Status == PtoStatus.Approved);
        return FindAssignmentConflicts(slot, technicianIds, jobs, approved).ToLookup(c => c.TechnicianId);
    }

    /// <summary>
    /// Conflicts for a job that would run in <paramref name="slot"/> with these technicians, using the same rules as the
    /// Conflicts page. Each result's <see cref="ScheduleConflict.Job"/> is the proposed job; for double bookings
    /// <see cref="ScheduleConflict.OtherJob"/> is the existing job. An invalid slot (end before start) has no conflicts.
    /// </summary>
    public static List<ScheduleConflict> FindAssignmentConflicts(JobSlot slot, IEnumerable<Guid> technicianIds,
        IReadOnlyList<Job> jobs, IEnumerable<PtoRequest> approvedPto)
    {
        if (!slot.IsValid)
        {
            return [];
        }

        var proposed = new Job
        {
            Id = slot.JobId ?? Guid.NewGuid(), StartDate = slot.StartDate, EndDate = slot.EndDate,
            StartTime = slot.StartTime, EndTime = slot.EndTime
        };
        proposed.SetAssignedTechnicians(technicianIds);

        var overlapping = jobs.Where(j => j.Id != proposed.Id && j.OverlapsRange(proposed.StartDate, proposed.EndDate));
        var doubleBookings = FindDoubleBookings([proposed, .. overlapping], proposed.StartDate)
            .Where(c => c.Job.Id == proposed.Id || c.OtherJob!.Id == proposed.Id)
            .Select(c => c.Job.Id == proposed.Id ? c : c with { Job = proposed, OtherJob = c.Job });
        return [.. doubleBookings, .. FindPtoConflicts([proposed], approvedPto, proposed.StartDate)];
    }

    /// <summary>
    /// Pairs of active jobs sharing a technician whose date ranges overlap and whose daily hours overlap
    /// (touching hours such as 8–12 and 12–4 are fine). Each pair is reported once per shared technician.
    /// </summary>
    public static List<ScheduleConflict> FindDoubleBookings(IReadOnlyList<Job> jobs, DateOnly from)
    {
        var active = jobs.Where(j => j.Status != JobStatus.Completed && j.EndDate >= from)
            .OrderBy(j => j.StartDate).ThenBy(j => j.StartTime).ToList();
        var conflicts = new List<ScheduleConflict>();

        for (var i = 0; i < active.Count; i++)
        {
            for (var k = i + 1; k < active.Count; k++)
            {
                var (a, b) = (active[i], active[k]);
                var first = Max(Max(a.StartDate, b.StartDate), from);
                var last = Min(a.EndDate, b.EndDate);
                if (first > last || !(a.StartTime < b.EndTime && b.StartTime < a.EndTime))
                {
                    continue;
                }

                conflicts.AddRange(a.AssignedTechnicianIds.Intersect(b.AssignedTechnicianIds)
                    .Select(tech => new ScheduleConflict(ConflictKind.DoubleBooked, tech, a, first, last, b, null)));
            }
        }

        return conflicts;
    }

    /// <summary>
    /// Active jobs whose daily hours fall inside one of the given PTO periods for the same technician.
    /// Reports the first and last affected day of each job/PTO pair.
    /// </summary>
    public static List<ScheduleConflict> FindPtoConflicts(IReadOnlyList<Job> jobs, IEnumerable<PtoRequest> requests, DateOnly from)
    {
        var conflicts = new List<ScheduleConflict>();
        foreach (var pto in requests)
        {
            foreach (var job in jobs.Where(j => j.Status != JobStatus.Completed && j.AssignedTechnicianIds.Contains(pto.TechnicianId)))
            {
                var start = Max(Max(job.StartDate, pto.StartDate), from);
                var end = Min(job.EndDate, pto.EndDate);
                if (start > end)
                {
                    continue;
                }

                var affected = ScheduleCalendar.DaysBetween(start, end)
                    .Where(day => pto.Overlaps(day.ToDateTime(job.StartTime), day.ToDateTime(job.EndTime)))
                    .ToList();
                if (affected.Count > 0)
                {
                    conflicts.Add(new ScheduleConflict(ConflictKind.OnPto, pto.TechnicianId, job, affected[0], affected[^1], null, pto));
                }
            }
        }

        return conflicts;
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
