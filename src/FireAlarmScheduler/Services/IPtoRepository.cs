// Purpose: Persistence boundary for PTO requests.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface IPtoRepository
{
    Task<List<PtoRequest>> GetAllAsync();
    Task<List<PtoRequest>> GetForTechnicianAsync(Guid technicianId);
    Task<PtoRequest?> GetByIdAsync(Guid id);
    Task AddAsync(PtoRequest request);

    /// <summary>
    /// Loads the request, applies <paramref name="change"/>, and saves it. Returns null if the request does not
    /// exist; otherwise the value returned by <paramref name="change"/>. Nothing is saved if the change throws.
    /// </summary>
    Task<TResult?> UpdateAsync<TResult>(Guid id, Func<PtoRequest, TResult> change);

    Task<bool> DeleteAsync(Guid id);
}
