// Purpose: Team management rules: add, rename, list, and remove technicians.
// Removing a technician also removes them from every job they were assigned to (a database cascade).
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public class TechnicianService
{
    private const int MaxNameLength = 100;

    private readonly ITechnicianRepository _technicians;

    public TechnicianService(ITechnicianRepository technicians)
    {
        _technicians = technicians;
    }

    /// <summary>Returns all technicians sorted by last name, then first name.</summary>
    public async Task<List<Technician>> GetAllAsync() =>
        (await _technicians.GetAllAsync())
            .OrderBy(t => t.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.FirstName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public Task<Technician?> GetAsync(Guid id) => _technicians.GetByIdAsync(id);

    public async Task<Technician> AddAsync(string? firstName, string? lastName)
    {
        var technician = new Technician
        {
            Id = Guid.NewGuid(),
            FirstName = RequireName(firstName, "FirstName", "First name"),
            LastName = RequireName(lastName, "LastName", "Last name")
        };

        await _technicians.AddAsync(technician);
        return technician;
    }

    public async Task<bool> UpdateAsync(Guid id, string? firstName, string? lastName)
    {
        var technician = new Technician
        {
            Id = id,
            FirstName = RequireName(firstName, "FirstName", "First name"),
            LastName = RequireName(lastName, "LastName", "Last name")
        };

        return await _technicians.UpdateAsync(technician);
    }

    /// <summary>Deletes the technician and unassigns them from all jobs. Returns false if not found.</summary>
    public Task<bool> DeleteAsync(Guid id) => _technicians.DeleteAsync(id);

    private static string RequireName(string? value, string field, string label)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException(field, $"{label} is required.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ValidationFailedException(field, $"{label} must be {MaxNameLength} characters or fewer.");
        }

        return trimmed;
    }
}
