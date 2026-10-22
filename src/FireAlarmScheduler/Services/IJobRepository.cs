// Purpose: Persistence boundary for jobs, including their buildings and file metadata.
// The JSON implementation can later be swapped for EF Core.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface IJobRepository
{
    Task<List<Job>> GetAllAsync();
    Task<Job?> GetByIdAsync(Guid id);
    Task AddAsync(Job job);

    /// <summary>
    /// Loads the job, applies <paramref name="change"/>, and saves it as one atomic step.
    /// Returns null if the job does not exist; otherwise the value returned by <paramref name="change"/>.
    /// </summary>
    Task<TResult?> UpdateAsync<TResult>(Guid id, Func<Job, TResult> change);

    /// <summary>Applies <paramref name="change"/> to every job as one atomic step.</summary>
    Task UpdateAllAsync(Action<Job> change);

    /// <summary>Removes the job and returns it, or null if it did not exist.</summary>
    Task<Job?> DeleteAsync(Guid id);
}
