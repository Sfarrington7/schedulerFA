// Purpose: Tests the "needs attention" counts behind the dashboard cards and sidebar badges against the real database:
// an empty schedule, which PTO statuses count as pending, conflicts from today on (earliest first, past ones left
// out), and that one service instance (one web request) reuses its first lookup. The clock is fixed to Nov 3, 2026.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class ScheduleAlertServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public ScheduleAlertServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    private ScheduleAlertService NewAlertService() =>
        new(_harness.PtoService, _harness.ConflictService, new FixedTimeProvider(ServiceHarness.Now));

    private static DateOnly Day(int november) => new(2026, 11, november);

    private Task<Job> AddJobAsync(string name, DateOnly start, DateOnly end, params Guid[] technicians) =>
        _harness.JobService.CreateAsync(ServiceHarness.ValidJob(technicians) with { Name = name, StartDate = start, EndDate = end });

    [Fact]
    public async Task GetAsync_EmptySchedule_NothingNeedsAttention()
    {
        var alerts = await NewAlertService().GetAsync();

        Assert.Equal(new DateOnly(2026, 11, 3), alerts.Today);
        Assert.Equal(0, alerts.PendingPtoCount);
        Assert.Empty(alerts.Conflicts);
    }

    [Fact]
    public async Task GetAsync_MixedPtoStatuses_CountsOnlyPending()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        await _harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(10), Day(10)));
        await _harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(11), Day(11)));
        var approved = await _harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(12), Day(12)));
        var denied = await _harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(13), Day(13)));
        await _harness.PtoService.ReviewAsync(approved.Id, PtoStatus.Approved, "lead");
        await _harness.PtoService.ReviewAsync(denied.Id, PtoStatus.Denied, "lead");

        var alerts = await NewAlertService().GetAsync();

        Assert.Equal(2, alerts.PendingPtoCount);
    }

    [Fact]
    public async Task GetAsync_Conflicts_FromTodayOnEarliestFirst()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var sara = await _harness.TechnicianService.AddAsync("Sara", "Lee");
        await AddJobAsync("Past A", new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), john.Id);
        await AddJobAsync("Past B", new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), john.Id);
        await AddJobAsync("Harbor", Day(19), Day(20), john.Id);
        await AddJobAsync("Lincoln", Day(19), Day(19), john.Id);
        await AddJobAsync("Riverside", Day(10), Day(12), sara.Id);
        var pto = await _harness.PtoService.SubmitAsync(sara.Id, ServiceHarness.FullDays(Day(11), Day(11)));
        await _harness.PtoService.ReviewAsync(pto.Id, PtoStatus.Approved, "lead");

        var alerts = await NewAlertService().GetAsync();

        Assert.Equal(2, alerts.ConflictCount);
        Assert.Equal((ConflictKind.OnPto, sara.Id, Day(11)), (alerts.Conflicts[0].Kind, alerts.Conflicts[0].TechnicianId, alerts.Conflicts[0].FirstDate));
        Assert.Equal((ConflictKind.DoubleBooked, john.Id, Day(19)), (alerts.Conflicts[1].Kind, alerts.Conflicts[1].TechnicianId, alerts.Conflicts[1].FirstDate));
        Assert.Equal(0, alerts.PendingPtoCount);
    }

    [Fact]
    public async Task GetAsync_SameInstanceAfterChange_ReusesFirstLookupButNewInstanceSeesChange()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var service = NewAlertService();
        var first = await service.GetAsync();

        await _harness.PtoService.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(10), Day(10)));

        Assert.Same(first, await service.GetAsync());
        Assert.Equal(1, (await NewAlertService().GetAsync()).PendingPtoCount);
    }
}
