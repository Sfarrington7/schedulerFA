// Purpose: Tests JSON persistence: missing files, round trips across restarts, atomic writes,
// and refusal to overwrite a corrupted data file.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class JsonFileStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task ReadAllAsync_MissingFile_ReturnsEmptyList()
    {
        var store = new JsonFileStore<Technician>(_dir.Combine("technicians.json"));

        var items = await store.ReadAllAsync();

        Assert.Empty(items);
    }

    [Fact]
    public async Task UpdateAsync_ThenNewStoreInstance_LoadsSavedData()
    {
        var path = _dir.Combine("nested", "jobs.json");
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Name = "Job",
            StartDate = new DateOnly(2026, 11, 2),
            StartTime = new TimeOnly(8, 30),
            Status = JobStatus.InProgress,
            Buildings = [new Building { Id = Guid.NewGuid(), Name = "Building A" }]
        };
        await new JsonFileStore<Job>(path).UpdateAsync(items => { items.Add(job); return true; });

        var reloaded = await new JsonFileStore<Job>(path).ReadAllAsync();

        var loaded = Assert.Single(reloaded);
        Assert.Equal(job.Id, loaded.Id);
        Assert.Equal(new DateOnly(2026, 11, 2), loaded.StartDate);
        Assert.Equal(new TimeOnly(8, 30), loaded.StartTime);
        Assert.Equal(JobStatus.InProgress, loaded.Status);
        Assert.Equal("Building A", Assert.Single(loaded.Buildings).Name);
        Assert.Contains("\"InProgress\"", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task UpdateAsync_CorruptedFile_ThrowsAndLeavesFileUnchanged()
    {
        var path = _dir.Combine("technicians.json");
        await File.WriteAllTextAsync(path, "{ this is not valid json");
        var store = new JsonFileStore<Technician>(path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.UpdateAsync(items => { items.Add(new Technician()); return true; }));

        Assert.Equal("{ this is not valid json", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task UpdateAsync_ChangeThrows_NothingIsSaved()
    {
        var path = _dir.Combine("technicians.json");
        var store = new JsonFileStore<Technician>(path);
        await store.UpdateAsync(items => { items.Add(new Technician { FirstName = "Keep" }); return true; });

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync<bool>(items =>
        {
            items.Clear();
            throw new InvalidOperationException();
        }));

        Assert.Equal("Keep", Assert.Single(await store.ReadAllAsync()).FirstName);
    }

    [Fact]
    public async Task UpdateAsync_ConcurrentAdds_KeepsEveryItem()
    {
        var store = new JsonFileStore<Technician>(_dir.Combine("technicians.json"));

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            store.UpdateAsync(items => { items.Add(new Technician { FirstName = $"T{i}" }); return true; })));

        Assert.Equal(20, (await store.ReadAllAsync()).Count);
    }
}
