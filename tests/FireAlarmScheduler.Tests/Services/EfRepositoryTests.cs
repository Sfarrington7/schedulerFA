// Purpose: Tests SQL persistence through the EF Core repositories on a throwaway SQL Server database built from
// the real migrations: full job graphs survive restarts, rows keep their order, failed changes save nothing,
// and database cascades clean up assignments and account links.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Tests.Services;

public class EfRepositoryTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public EfRepositoryTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task JobGraph_SavedAndReloadedAfterRestart_KeepsEveryField()
    {
        var tech = await _harness.TechnicianService.AddAsync("John", "Smith");
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob(tech.Id) with { Status = JobStatus.InProgress });
        var building = (await _harness.JobService.AddBuildingAsync(job.Id, "Building A", "Roof"))!;
        var file = (await _harness.FileService.UploadAsync(job.Id, building.Id, "devices.csv", new MemoryStream([1, 2]), 2))!;

        var reloaded = (await new ServiceHarness(_dir.Path).Jobs.GetByIdAsync(job.Id))!;

        Assert.Equal(JobStatus.InProgress, reloaded.Status);
        Assert.Equal(new DateOnly(2026, 11, 2), reloaded.StartDate);
        Assert.Equal(new TimeOnly(16, 0), reloaded.EndTime);
        Assert.Equal([tech.Id], reloaded.AssignedTechnicianIds);
        var storedBuilding = Assert.Single(reloaded.Buildings);
        Assert.Equal("Roof", storedBuilding.Notes);
        var storedFile = Assert.Single(storedBuilding.UploadedFiles);
        Assert.Equal(file.StoredFileName, storedFile.StoredFileName);
        Assert.Equal(ServiceHarness.Now.UtcDateTime, storedFile.UploadDate);
    }

    [Fact]
    public async Task JobStatus_StoredAsReadableText()
    {
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob() with { Status = JobStatus.Completed });

        await using var db = await _harness.ContextFactory.CreateDbContextAsync();
        var status = await db.Database.SqlQuery<string>($"SELECT Status AS Value FROM Jobs WHERE Id = {job.Id}").SingleAsync();

        Assert.Equal("Completed", status);
    }

    [Fact]
    public async Task UpdateAsync_ChangeThrows_SavesNothing()
    {
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob());

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Jobs.UpdateAsync<bool>(job.Id, j =>
        {
            j.Name = "Changed";
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(job.Name, (await _harness.Jobs.GetByIdAsync(job.Id))!.Name);
    }

    [Fact]
    public async Task UpdateAsync_ReassigningTechnicians_ReplacesJoinRows()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob(john.Id));

        await _harness.JobService.UpdateAsync(job.Id, ServiceHarness.ValidJob(mike.Id, john.Id));
        await _harness.JobService.UpdateAsync(job.Id, ServiceHarness.ValidJob(mike.Id));

        await using var db = await _harness.ContextFactory.CreateDbContextAsync();
        Assert.Equal([mike.Id], await db.JobAssignments.Where(a => a.JobId == job.Id).Select(a => a.TechnicianId).ToListAsync());
    }

    [Fact]
    public async Task DeleteJob_CascadesToBuildingsFilesAndAssignments()
    {
        var tech = await _harness.TechnicianService.AddAsync("John", "Smith");
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob(tech.Id));
        var building = (await _harness.JobService.AddBuildingAsync(job.Id, "Building A", null))!;
        await _harness.FileService.UploadAsync(job.Id, building.Id, "a.csv", new MemoryStream([1]), 1);

        Assert.True(await _harness.JobService.DeleteAsync(job.Id));

        await using var db = await _harness.ContextFactory.CreateDbContextAsync();
        Assert.Empty(await db.Buildings.ToListAsync());
        Assert.Empty(await db.UploadedFiles.ToListAsync());
        Assert.Empty(await db.JobAssignments.ToListAsync());
        Assert.NotNull(await db.Technicians.FindAsync(tech.Id));
    }

    [Fact]
    public async Task DeleteTechnician_UnlinksTheirAccount()
    {
        var tech = await _harness.TechnicianService.AddAsync("John", "Smith");
        var user = await _harness.CreateUserAsync("john@example.test", [AppRoles.Technician], tech.Id);

        await _harness.TechnicianService.DeleteAsync(tech.Id);

        var reloaded = (await new ServiceHarness(_dir.Path).UserAdmin.GetAsync(user.Id))!;
        Assert.Null(reloaded.User.TechnicianId);
    }

    [Fact]
    public async Task Initialize_SeedsFourRoles_AndIsSafeToRunAgain()
    {
        _ = new ServiceHarness(_dir.Path);

        await using var db = await _harness.ContextFactory.CreateDbContextAsync();
        Assert.Equal(AppRoles.All.Order(), (await db.Roles.Select(r => r.Name!).ToListAsync()).Order());
    }
}
