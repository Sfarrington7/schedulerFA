// Purpose: Tests conflict detection: double bookings (same technician, overlapping days and daily hours) and
// jobs during PTO (partial days, touching times, completed and past jobs, which PTO statuses count), and the conflicts
// a technician would have if assigned to a job being created or edited (the job form's picker).
// Most cases call the pure Find* methods with in-memory jobs and requests.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class ScheduleConflictServiceTests
{
    private static readonly Guid John = Guid.NewGuid();
    private static readonly Guid Mike = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static Job MakeJob(string name, string start, string end, string from = "08:00", string to = "16:00",
        JobStatus status = JobStatus.Scheduled, params Guid[] technicians)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(), Name = name, StartDate = DateOnly.Parse(start), EndDate = DateOnly.Parse(end),
            StartTime = TimeOnly.Parse(from), EndTime = TimeOnly.Parse(to), Status = status
        };
        job.SetAssignedTechnicians(technicians.Length == 0 ? [John] : technicians);
        return job;
    }

    private static PtoRequest MakePto(string start, string startTime, string end, string endTime,
        PtoStatus status = PtoStatus.Approved, Guid? technician = null) => new()
    {
        Id = Guid.NewGuid(), TechnicianId = technician ?? John, Status = status,
        StartDate = DateOnly.Parse(start), StartTime = TimeOnly.Parse(startTime),
        EndDate = DateOnly.Parse(end), EndTime = TimeOnly.Parse(endTime)
    };

    [Fact]
    public void FindDoubleBookings_OverlappingDaysAndHours_ReportsSharedDayRange()
    {
        var a = MakeJob("A", "2026-11-02", "2026-11-06");
        var b = MakeJob("B", "2026-11-05", "2026-11-09", "13:00", "17:00");

        var conflict = Assert.Single(ScheduleConflictService.FindDoubleBookings([a, b], Today));

        Assert.Equal(ConflictKind.DoubleBooked, conflict.Kind);
        Assert.Equal(John, conflict.TechnicianId);
        Assert.Equal(new DateOnly(2026, 11, 5), conflict.FirstDate);
        Assert.Equal(new DateOnly(2026, 11, 6), conflict.LastDate);
        Assert.Equal("B", conflict.OtherJob!.Name);
    }

    [Fact]
    public void FindDoubleBookings_SameDayButHoursOnlyTouch_NoConflict()
    {
        var morning = MakeJob("Morning", "2026-11-05", "2026-11-05", "08:00", "12:00");
        var afternoon = MakeJob("Afternoon", "2026-11-05", "2026-11-05", "12:00", "16:00");

        Assert.Empty(ScheduleConflictService.FindDoubleBookings([morning, afternoon], Today));
    }

    [Fact]
    public void FindDoubleBookings_DifferentTechniciansOrDays_NoConflict()
    {
        var johns = MakeJob("John's", "2026-11-05", "2026-11-05", technicians: John);
        var mikes = MakeJob("Mike's", "2026-11-05", "2026-11-05", technicians: Mike);
        var nextDay = MakeJob("Next day", "2026-11-06", "2026-11-06", technicians: John);

        Assert.Empty(ScheduleConflictService.FindDoubleBookings([johns, mikes, nextDay], Today));
    }

    [Fact]
    public void FindDoubleBookings_TwoSharedTechnicians_OneConflictEach()
    {
        var a = MakeJob("A", "2026-11-05", "2026-11-05", technicians: [John, Mike]);
        var b = MakeJob("B", "2026-11-05", "2026-11-05", technicians: [John, Mike]);

        var conflicts = ScheduleConflictService.FindDoubleBookings([a, b], Today);

        Assert.Equal(new[] { John, Mike }.Order(), conflicts.Select(c => c.TechnicianId).Order());
    }

    [Fact]
    public void FindDoubleBookings_CompletedOrPastJobs_Ignored()
    {
        var completed = MakeJob("Completed", "2026-11-05", "2026-11-05", status: JobStatus.Completed);
        var active = MakeJob("Active", "2026-11-05", "2026-11-05");
        var pastA = MakeJob("Past A", "2026-10-20", "2026-10-21");
        var pastB = MakeJob("Past B", "2026-10-21", "2026-10-22");

        Assert.Empty(ScheduleConflictService.FindDoubleBookings([completed, active, pastA, pastB], Today));
    }

    [Fact]
    public void FindDoubleBookings_OverlapStartedBeforeToday_ReportsFromToday()
    {
        var a = MakeJob("A", "2026-11-01", "2026-11-05");
        var b = MakeJob("B", "2026-11-02", "2026-11-04");

        var conflict = Assert.Single(ScheduleConflictService.FindDoubleBookings([a, b], Today));

        Assert.Equal(Today, conflict.FirstDate);
        Assert.Equal(new DateOnly(2026, 11, 4), conflict.LastDate);
    }

    [Fact]
    public void FindPtoConflicts_FullDaysOverJob_ReportsAffectedDays()
    {
        var job = MakeJob("Inspection", "2026-11-02", "2026-11-06");
        var pto = MakePto("2026-11-05", "00:00", "2026-11-09", "23:59");

        var conflict = Assert.Single(ScheduleConflictService.FindPtoConflicts([job], [pto], Today));

        Assert.Equal(ConflictKind.OnPto, conflict.Kind);
        Assert.Equal(new DateOnly(2026, 11, 5), conflict.FirstDate);
        Assert.Equal(new DateOnly(2026, 11, 6), conflict.LastDate);
        Assert.Same(pto, conflict.Pto);
    }

    [Theory]
    [InlineData("2026-11-05", "16:00", "2026-11-05", "23:59", false)] // starts as the job's day ends
    [InlineData("2026-11-05", "00:00", "2026-11-05", "08:00", false)] // ends as the job's day starts
    [InlineData("2026-11-05", "15:00", "2026-11-05", "23:59", true)]  // leaves an hour early
    [InlineData("2026-11-04", "17:00", "2026-11-05", "09:00", true)]  // overnight into the morning
    [InlineData("2026-11-04", "17:00", "2026-11-05", "07:00", false)] // overnight, back before work
    public void FindPtoConflicts_PartialDays_ComparesHours(string start, string startTime, string end, string endTime, bool expected)
    {
        var job = MakeJob("Inspection", "2026-11-05", "2026-11-05");

        var conflicts = ScheduleConflictService.FindPtoConflicts([job], [MakePto(start, startTime, end, endTime)], Today);

        Assert.Equal(expected, conflicts.Count == 1);
    }

    [Fact]
    public void FindPtoConflicts_OtherTechniciansPtoOrCompletedJob_Ignored()
    {
        var johnsJob = MakeJob("John's", "2026-11-05", "2026-11-05", technicians: John);
        var completed = MakeJob("Completed", "2026-11-05", "2026-11-05", status: JobStatus.Completed, technicians: Mike);
        var mikesPto = MakePto("2026-11-05", "00:00", "2026-11-05", "23:59", technician: Mike);

        Assert.Empty(ScheduleConflictService.FindPtoConflicts([johnsJob, completed], [mikesPto], Today));
    }

    [Fact]
    public async Task GetConflictsAsync_CountsOnlyApprovedPto()
    {
        using var dir = new TempDirectory();
        var harness = new ServiceHarness(dir.Path);
        var john = await harness.TechnicianService.AddAsync("John", "Smith");
        await harness.JobService.CreateAsync(ServiceHarness.ValidJob(john.Id));
        var approved = await harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4)));
        var denied = await harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 5)));
        await harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6)));
        await harness.PtoService.ReviewAsync(approved.Id, PtoStatus.Approved, "admin@example.test");
        await harness.PtoService.ReviewAsync(denied.Id, PtoStatus.Denied, "admin@example.test");

        var conflicts = await harness.ConflictService.GetConflictsAsync(Today);

        var conflict = Assert.Single(conflicts);
        Assert.Equal(approved.Id, conflict.Pto!.Id);
        Assert.Equal(new DateOnly(2026, 11, 4), conflict.FirstDate);
    }

    private static JobSlot Slot(string start, string end, string from = "08:00", string to = "16:00", Guid? jobId = null) =>
        new(jobId, DateOnly.Parse(start), DateOnly.Parse(end), TimeOnly.Parse(from), TimeOnly.Parse(to));

    [Fact]
    public void FindAssignmentConflicts_TechnicianOnOverlappingJob_ReportsExistingJobForThatTechnicianOnly()
    {
        var existing = MakeJob("Smith Tower", "2026-11-09", "2026-11-12", "13:00", "17:00");

        var conflict = Assert.Single(ScheduleConflictService.FindAssignmentConflicts(
            Slot("2026-11-11", "2026-11-13"), [John, Mike], [existing], []));

        Assert.Equal(ConflictKind.DoubleBooked, conflict.Kind);
        Assert.Equal(John, conflict.TechnicianId);
        Assert.Equal("Smith Tower", conflict.OtherJob!.Name);
        Assert.Equal(new DateOnly(2026, 11, 11), conflict.FirstDate);
        Assert.Equal(new DateOnly(2026, 11, 12), conflict.LastDate);
    }

    [Fact]
    public void FindAssignmentConflicts_ProposedJobStartsBeforeExisting_StillReportsExistingAsOtherJob()
    {
        var existing = MakeJob("Smith Tower", "2026-11-12", "2026-11-12");

        var conflict = Assert.Single(ScheduleConflictService.FindAssignmentConflicts(
            Slot("2026-11-10", "2026-11-12"), [John], [existing], []));

        Assert.Equal("Smith Tower", conflict.OtherJob!.Name);
        Assert.Equal(new DateOnly(2026, 11, 12), conflict.FirstDate);
    }

    [Fact]
    public void FindAssignmentConflicts_EditingTheSameJob_DoesNotConflictWithItself()
    {
        var existing = MakeJob("Smith Tower", "2026-11-10", "2026-11-10");

        Assert.Empty(ScheduleConflictService.FindAssignmentConflicts(
            Slot("2026-11-10", "2026-11-10", jobId: existing.Id), [John], [existing], []));
    }

    [Fact]
    public void FindAssignmentConflicts_TouchingHoursOrCompletedJob_NoConflict()
    {
        var morning = MakeJob("Morning", "2026-11-10", "2026-11-10", "06:00", "08:00");
        var done = MakeJob("Done", "2026-11-10", "2026-11-10", status: JobStatus.Completed);

        Assert.Empty(ScheduleConflictService.FindAssignmentConflicts(Slot("2026-11-10", "2026-11-10"), [John], [morning, done], []));
    }

    [Fact]
    public void FindAssignmentConflicts_DuringApprovedPto_ReportsPtoConflict()
    {
        var pto = MakePto("2026-11-10", "12:00", "2026-11-10", "23:59");

        var conflict = Assert.Single(ScheduleConflictService.FindAssignmentConflicts(
            Slot("2026-11-09", "2026-11-11"), [John, Mike], [], [pto]));

        Assert.Equal(ConflictKind.OnPto, conflict.Kind);
        Assert.Equal(John, conflict.TechnicianId);
        Assert.Equal(new DateOnly(2026, 11, 10), conflict.FirstDate);
        Assert.Equal(new DateOnly(2026, 11, 10), conflict.LastDate);
    }

    [Fact]
    public void FindAssignmentConflicts_EndBeforeStart_NoConflicts()
    {
        var existing = MakeJob("Smith Tower", "2026-11-10", "2026-11-10");

        Assert.Empty(ScheduleConflictService.FindAssignmentConflicts(Slot("2026-11-10", "2026-11-10", "16:00", "08:00"), [John], [existing], []));
        Assert.Empty(ScheduleConflictService.FindAssignmentConflicts(Slot("2026-11-11", "2026-11-10"), [John], [existing], []));
    }
}
