// Purpose: Tests team management: adding, editing, listing, and removing technicians,
// including validation and cleanup of job assignments.
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class TechnicianServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public TechnicianServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AddAsync_ValidNames_TrimsAndPersistsAcrossRestart()
    {
        var added = await _harness.TechnicianService.AddAsync("  John ", " Smith  ");

        var reloaded = await new ServiceHarness(_dir.Path).TechnicianService.GetAsync(added.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("John", reloaded.FirstName);
        Assert.Equal("Smith", reloaded.LastName);
        Assert.Equal("John Smith", reloaded.FullName);
    }

    [Theory]
    [InlineData("", "Smith", "FirstName")]
    [InlineData("John", "   ", "LastName")]
    [InlineData(null, "Smith", "FirstName")]
    public async Task AddAsync_MissingName_ThrowsValidationForThatField(string? first, string? last, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() => _harness.TechnicianService.AddAsync(first, last));

        Assert.True(ex.Errors.ContainsKey(field));
        Assert.Empty(await _harness.TechnicianService.GetAllAsync());
    }

    [Fact]
    public async Task GetAllAsync_SortsByLastThenFirstName()
    {
        await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        await _harness.TechnicianService.AddAsync("John", "Smith");
        await _harness.TechnicianService.AddAsync("Anna", "Johnson");

        var names = (await _harness.TechnicianService.GetAllAsync()).Select(t => t.FullName);

        Assert.Equal(["Anna Johnson", "Mike Johnson", "John Smith"], names);
    }

    [Fact]
    public async Task UpdateAsync_ExistingTechnician_ChangesNames()
    {
        var tech = await _harness.TechnicianService.AddAsync("Jon", "Smith");

        var updated = await _harness.TechnicianService.UpdateAsync(tech.Id, "John", "Smyth");

        Assert.True(updated);
        Assert.Equal("John Smyth", (await _harness.TechnicianService.GetAsync(tech.Id))!.FullName);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        Assert.False(await _harness.TechnicianService.UpdateAsync(Guid.NewGuid(), "A", "B"));
    }

    [Fact]
    public async Task DeleteAsync_AssignedTechnician_RemovesTechnicianAndJobAssignment()
    {
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        var mike = await _harness.TechnicianService.AddAsync("Mike", "Johnson");
        var job = await _harness.JobService.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id));

        var deleted = await _harness.TechnicianService.DeleteAsync(john.Id);

        Assert.True(deleted);
        Assert.Null(await _harness.TechnicianService.GetAsync(john.Id));
        Assert.Equal([mike.Id], (await _harness.JobService.GetAsync(job.Id))!.AssignedTechnicianIds);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsFalse()
    {
        Assert.False(await _harness.TechnicianService.DeleteAsync(Guid.NewGuid()));
    }
}
