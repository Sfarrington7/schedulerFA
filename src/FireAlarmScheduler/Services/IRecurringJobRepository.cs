// Purpose: Persistence boundary for recurring jobs and their standing building lists.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface IRecurringJobRepository
{
    /// <summary>All recurring jobs, each with its buildings in display order.</summary>
    Task<List<RecurringJob>> GetAllAsync();

    Task<RecurringJob?> GetByIdAsync(Guid id);
    Task AddAsync(RecurringJob recurringJob);

    /// <summary>
    /// Loads the recurring job with its buildings, applies <paramref name="change"/>, and saves it. Returns null if
    /// it does not exist; otherwise the value returned by <paramref name="change"/>. Nothing is saved if the change throws.
    /// </summary>
    Task<TResult?> UpdateAsync<TResult>(Guid id, Func<RecurringJob, TResult> change);

    /// <summary>Deletes the recurring job and its building list; its jobs are kept and unlinked. Returns false if not found.</summary>
    Task<bool> DeleteAsync(Guid id);
}
