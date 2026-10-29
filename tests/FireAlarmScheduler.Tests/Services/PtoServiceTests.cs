// Purpose: Tests PTO request rules against the real database: submitting (validation, past dates, overlapping
// own requests), cancelling (own pending only), reviewing (approve, deny, revoke), and cascade on technician delete.
// The clock is fixed to Nov 3, 2026.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class PtoServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public PtoServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    private PtoService Pto => _harness.PtoService;

    private static readonly DateOnly Nov10 = new(2026, 11, 10);

    [Fact]
    public async Task SubmitAsync_ValidRequest_SavedAsPendingAndSurvivesRestart()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");

        var request = await Pto.SubmitAsync(john.Id, new PtoInput(Nov10, new TimeOnly(12, 0), Nov10.AddDays(2), new TimeOnly(17, 0), "  Family trip "));

        var reloaded = Assert.Single(await new ServiceHarness(_dir.Path).PtoService.GetForTechnicianAsync(john.Id));
        Assert.Equal(request.Id, reloaded.Id);
        Assert.Equal(PtoStatus.Pending, reloaded.Status);
        Assert.Equal(new DateTime(2026, 11, 10, 12, 0, 0), reloaded.StartsAt);
        Assert.Equal(new DateTime(2026, 11, 12, 17, 0, 0), reloaded.EndsAt);
        Assert.Equal("Family trip", reloaded.Reason);
        Assert.Equal(ServiceHarness.Now.UtcDateTime, reloaded.RequestedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_TodayIsAllowed()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");

        Assert.NotNull(await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3))));
    }

    [Theory]
    [InlineData("2026-11-12", "08:00", "2026-11-10", "17:00", "EndDate")]   // ends before it starts
    [InlineData("2026-11-10", "13:00", "2026-11-10", "13:00", "EndDate")]   // zero length
    [InlineData("2026-11-10", "13:00", "2026-11-10", "09:00", "EndDate")]   // same day, end before start
    [InlineData("2026-11-02", "00:00", "2026-11-04", "23:59", "StartDate")] // starts yesterday
    public async Task SubmitAsync_InvalidPeriod_ThrowsAndSavesNothing(string startDate, string startTime, string endDate, string endTime, string field)
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var input = new PtoInput(DateOnly.Parse(startDate), TimeOnly.Parse(startTime), DateOnly.Parse(endDate), TimeOnly.Parse(endTime), null);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Pto.SubmitAsync(john.Id, input));

        Assert.True(ex.Errors.ContainsKey(field));
        Assert.Empty(await Pto.GetForTechnicianAsync(john.Id));
    }

    [Fact]
    public async Task SubmitAsync_ReasonTooLong_Throws()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10, new string('x', 501))));

        Assert.True(ex.Errors.ContainsKey("Reason"));
    }

    [Fact]
    public async Task SubmitAsync_UnknownTechnician_Throws()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => Pto.SubmitAsync(Guid.NewGuid(), ServiceHarness.FullDays(Nov10, Nov10)));
    }

    [Fact]
    public async Task SubmitAsync_OverlapsOwnPendingRequest_Throws()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10.AddDays(2)));

        await Assert.ThrowsAsync<ValidationFailedException>(() => Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10.AddDays(2), Nov10.AddDays(4))));
    }

    [Fact]
    public async Task SubmitAsync_TouchingOrDeniedOrOtherTechniciansRequests_Allowed()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var morning = await Pto.SubmitAsync(john.Id, new PtoInput(Nov10, new TimeOnly(8, 0), Nov10, new TimeOnly(12, 0), null));
        await Pto.SubmitAsync(mike.Id, ServiceHarness.FullDays(Nov10, Nov10));
        var denied = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10.AddDays(5), Nov10.AddDays(5)));
        await Pto.ReviewAsync(denied.Id, PtoStatus.Denied, "admin@example.test");

        await Pto.SubmitAsync(john.Id, new PtoInput(Nov10, new TimeOnly(12, 0), Nov10, new TimeOnly(17, 0), null));
        await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10.AddDays(5), Nov10.AddDays(5)));

        Assert.Equal(4, (await Pto.GetForTechnicianAsync(john.Id)).Count);
        Assert.Equal(PtoStatus.Pending, (await _harness.PtoRequests.GetByIdAsync(morning.Id))!.Status);
    }

    [Fact]
    public async Task CancelAsync_OwnPendingRequest_Removes()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var request = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));

        Assert.Equal(PtoCancelResult.Cancelled, await Pto.CancelAsync(john.Id, request.Id));

        Assert.Empty(await Pto.GetForTechnicianAsync(john.Id));
    }

    [Fact]
    public async Task CancelAsync_AnotherTechniciansRequest_NotFoundAndKept()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var request = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));

        Assert.Equal(PtoCancelResult.NotFound, await Pto.CancelAsync(mike.Id, request.Id));

        Assert.Single(await Pto.GetForTechnicianAsync(john.Id));
    }

    [Fact]
    public async Task CancelAsync_ApprovedRequest_NotPendingAndKept()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var request = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));
        await Pto.ReviewAsync(request.Id, PtoStatus.Approved, "admin@example.test");

        Assert.Equal(PtoCancelResult.NotPending, await Pto.CancelAsync(john.Id, request.Id));

        Assert.Equal(PtoStatus.Approved, Assert.Single(await Pto.GetForTechnicianAsync(john.Id)).Status);
    }

    [Fact]
    public async Task ReviewAsync_ApproveThenRevoke_RecordsReviewerEachTime()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var request = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));

        Assert.True(await Pto.ReviewAsync(request.Id, PtoStatus.Approved, "first@example.test"));
        var approved = (await _harness.PtoRequests.GetByIdAsync(request.Id))!;
        Assert.True(await Pto.ReviewAsync(request.Id, PtoStatus.Denied, "second@example.test"));
        var revoked = (await _harness.PtoRequests.GetByIdAsync(request.Id))!;

        Assert.Equal(PtoStatus.Approved, approved.Status);
        Assert.Equal("first@example.test", approved.ReviewedBy);
        Assert.Equal(ServiceHarness.Now.UtcDateTime, approved.ReviewedAtUtc);
        Assert.Equal(PtoStatus.Denied, revoked.Status);
        Assert.Equal("second@example.test", revoked.ReviewedBy);
    }

    [Fact]
    public async Task ReviewAsync_UnknownRequest_ReturnsFalse()
    {
        Assert.False(await Pto.ReviewAsync(Guid.NewGuid(), PtoStatus.Approved, "admin@example.test"));
    }

    [Fact]
    public async Task ReviewAsync_BackToPending_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Pto.ReviewAsync(Guid.NewGuid(), PtoStatus.Pending, "admin@example.test"));
    }

    [Fact]
    public async Task GetAllAsync_PendingFirstThenByStart()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var later = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10.AddDays(10), Nov10.AddDays(10)));
        var reviewed = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10.AddDays(-5), Nov10.AddDays(-5)));
        var sooner = await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));
        await Pto.ReviewAsync(reviewed.Id, PtoStatus.Approved, "admin@example.test");

        var ids = (await Pto.GetAllAsync()).Select(p => p.Id);

        Assert.Equal([sooner.Id, later.Id, reviewed.Id], ids);
    }

    [Fact]
    public async Task DeletingTechnician_RemovesTheirRequests()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        await Pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Nov10, Nov10));

        await _harness.TechnicianService.DeleteAsync(john.Id);

        Assert.Empty(await Pto.GetAllAsync());
    }
}
