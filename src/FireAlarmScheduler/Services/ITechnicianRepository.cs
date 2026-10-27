// Purpose: Persistence boundary for technicians. Implemented over a SQL table by EfTechnicianRepository.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public interface ITechnicianRepository
{
    Task<List<Technician>> GetAllAsync();
    Task<Technician?> GetByIdAsync(Guid id);
    Task AddAsync(Technician technician);

    /// <summary>Replaces the stored technician with the same Id. Returns false if it does not exist.</summary>
    Task<bool> UpdateAsync(Technician technician);

    /// <summary>Deletes the technician and, through the database, their job assignments. Returns false if not found.</summary>
    Task<bool> DeleteAsync(Guid id);
}
