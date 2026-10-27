// Purpose: Stores jobs, their technician assignments, buildings, and file metadata in SQL tables via EF Core.
// Each call uses its own short-lived DbContext, so results are detached snapshots like the old JSON store.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Services;

public class EfJobRepository : IJobRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EfJobRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<Job>> GetAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await WithChildren(db.Jobs).AsNoTracking().AsSplitQuery().ToListAsync();
    }

    public async Task<Job?> GetByIdAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await WithChildren(db.Jobs).AsNoTracking().AsSplitQuery().FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task AddAsync(Job job)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
    }

    public async Task<TResult?> UpdateAsync<TResult>(Guid id, Func<Job, TResult> change)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var job = await WithChildren(db.Jobs).AsSplitQuery().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return default;
        }

        // If change throws, SaveChanges is never reached and the database is left untouched.
        var result = change(job);
        await db.SaveChangesAsync();
        return result;
    }

    public async Task<Job?> DeleteAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var job = await WithChildren(db.Jobs).AsSplitQuery().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null)
        {
            return null;
        }

        db.Jobs.Remove(job);
        await db.SaveChangesAsync();
        return job;
    }

    /// <summary>Loads everything a job page needs, with buildings in the order they were added.</summary>
    private static IQueryable<Job> WithChildren(IQueryable<Job> jobs) =>
        jobs.Include(j => j.Assignments)
            .Include(j => j.Buildings.OrderBy(b => b.SortOrder))
            .ThenInclude(b => b.UploadedFiles);
}
