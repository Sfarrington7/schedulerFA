// Purpose: Recurring job rules: list, save, and remove recurring jobs; keep each one's details matching the newest
// job in its series; and maintain its standing building list, which every new job in the series starts with.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

/// <summary>A recurring job with how many jobs belong to it and when the newest one starts.</summary>
public record RecurringJobSummary(RecurringJob RecurringJob, int JobCount, DateOnly? LatestStartDate);

public class RecurringJobService
{
    private readonly IRecurringJobRepository _recurringJobs;
    private readonly IJobRepository _jobs;

    public RecurringJobService(IRecurringJobRepository recurringJobs, IJobRepository jobs)
    {
        _recurringJobs = recurringJobs;
        _jobs = jobs;
    }

    /// <summary>All recurring jobs sorted by name, with their job counts.</summary>
    public async Task<List<RecurringJobSummary>> GetAllAsync()
    {
        var jobs = await _jobs.GetAllAsync();
        return (await _recurringJobs.GetAllAsync())
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r =>
            {
                var series = jobs.Where(j => j.RecurringJobId == r.Id).ToList();
                return new RecurringJobSummary(r, series.Count, series.Count == 0 ? null : series.Max(j => j.StartDate));
            })
            .ToList();
    }

    public Task<RecurringJob?> GetAsync(Guid id) => _recurringJobs.GetByIdAsync(id);

    /// <summary>Removes a recurring job and its building list. Its jobs and their buildings stay; they are just unlinked.</summary>
    public Task<bool> DeleteAsync(Guid id) => _recurringJobs.DeleteAsync(id);

    /// <summary>The newest job in the series (by start date and time), or null if the series has no jobs.</summary>
    public async Task<Job?> GetLatestJobAsync(Guid recurringJobId) =>
        (await _jobs.GetAllAsync())
            .Where(j => j.RecurringJobId == recurringJobId)
            .OrderByDescending(j => j.StartDate)
            .ThenByDescending(j => j.StartTime)
            .FirstOrDefault();

    /// <summary>
    /// Saves a new recurring job from a job's details and buildings. Throws ValidationFailedException if another
    /// recurring job already has the same name, so the list of recurring jobs stays unambiguous.
    /// </summary>
    public async Task<RecurringJob> CreateFromAsync(Job job)
    {
        if ((await _recurringJobs.GetAllAsync()).Any(r => string.Equals(r.Name, job.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationFailedException("Name",
                $"A recurring job named \"{job.Name}\" already exists. Choose it from the recurring job list instead.");
        }

        var recurringJob = new RecurringJob { Id = Guid.NewGuid() };
        CopyDetails(job, recurringJob);
        foreach (var building in job.Buildings.OrderBy(b => b.SortOrder))
        {
            recurringJob.AddBuildingIfMissing(building.Name, building.Notes);
        }

        await _recurringJobs.AddAsync(recurringJob);
        return recurringJob;
    }

    /// <summary>
    /// Copies the job's name, customer, site, and address onto its recurring job, but only when this job is the
    /// newest in the series, so editing an older job never overwrites newer details.
    /// </summary>
    public async Task SyncDetailsFromJobAsync(Job job)
    {
        if (job.RecurringJobId is not { } recurringJobId || (await GetLatestJobAsync(recurringJobId))?.Id != job.Id)
        {
            return;
        }

        await _recurringJobs.UpdateAsync(recurringJobId, recurringJob =>
        {
            CopyDetails(job, recurringJob);
            return true;
        });
    }

    /// <summary>Adds buildings to the standing list, skipping names already on it. Returns false if the recurring job doesn't exist.</summary>
    public async Task<bool> AddBuildingsAsync(Guid recurringJobId, IEnumerable<(string Name, string? Notes)> buildings) =>
        await _recurringJobs.UpdateAsync(recurringJobId, recurringJob =>
        {
            foreach (var (name, notes) in buildings)
            {
                recurringJob.AddBuildingIfMissing(name, notes);
            }

            return true;
        });

    /// <summary>
    /// Renames the standing-list building called <paramref name="oldName"/> and updates its notes. Does nothing if
    /// that building isn't on the list or another building already has <paramref name="newName"/>.
    /// </summary>
    public Task RenameBuildingAsync(Guid recurringJobId, string oldName, string newName, string? notes) =>
        _recurringJobs.UpdateAsync(recurringJobId, recurringJob =>
        {
            var building = recurringJob.Buildings.FirstOrDefault(b => string.Equals(b.Name, oldName, StringComparison.OrdinalIgnoreCase));
            var taken = recurringJob.Buildings.Any(b => b != building && string.Equals(b.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (building is not null && !taken)
            {
                building.Name = newName;
                building.Notes = notes;
            }

            return true;
        });

    /// <summary>Takes a building off the standing list so future jobs no longer start with it. Existing jobs keep theirs.</summary>
    public async Task<bool> RemoveBuildingAsync(Guid recurringJobId, Guid buildingId) =>
        await _recurringJobs.UpdateAsync(recurringJobId, recurringJob =>
            recurringJob.Buildings.RemoveAll(b => b.Id == buildingId) > 0);

    private static void CopyDetails(Job job, RecurringJob recurringJob)
    {
        recurringJob.Name = job.Name;
        recurringJob.CustomerName = job.CustomerName;
        recurringJob.SiteName = job.SiteName;
        recurringJob.Address = job.Address;
    }
}
