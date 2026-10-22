// Purpose: Persistence boundary for technicians. The JSON implementation can later be swapped for EF Core.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface ITechnicianRepository
{
    Task<List<Technician>> GetAllAsync();
    Task<Technician?> GetByIdAsync(Guid id);
    Task AddAsync(Technician technician);

    /// <summary>Replaces the stored technician with the same Id. Returns false if it does not exist.</summary>
    Task<bool> UpdateAsync(Technician technician);

    Task<bool> DeleteAsync(Guid id);
}
