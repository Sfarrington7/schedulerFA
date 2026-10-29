// Purpose: Stores recurring jobs in the RecurringJobs SQL table via EF Core, one short-lived DbContext per call.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Services;

public class EfRecurringJobRepository : IRecurringJobRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EfRecurringJobRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<RecurringJob>> GetAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.RecurringJobs.AsNoTracking().ToListAsync();
    }

    public async Task<RecurringJob?> GetByIdAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.RecurringJobs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task AddAsync(RecurringJob recurringJob)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.RecurringJobs.Add(recurringJob);
        await db.SaveChangesAsync();
    }

    public async Task<bool> UpdateAsync(RecurringJob recurringJob)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        if (!await db.RecurringJobs.AnyAsync(r => r.Id == recurringJob.Id))
        {
            return false;
        }

        db.RecurringJobs.Update(recurringJob);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var recurringJob = await db.RecurringJobs.FirstOrDefaultAsync(r => r.Id == id);
        if (recurringJob is null)
        {
            return false;
        }

        db.RecurringJobs.Remove(recurringJob);
        await db.SaveChangesAsync();
        return true;
    }
}
