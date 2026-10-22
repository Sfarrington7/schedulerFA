// Purpose: Stores jobs (with nested buildings and file metadata) in jobs.json via JsonFileStore.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public class JsonJobRepository : IJobRepository
{
    private readonly JsonFileStore<Job> _store;

    public JsonJobRepository(JsonFileStore<Job> store)
    {
        _store = store;
    }

    public Task<List<Job>> GetAllAsync() => _store.ReadAllAsync();

    public async Task<Job?> GetByIdAsync(Guid id) =>
        (await _store.ReadAllAsync()).FirstOrDefault(j => j.Id == id);

    public Task AddAsync(Job job) =>
        _store.UpdateAsync(items =>
        {
            items.Add(job);
            return true;
        });

    public Task<TResult?> UpdateAsync<TResult>(Guid id, Func<Job, TResult> change) =>
        _store.UpdateAsync(items =>
        {
            var job = items.FirstOrDefault(j => j.Id == id);
            return job is null ? default : change(job);
        });

    public Task UpdateAllAsync(Action<Job> change) =>
        _store.UpdateAsync(items =>
        {
            items.ForEach(change);
            return true;
        });

    public Task<Job?> DeleteAsync(Guid id) =>
        _store.UpdateAsync(items =>
        {
            var job = items.FirstOrDefault(j => j.Id == id);
            if (job is not null)
            {
                items.Remove(job);
            }

            return job;
        });
}
