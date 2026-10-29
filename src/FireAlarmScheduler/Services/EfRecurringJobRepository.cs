// Purpose: Stores recurring jobs and their building lists in SQL tables via EF Core, one short-lived DbContext per call.
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
        return await WithBuildings(db.RecurringJobs).AsNoTracking().ToListAsync();
    }

    public async Task<RecurringJob?> GetByIdAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await WithBuildings(db.RecurringJobs).AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task AddAsync(RecurringJob recurringJob)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.RecurringJobs.Add(recurringJob);
        await db.SaveChangesAsync();
    }

    public async Task<TResult?> UpdateAsync<TResult>(Guid id, Func<RecurringJob, TResult> change)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var recurringJob = await WithBuildings(db.RecurringJobs).FirstOrDefaultAsync(r => r.Id == id);
        if (recurringJob is null)
        {
            return default;
        }

        var result = change(recurringJob);
        await db.SaveChangesAsync();
        return result;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var recurringJob = await WithBuildings(db.RecurringJobs).FirstOrDefaultAsync(r => r.Id == id);
        if (recurringJob is null)
        {
            return false;
        }

        db.RecurringJobs.Remove(recurringJob);
        await db.SaveChangesAsync();
        return true;
    }

    private static IQueryable<RecurringJob> WithBuildings(IQueryable<RecurringJob> recurringJobs) =>
        recurringJobs.Include(r => r.Buildings.OrderBy(b => b.SortOrder));
}
