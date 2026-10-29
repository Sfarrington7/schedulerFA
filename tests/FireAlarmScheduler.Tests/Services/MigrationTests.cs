// Purpose: Tests data-moving migrations against a real SQL Server database: the database is migrated to the step
// before, seeded with rows in the old shape, then migrated to the latest version and checked.
using FireAlarmScheduler.Data;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FireAlarmScheduler.Tests.Services;

public class MigrationTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AddRecurringJobBuildings_SeedsEachSeriesFromItsNewestJob()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(TestDatabase.ConnectionStringFor(_dir.DatabaseName))
            .Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029014632_AddRecurringJobs");

        // One series with a 2025 job (Old Wing) and a newer 2026 job (A, B, C in that order); plus a one-off job.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @r uniqueidentifier = '11111111-1111-1111-1111-111111111111';
            DECLARE @old uniqueidentifier = NEWID(), @new uniqueidentifier = NEWID(), @oneOff uniqueidentifier = NEWID();
            INSERT INTO RecurringJobs (Id, Name, Address) VALUES (@r, 'Central Plaza', '100 Main Street');
            INSERT INTO Jobs (Id, Name, Address, StartDate, EndDate, StartTime, EndTime, Status, RecurringJobId) VALUES
                (@old, 'Central Plaza', '100 Main Street', '2025-11-03', '2025-11-07', '08:00', '16:00', 'Completed', @r),
                (@new, 'Central Plaza', '100 Main Street', '2026-11-02', '2026-11-06', '08:00', '16:00', 'Scheduled', @r),
                (@oneOff, 'One-off', '1 Side St', '2026-12-01', '2026-12-01', '08:00', '16:00', 'Scheduled', NULL);
            INSERT INTO Buildings (Id, JobId, Name, Notes, SortOrder) VALUES
                (NEWID(), @old, 'Old Wing', NULL, 0),
                (NEWID(), @new, 'Building C', NULL, 5),
                (NEWID(), @new, 'Building A', 'Panel in lobby', 1),
                (NEWID(), @new, 'Building B', NULL, 3),
                (NEWID(), @oneOff, 'Shed', NULL, 0);
            """);

        await migrator.MigrateAsync();

        var buildings = await db.RecurringJobBuildings.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync();
        Assert.Equal(["Building A", "Building B", "Building C"], buildings.Select(b => b.Name));
        Assert.Equal([0, 1, 2], buildings.Select(b => b.SortOrder));
        Assert.Equal("Panel in lobby", buildings[0].Notes);
        Assert.All(buildings, b => Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), b.RecurringJobId));
    }
}
