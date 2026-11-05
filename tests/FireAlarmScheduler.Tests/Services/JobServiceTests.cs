// Purpose: Tests job scheduling: create/edit/delete, validation, the job files link, technician assignment,
// building management, the technicians' building checklist, and daily/weekly date-range queries.
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
    public async Task DeleteAsync_JobWithBuildings_RemovesJob()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        await Jobs.AddBuildingAsync(job.Id, "Building A", null);

        Assert.True(await Jobs.DeleteAsync(job.Id));

        Assert.Null(await Jobs.GetAsync(job.Id));
        Assert.False(await Jobs.DeleteAsync(job.Id));
    }

    [Fact]
    public async Task CreateAsync_FilesLink_SavedTrimmed()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { FilesUrl = "  https://drive.google.com/drive/folders/abc123?usp=sharing " });

        Assert.Equal("https://drive.google.com/drive/folders/abc123?usp=sharing", (await Jobs.GetAsync(job.Id))!.FilesUrl);
    }

    [Fact]
    public async Task CreateAsync_UnsafeFilesLink_ReportsErrorAndSavesNothing()
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Jobs.CreateAsync(ServiceHarness.ValidJob() with { Name = "", FilesUrl = "javascript:alert(1)" }));

        Assert.Equal(["FilesUrl", "Name"], ex.Errors.Keys.Order());
        Assert.Empty(await Jobs.GetAllAsync());
    }

    [Fact]
    public async Task UpdateAsync_ChangeThenClearFilesLink_StoresEachValue()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { FilesUrl = "https://drive.google.com/drive/folders/old" });

        await Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { FilesUrl = "https://contoso.sharepoint.com/sites/jobs/new" });
        Assert.Equal("https://contoso.sharepoint.com/sites/jobs/new", (await Jobs.GetAsync(job.Id))!.FilesUrl);

        await Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { FilesUrl = " " });
        Assert.Null((await Jobs.GetAsync(job.Id))!.FilesUrl);
    }

    [Fact]
    public async Task UpdateAsync_InvalidFilesLink_KeepsOldLink()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob() with { FilesUrl = "https://drive.google.com/drive/folders/old" });

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob() with { FilesUrl = "ftp://files.example.com/job" }));

        Assert.Equal(JobFilesLink.InvalidMessage, ex.Errors["FilesUrl"]);
        Assert.Equal("https://drive.google.com/drive/folders/old", (await Jobs.GetAsync(job.Id))!.FilesUrl);
    }

    [Fact]
    public async Task SetBuildingCompleteAsync_AssignedTechnician_RecordsWhoAndWhen()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id));
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        var b = (await Jobs.AddBuildingAsync(job.Id, "Building B", null))!;

        var result = await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, john.Id, complete: true);

        Assert.True(result!.IsComplete);
        var buildings = (await new ServiceHarness(_dir.Path).JobService.GetAsync(job.Id))!.Buildings;
        Assert.Equal(ServiceHarness.Now.UtcDateTime, buildings.Single(x => x.Id == a.Id).CompletedAtUtc);
        Assert.Equal(john.Id, buildings.Single(x => x.Id == a.Id).CompletedByTechnicianId);
        Assert.False(buildings.Single(x => x.Id == b.Id).IsComplete);
    }

    [Fact]
    public async Task SetBuildingCompleteAsync_Unticked_ClearsCompletion()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id));
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, john.Id, complete: true);

        await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, john.Id, complete: false);

        var building = Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings);
        Assert.Null(building.CompletedAtUtc);
        Assert.Null(building.CompletedByTechnicianId);
    }

    [Fact]
    public async Task SetBuildingCompleteAsync_AlreadyTickedBySomeoneElse_KeepsFirstTechnician()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id));
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, john.Id, complete: true);

        var result = await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, mike.Id, complete: true);

        Assert.Equal(john.Id, result!.CompletedByTechnicianId);
        Assert.Equal(john.Id, Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings).CompletedByTechnicianId);
    }

    [Fact]
    public async Task SetBuildingCompleteAsync_TechnicianNotOnJob_ReturnsNullAndChangesNothing()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var sara = await _harness.TechnicianService.AddAsync("Sara", "Lee");
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id));
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;

        Assert.Null(await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, sara.Id, complete: true));
        Assert.Null(await Jobs.SetBuildingCompleteAsync(job.Id, Guid.NewGuid(), john.Id, complete: true));
        Assert.Null(await Jobs.SetBuildingCompleteAsync(Guid.NewGuid(), a.Id, john.Id, complete: true));

        Assert.False(Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings).IsComplete);
    }

    [Fact]
    public async Task UpdateBuildingAsync_CompletedBuilding_StaysComplete()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob(john.Id));
        var a = (await Jobs.AddBuildingAsync(job.Id, "Bldg A", null))!;
        await Jobs.SetBuildingCompleteAsync(job.Id, a.Id, john.Id, complete: true);

        await Jobs.UpdateBuildingAsync(job.Id, a.Id, "Building A", "Roof access");
        await Jobs.UpdateAsync(job.Id, ServiceHarness.ValidJob(john.Id) with { Status = JobStatus.InProgress });

        var building = Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings);
        Assert.Equal("Building A", building.Name);
        Assert.Equal(john.Id, building.CompletedByTechnicianId);
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
    public async Task DeleteBuildingAsync_RemovesOnlyThatBuilding()
    {
        var job = await Jobs.CreateAsync(ServiceHarness.ValidJob());
        var a = (await Jobs.AddBuildingAsync(job.Id, "Building A", null))!;
        await Jobs.AddBuildingAsync(job.Id, "Building B", null);

        Assert.True(await Jobs.DeleteBuildingAsync(job.Id, a.Id));

        Assert.Equal("Building B", Assert.Single((await Jobs.GetAsync(job.Id))!.Buildings).Name);
        Assert.False(await Jobs.DeleteBuildingAsync(job.Id, a.Id));
        Assert.False(await Jobs.DeleteBuildingAsync(Guid.NewGuid(), a.Id));
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
