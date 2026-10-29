// Purpose: Tests recurring jobs against the real database: saving a job as recurring, creating a job from one
// (details linked, building list copied from the newest job in the series, files not copied), keeping the saved
// details in step with the newest job, duplicate names, and removing a recurring job without losing its jobs.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class RecurringJobServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public RecurringJobServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    private JobService Jobs => _harness.JobService;
    private RecurringJobService Recurring => _harness.RecurringJobService;

    /// <summary>Creates the 2026 job saved as recurring, with Building A (with a file), Building B, and Parking Garage.</summary>
    private async Task<Job> CreateSeriesAsync()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { SaveAsRecurring = true });
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", "Panel in basement"))!;
        await Jobs.AddBuildingAsync(job.Id, "Building B", null);
        await Jobs.AddBuildingAsync(job.Id, "Parking Garage", null);
        await _harness.FileService.UploadAsync(job.Id, a.Id, "devices.csv", new MemoryStream([1, 2, 3]), 3);
        return (await Jobs.GetAsync(job.Id))!;
    }

    private static JobInput NextYear(Guid recurringJobId) => ServiceHarness.ValidJob() with
    {
        StartDate = new DateOnly(2027, 11, 1),
        EndDate = new DateOnly(2027, 11, 5),
        RecurringJobId = recurringJobId
    };

    [Fact]
    public async Task CreateAsync_SaveAsRecurring_SavesDetailsAndLinksJob()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { SaveAsRecurring = true });

        var summary = Assert.Single(await Recurring.GetAllAsync());
        Assert.Equal(job.RecurringJobId, summary.RecurringJob.Id);
        Assert.Equal("Central Plaza Annual Fire Alarm Inspection", summary.RecurringJob.Name);
        Assert.Equal("Central Plaza LLC", summary.RecurringJob.CustomerName);
        Assert.Equal("Central Plaza", summary.RecurringJob.SiteName);
        Assert.Equal("100 Main Street", summary.RecurringJob.Address);
        Assert.Equal(1, summary.JobCount);
        Assert.Equal(new DateOnly(2026, 11, 2), summary.LatestStartDate);
        Assert.Equal(summary.RecurringJob.Id, (await Jobs.GetAsync(job.Id))!.RecurringJobId);
    }

    [Fact]
    public async Task CreateAsync_FromRecurringJob_CopiesBuildingNamesNotesAndOrderButNotFiles()
    {
        var first = await CreateSeriesAsync();

        var second = await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value));

        var reloaded = (await Jobs.GetAsync(second.Id))!;
        Assert.Equal(first.RecurringJobId, reloaded.RecurringJobId);
        Assert.Equal(["Building A", "Building B", "Parking Garage"], reloaded.Buildings.Select(b => b.Name));
        Assert.Equal("Panel in basement", reloaded.Buildings[0].Notes);
        Assert.All(reloaded.Buildings, b => Assert.Empty(b.UploadedFiles));
        Assert.Empty(reloaded.Buildings.Select(b => b.Id).Intersect(first.Buildings.Select(b => b.Id)));
        var original = (await Jobs.GetAsync(first.Id))!;
        Assert.Single(original.Buildings[0].UploadedFiles);
        Assert.Equal(3, original.Buildings.Count);
    }

    [Fact]
    public async Task CreateAsync_FromRecurringJob_CopiesFromNewestJobInSeries()
    {
        var first = await CreateSeriesAsync();
        var second = await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value));
        await Jobs.AddBuildingAsync(second.Id, "New Annex", null);

        var third = await Jobs.CreateAsync(NextYear(first.RecurringJobId.Value) with
        {
            StartDate = new DateOnly(2028, 11, 6), EndDate = new DateOnly(2028, 11, 10)
        });

        Assert.Equal(["Building A", "Building B", "Parking Garage", "New Annex"], third.Buildings.Select(b => b.Name));
        Assert.Equal(3, (await Recurring.GetAllAsync())[0].JobCount);
    }

    [Fact]
    public async Task CreateAsync_FromRecurringJobWithNoBuildings_StartsEmpty()
    {
        var first = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { SaveAsRecurring = true });

        var second = await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value));

        Assert.Empty(second.Buildings);
    }

    [Fact]
    public async Task CreateAsync_UnknownRecurringJob_ThrowsAndSavesNothing()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.CreateAsync(NextYear(Guid.NewGuid())));

        Assert.True(ex.Errors.ContainsKey("RecurringJobId"));
        Assert.Empty(await Jobs.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_SaveAsRecurringWithDuplicateName_ThrowsAndSavesNothing()
    {
        await Jobs.CreateAsync(ServiceHarness.ValidJob() with { SaveAsRecurring = true });

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "central plaza annual fire alarm inspection", SaveAsRecurring = true }));

        Assert.True(ex.Errors.ContainsKey("Name"));
        Assert.Single(await Jobs.GetAllAsync());
        Assert.Single(await Recurring.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_InvalidJobWithSaveAsRecurring_CreatesNoRecurringJob()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Jobs.CreateAsync(ServiceHarness.ValidJob() with { Address = "", SaveAsRecurring = true }));

        Assert.Empty(await Recurring.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_NewestJobWithChangedAddress_UpdatesSavedDetails()
    {
        var first = await CreateSeriesAsync();

        await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value) with { Address = "100 Main St, Suite 2" });

        Assert.Equal("100 Main St, Suite 2", (await Recurring.GetAsync(first.RecurringJobId.Value))!.Address);
    }

    [Fact]
    public async Task UpdateAsync_OlderJobInSeries_DoesNotChangeSavedDetails()
    {
        var first = await CreateSeriesAsync();
        await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value));

        await Jobs.UpdateAsync(first.Id, ServiceHarness.ValidJob() with { Address = "Old address" });

        Assert.Equal("100 Main Street", (await Recurring.GetAsync(first.RecurringJobId.Value))!.Address);
    }

    [Fact]
    public async Task UpdateAsync_OneOffJobWithSaveAsRecurring_CreatesAndLinksRecurringJob()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());

        Assert.True(await Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { SaveAsRecurring = true }));

        var linked = (await Jobs.GetAsync(job.Id))!.RecurringJobId;
        Assert.NotNull(linked);
        Assert.Equal(linked, Assert.Single(await Recurring.GetAllAsync()).RecurringJob.Id);
    }

    [Fact]
    public async Task UpdateAsync_RecurringJobIdInInput_IsIgnored()
    {
        var series = await CreateSeriesAsync();
        var oneOff = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "One-off" });

        await Jobs.UpdateAsync(oneOff.Id, ServiceHarness.ValidJob() with { Name = "One-off", RecurringJobId = series.RecurringJobId });

        Assert.Null((await Jobs.GetAsync(oneOff.Id))!.RecurringJobId);
    }

    [Fact]
    public async Task DeleteAsync_RemovesRecurringJobButKeepsItsJobs()
    {
        var first = await CreateSeriesAsync();

        Assert.True(await Recurring.DeleteAsync(first.RecurringJobId!.Value));

        Assert.Empty(await Recurring.GetAllAsync());
        var job = (await Jobs.GetAsync(first.Id))!;
        Assert.Null(job.RecurringJobId);
        Assert.Equal(3, job.Buildings.Count);
        Assert.False(await Recurring.DeleteAsync(first.RecurringJobId.Value));
    }

    [Fact]
    public async Task DeleteJob_LastJobInSeries_KeepsRecurringJob()
    {
        var first = await CreateSeriesAsync();

        await Jobs.DeleteAsync(first.Id);

        var summary = Assert.Single(await Recurring.GetAllAsync());
        Assert.Equal(0, summary.JobCount);
        Assert.Null(summary.LatestStartDate);
    }
}
