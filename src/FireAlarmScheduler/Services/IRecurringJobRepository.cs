// Purpose: Persistence boundary for recurring jobs.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface IRecurringJobRepository
{
    Task<List<RecurringJob>> GetAllAsync();
    Task<RecurringJob?> GetByIdAsync(Guid id);
    Task AddAsync(RecurringJob recurringJob);

    /// <summary>Replaces the stored recurring job with the same Id. Returns false if it does not exist.</summary>
    Task<bool> UpdateAsync(RecurringJob recurringJob);

    /// <summary>Deletes the recurring job; its jobs are kept and unlinked by the database. Returns false if not found.</summary>
    Task<bool> DeleteAsync(Guid id);
}
