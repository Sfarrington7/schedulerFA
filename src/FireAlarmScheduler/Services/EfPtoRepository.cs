// Purpose: Stores PTO requests in the PtoRequests SQL table via EF Core, one short-lived DbContext per call.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Entities;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Services;

public class EfPtoRepository : IPtoRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EfPtoRepository(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<PtoRequest>> GetAllAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.PtoRequests.AsNoTracking().ToListAsync();
    }

    public async Task<List<PtoRequest>> GetForTechnicianAsync(Guid technicianId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.PtoRequests.AsNoTracking().Where(p => p.TechnicianId == technicianId).ToListAsync();
    }

    public async Task<PtoRequest?> GetByIdAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.PtoRequests.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task AddAsync(PtoRequest request)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        db.PtoRequests.Add(request);
        await db.SaveChangesAsync();
    }

    public async Task<TResult?> UpdateAsync<TResult>(Guid id, Func<PtoRequest, TResult> change)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var request = await db.PtoRequests.FirstOrDefaultAsync(p => p.Id == id);
        if (request is null)
        {
            return default;
        }

        var result = change(request);
        await db.SaveChangesAsync();
        return result;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.PtoRequests.Where(p => p.Id == id).ExecuteDeleteAsync() > 0;
    }
}
