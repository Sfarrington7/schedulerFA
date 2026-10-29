// Purpose: Tests recurring jobs against the real database: saving a job as recurring, creating a job from one
// (details linked, the series' building list used, files not copied), keeping saved details in step with the newest
// job, the standing building list (buildings added to any job in a series reach the list and upcoming jobs; renames
// follow; removals from one job don't), duplicate names, and removing a recurring job without losing its jobs.
// The clock is fixed to Nov 3, 2026, so 2027 jobs are "upcoming".
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
    public async Task CreateAsync_FromRecurringJob_IncludesBuildingAddedToAnyEarlierJob()
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

    [Fact]
    public async Task CreateAsync_BuildingRows_SavedInOrderIgnoringBlankRows()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with
        {
            Buildings = [new("  Building A ", "Panel in lobby"), new("", "ignored"), new("Parking Garage", "  ")]
        });

        var reloaded = (await Jobs.GetAsync(job.Id))!;
        Assert.Equal(["Building A", "Parking Garage"], reloaded.Buildings.Select(b => b.Name));
        Assert.Equal("Panel in lobby", reloaded.Buildings[0].Notes);
        Assert.Null(reloaded.Buildings[1].Notes);
    }

    [Fact]
    public async Task CreateAsync_DuplicateBuildingRows_ThrowsAndSavesNothing()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.CreateAsync(ServiceHarness.ValidJob() with
        {
            SaveAsRecurring = true,
            Buildings = [new("Building A", null), new("building a", null)]
        }));

        Assert.True(ex.Errors.ContainsKey("Buildings"));
        Assert.Empty(await Jobs.GetAllAsync());
        Assert.Empty(await Recurring.GetAllAsync());
    }

    [Fact]
    public async Task CreateAsync_SaveAsRecurringWithBuildings_SavesThemAsTheSeriesList()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with
        {
            SaveAsRecurring = true,
            Buildings = [new("Building A", "North entrance"), new("Building B", null)]
        });

        var recurring = (await Recurring.GetAsync(job.RecurringJobId!.Value))!;
        Assert.Equal(["Building A", "Building B"], recurring.Buildings.Select(b => b.Name));
        Assert.Equal("North entrance", recurring.Buildings[0].Notes);
    }

    [Fact]
    public async Task CreateAsync_FromRecurringJobWithEditedRows_UsesRowsAndAddsOnlyNewBuildingsToSeries()
    {
        var first = await CreateSeriesAsync();

        var second = await Jobs.CreateAsync(NextYear(first.RecurringJobId!.Value) with
        {
            Buildings = [new("Building A", null), new("Building C", "New this year")]
        });

        Assert.Equal(["Building A", "Building C"], second.Buildings.Select(b => b.Name));
        var series = (await Recurring.GetAsync(first.RecurringJobId.Value))!;
        Assert.Equal(["Building A", "Building B", "Parking Garage", "Building C"], series.Buildings.Select(b => b.Name));
    }

    [Fact]
    public async Task AddBuildingAsync_JobInSeries_AddsToSeriesListAndUpcomingJobsOnly()
    {
        var current = await CreateSeriesAsync();
        var upcoming = await Jobs.CreateAsync(NextYear(current.RecurringJobId!.Value));
        var completedUpcoming = await Jobs.CreateAsync(NextYear(current.RecurringJobId.Value) with
        {
            StartDate = new DateOnly(2027, 12, 1), EndDate = new DateOnly(2027, 12, 1), Status = JobStatus.Completed
        });
        var oneOff = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "One-off", StartDate = new DateOnly(2027, 1, 4), EndDate = new DateOnly(2027, 1, 4) });

        await Jobs.AddBuildingAsync(current.Id, "Building C", "Added during this year's test");

        Assert.Contains("Building C", (await Recurring.GetAsync(current.RecurringJobId.Value))!.Buildings.Select(b => b.Name));
        var upcomingC = Assert.Single((await Jobs.GetAsync(upcoming.Id))!.Buildings, b => b.Name == "Building C");
        Assert.Equal("Added during this year's test", upcomingC.Notes);
        Assert.Equal(3, upcomingC.SortOrder);
        Assert.DoesNotContain("Building C", (await Jobs.GetAsync(completedUpcoming.Id))!.Buildings.Select(b => b.Name));
        Assert.Empty((await Jobs.GetAsync(oneOff.Id))!.Buildings);
    }

    [Fact]
    public async Task AddBuildingAsync_UpcomingJobAlreadyHasIt_NoDuplicate()
    {
        var current = await CreateSeriesAsync();
        var upcoming = await Jobs.CreateAsync(NextYear(current.RecurringJobId!.Value) with
        {
            Buildings = [new("Building A", null), new("building c", null)]
        });

        await Jobs.AddBuildingAsync(current.Id, "Building C", null);

        Assert.Single((await Jobs.GetAsync(upcoming.Id))!.Buildings, b => b.Name.Equals("Building C", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddBuildingAsync_PastJobInSeries_NotChangedWhenUpcomingJobAddsBuilding()
    {
        var current = await CreateSeriesAsync();
        var upcoming = await Jobs.CreateAsync(NextYear(current.RecurringJobId!.Value));

        await Jobs.AddBuildingAsync(upcoming.Id, "Building C", null);

        Assert.Equal(3, (await Jobs.GetAsync(current.Id))!.Buildings.Count);
        Assert.Contains("Building C", (await Recurring.GetAsync(current.RecurringJobId.Value))!.Buildings.Select(b => b.Name));
    }

    [Fact]
    public async Task UpdateBuildingAsync_JobInSeries_RenamesOnSeriesList()
    {
        var current = await CreateSeriesAsync();
        var buildingB = current.Buildings.Single(b => b.Name == "Building B");

        await Jobs.UpdateBuildingAsync(current.Id, buildingB.Id, "Building B (East)", "Roof hatch");

        var series = (await Recurring.GetAsync(current.RecurringJobId!.Value))!;
        Assert.Equal(["Building A", "Building B (East)", "Parking Garage"], series.Buildings.Select(b => b.Name));
        Assert.Equal("Roof hatch", series.Buildings[1].Notes);
    }

    [Fact]
    public async Task DeleteBuildingAsync_JobInSeries_KeepsBuildingOnSeriesList()
    {
        var current = await CreateSeriesAsync();

        await Jobs.DeleteBuildingAsync(current.Id, current.Buildings.Single(b => b.Name == "Parking Garage").Id);

        Assert.Equal(3, (await Recurring.GetAsync(current.RecurringJobId!.Value))!.Buildings.Count);
    }

    [Fact]
    public async Task AddRecurringBuildingAsync_AddsToSeriesAndUpcomingJobs()
    {
        var current = await CreateSeriesAsync();
        var upcoming = await Jobs.CreateAsync(NextYear(current.RecurringJobId!.Value));

        Assert.True(await Jobs.AddRecurringBuildingAsync(current.RecurringJobId.Value, " Pump House ", null));

        Assert.Contains("Pump House", (await Recurring.GetAsync(current.RecurringJobId.Value))!.Buildings.Select(b => b.Name));
        Assert.Contains("Pump House", (await Jobs.GetAsync(upcoming.Id))!.Buildings.Select(b => b.Name));
        Assert.DoesNotContain("Pump House", (await Jobs.GetAsync(current.Id))!.Buildings.Select(b => b.Name));
    }

    [Fact]
    public async Task AddRecurringBuildingAsync_BlankNameOrUnknownRecurringJob_Rejected()
    {
        var current = await CreateSeriesAsync();

        await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.AddRecurringBuildingAsync(current.RecurringJobId!.Value, " ", null));
        Assert.False(await Jobs.AddRecurringBuildingAsync(Guid.NewGuid(), "Pump House", null));
    }

    [Fact]
    public async Task RemoveBuildingAsync_TakesItOffSeriesListOnly()
    {
        var current = await CreateSeriesAsync();
        var series = (await Recurring.GetAsync(current.RecurringJobId!.Value))!;

        Assert.True(await Recurring.RemoveBuildingAsync(series.Id, series.Buildings.Single(b => b.Name == "Building B").Id));

        Assert.Equal(["Building A", "Parking Garage"], (await Recurring.GetAsync(series.Id))!.Buildings.Select(b => b.Name));
        Assert.Equal(3, (await Jobs.GetAsync(current.Id))!.Buildings.Count);
        var next = await Jobs.CreateAsync(NextYear(series.Id));
        Assert.Equal(["Building A", "Parking Garage"], next.Buildings.Select(b => b.Name));
    }
}
