// Purpose: Tests job scheduling: create/edit/delete, validation, technician assignment,
// building management, and daily/weekly date-range queries.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class JobServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public JobServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    private JobService Jobs => _harness.JobService;

    [Fact]
    public async Task CreateAsync_ValidInput_PersistsJobWithAssignments()
    {
        var tech = await _harness.TechnicianService.AddAsync("John", "Smith");

        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(tech.Id, tech.Id));

        var reloaded = await new ServiceHarness(_dir.Path).JobService.GetAsync(job.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Central Plaza Annual Fire Alarm Inspection", reloaded.Name);
        Assert.Equal("100 Main Street", reloaded.Address);
        Assert.Equal(new DateOnly(2026, 11, 6), reloaded.EndDate);
        Assert.Equal([tech.Id], reloaded.AssignedTechnicianIds);
    }

    [Fact]
    public async Task CreateAsync_BlankOptionalFields_StoredAsNull()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { CustomerName = "  ", Notes = "" });

        Assert.Null(job.CustomerName);
        Assert.Null(job.Notes);
    }

    [Fact]
    public async Task CreateAsync_InvalidInput_ReportsEveryProblemAndSavesNothing()
    {
        var input = ServiceHarness.ValidJob(Guid.NewGuid()) with
        {
            Name = " ",
            Address = "",
            EndDate = new DateOnly(2026, 11, 1),
            EndTime = new TimeOnly(8, 0)
        };

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.CreateAsync(input));

        Assert.Equal(["Address", "AssignedTechnicianIds", "EndDate", "EndTime", "Name"], ex.Errors.Keys.Order());
        Assert.Empty(await Jobs.GetAllAsync());
    }

    [Fact]
    public async Task UpdateAsync_ExistingJob_ChangesDetailsAndKeepsBuildings()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        await Jobs.AddBuildingAsync(job.Id, "Building A", null);

        var updated = await Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { Name = "Renamed", Status = JobStatus.InProgress });

        var reloaded = (await Jobs.GetAsync(job.Id))!;
        Assert.True(updated);
        Assert.Equal("Renamed", reloaded.Name);
        Assert.Equal(JobStatus.InProgress, reloaded.Status);
        Assert.Equal("Building A", Assert.Single(reloaded.Buildings).Name);
    }

    [Fact]
    public async Task UpdateAsync_InvalidInput_LeavesStoredJobUnchanged()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());

        await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { Name = "" }));

        Assert.Equal(job.Name, (await Jobs.GetAsync(job.Id))!.Name);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        Assert.False(await Jobs.UpdateAsync(Guid.NewGuid(), ServiceHarness.ValidJob()));
    }

    [Fact]
    public async Task DeleteAsync_JobWithFiles_RemovesJobAndStoredFiles()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        var building = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        var file = (await _harness.FileService.UploadAsync(job.Id, building.Id, "devices.csv", new MemoryStream([1, 2, 3]), 3))!;

        var deleted = await Jobs.DeleteAsync(job.Id);

        Assert.True(deleted);
        Assert.Null(await Jobs.GetAsync(job.Id));
        Assert.False(File.Exists(Path.Combine(_harness.UploadsDirectory, file.StoredFileName)));
    }

    [Fact]
    public async Task AddBuildingAsync_MultipleBuildings_AllKeptInOrder()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());

        await Jobs.AddBuildingAsync(job.Id, "Building A", null);
        await Jobs.AddBuildingAsync(job.Id, "Building B", "East entrance");
        await Jobs.AddBuildingAsync(job.Id, "Parking Garage", null);

        var buildings = (await Jobs.GetAsync(job.Id))!.Buildings;
        Assert.Equal(["Building A", "Building B", "Parking Garage"], buildings.Select(b => b.Name));
        Assert.Equal("East entrance", buildings[1].Notes);
        Assert.Equal(3, buildings.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public async Task AddBuildingAsync_DuplicateOrBlankName_Throws()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        await Jobs.AddBuildingAsync(job.Id, "Building A", null);

        await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.AddBuildingAsync(job.Id, "building a", null));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Jobs.AddBuildingAsync(job.Id, " ", null));
        Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings);
    }

    [Fact]
    public async Task AddBuildingAsync_UnknownJob_ReturnsNull()
    {
        Assert.Null(await Jobs.AddBuildingAsync(Guid.NewGuid(), "Building A", null));
    }

    [Fact]
    public async Task UpdateBuildingAsync_RenamesBuilding()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        var building = (await Jobs.AddBuildingAsync(job.Id, "Bldg A", null))!;

        Assert.True(await Jobs.UpdateBuildingAsync(job.Id, building.Id, "Building A", "Roof access"));

        var reloaded = Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings);
        Assert.Equal("Building A", reloaded.Name);
        Assert.Equal("Roof access", reloaded.Notes);
    }

    [Fact]
    public async Task DeleteBuildingAsync_RemovesBuildingAndItsFilesOnly()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        var b = (await Jobs.AddBuildingAsync(job.Id, "Building B", null))!;
        var fileA = (await _harness.FileService.UploadAsync(job.Id, a.Id, "a.csv", new MemoryStream([1]), 1))!;
        var fileB = (await _harness.FileService.UploadAsync(job.Id, b.Id, "b.csv", new MemoryStream([2]), 1))!;

        Assert.True(await Jobs.DeleteBuildingAsync(job.Id, a.Id));

        Assert.Equal("Building B", Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings).Name);
        Assert.False(File.Exists(Path.Combine(_harness.UploadsDirectory, fileA.StoredFileName)));
        Assert.True(File.Exists(Path.Combine(_harness.UploadsDirectory, fileB.StoredFileName)));
        Assert.False(await Jobs.DeleteBuildingAsync(job.Id, a.Id));
    }

    [Theory]
    [InlineData("2026-11-02", "2026-11-02", true)]  // first day
    [InlineData("2026-11-06", "2026-11-06", true)]  // last day
    [InlineData("2026-11-01", "2026-11-01", false)] // day before
    [InlineData("2026-11-07", "2026-11-07", false)] // day after
    [InlineData("2026-10-26", "2026-11-01", false)] // previous week
    [InlineData("2026-11-02", "2026-11-08", true)]  // same week
    [InlineData("2026-10-30", "2026-11-03", true)]  // overlaps start
    public async Task GetJobsForTechnicianAsync_FiltersByDateRange(string from, string to, bool expected)
    {
        var tech = await _harness.TechnicianService.AddAsync("John", "Smith");
        await Jobs.CreateAsync(ServiceHarness.ValidJob(tech.Id));

        var jobs = await Jobs.GetJobsForTechnicianAsync(tech.Id, DateOnly.Parse(from), DateOnly.Parse(to));

        Assert.Equal(expected, jobs.Count == 1);
    }

    [Fact]
    public async Task GetJobsForTechnicianAsync_OnlyReturnsAssignedJobs()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "John's job" });
        await Jobs.CreateAsync(ServiceHarness.ValidJob(mike.Id) with { Name = "Mike's job" });

        var jobs = await Jobs.GetJobsForTechnicianAsync(john.Id, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        Assert.Equal("John's job", Assert.Single(jobs).Name);
    }

    [Fact]
    public async Task GetJobsInRangeAsync_SortsByStartDateThenTime()
    {
        await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "Later", StartDate = new DateOnly(2026, 11, 4) });
        await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "Afternoon", StartTime = new TimeOnly(13, 0) });
        await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "Morning", StartTime = new TimeOnly(7, 0) });

        var jobs = await Jobs.GetJobsInRangeAsync(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8));

        Assert.Equal(["Morning", "Afternoon", "Later"], jobs.Select(j => j.Name));
    }

    [Fact]
    public async Task GetJobsForTechniciansAsync_SeveralTechnicians_ReturnsEachMatchingJobOnce()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var sara = await _harness.TechnicianService.AddAsync("Sara", "Lee");
        await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id) with { Name = "Shared job" });
        await Jobs.CreateAsync(ServiceHarness.ValidJob(mike.Id) with { Name = "Mike's job" });
        await Jobs.CreateAsync(ServiceHarness.ValidJob(sara.Id) with { Name = "Sara's job" });
        await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "Next month", StartDate = new DateOnly(2026, 12, 7), EndDate = new DateOnly(2026, 12, 8) });

        var jobs = await Jobs.GetJobsForTechniciansAsync([john.Id, mike.Id], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        Assert.Equal(["Mike's job", "Shared job"], jobs.Select(j => j.Name).Order());
    }

    [Fact]
    public async Task GetJobsForTechniciansAsync_NoTechnicians_ReturnsNothing()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id));
        await Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "Unassigned" });

        Assert.Empty(await Jobs.GetJobsForTechniciansAsync([], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)));
    }
}
