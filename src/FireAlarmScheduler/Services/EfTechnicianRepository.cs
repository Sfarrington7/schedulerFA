// Purpose: Stores technicians in the Technicians SQL table via EF Core.
// Deleting a technician cascades to their job assignments and unlinks any sign-in account.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Services;

public class EfTechnicianRepository : ITechnicianRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EfTechnicianRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Technician>> GetAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Technicians.AsNoTracking().ToListAsync();
    }

    public async Task<Technician?> GetByIdAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Technicians.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task AddAsync(Technician technician)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.Technicians.Add(technician);
        await db.SaveChangesAsync();
    }

    public async Task<bool> UpdateAsync(Technician technician)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var stored = await db.Technicians.FirstOrDefaultAsync(t => t.Id == technician.Id);
        if (stored is null)
        {
            return false;
        }

        stored.FirstName = technician.FirstName;
        stored.LastName = technician.LastName;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Technicians.Where(t => t.Id == id).ExecuteDeleteAsync() > 0;
    }
}
