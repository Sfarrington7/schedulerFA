// Purpose: Recurring job rules: list, save, and remove recurring jobs; find the building list a new job in the
// series should start with; and keep a recurring job's details matching the newest job in its series.
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

    /// <summary>Removes a recurring job. Its jobs and their buildings stay; they are just no longer linked.</summary>
    public Task<bool> DeleteAsync(Guid id) => _recurringJobs.DeleteAsync(id);

    /// <summary>
    /// The newest job in the series (by start date), whose buildings a new job copies, or null if the series has no jobs.
    /// <paramref name="exceptJobId"/> leaves one job out, such as the job being created.
    /// </summary>
    public async Task<Job?> GetLatestJobAsync(Guid recurringJobId, Guid? exceptJobId = null) =>
        (await _jobs.GetAllAsync())
            .Where(j => j.RecurringJobId == recurringJobId && j.Id != exceptJobId)
            .OrderByDescending(j => j.StartDate)
            .ThenByDescending(j => j.StartTime)
            .FirstOrDefault();

    /// <summary>
    /// Saves a new recurring job from a job's details. Throws ValidationFailedException if another recurring
    /// job already has the same name, so the list of recurring jobs stays unambiguous.
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
        await _recurringJobs.AddAsync(recurringJob);
        return recurringJob;
    }

    /// <summary>
    /// Copies the job's name, customer, site, and address onto its recurring job, but only when this job is the
    /// newest in the series, so editing an older job never overwrites newer details.
    /// </summary>
    public async Task SyncFromJobAsync(Job job)
    {
        if (job.RecurringJobId is not { } recurringJobId
            || (await GetLatestJobAsync(recurringJobId))?.Id != job.Id
            || await _recurringJobs.GetByIdAsync(recurringJobId) is not { } recurringJob)
        {
            return;
        }

        CopyDetails(job, recurringJob);
        await _recurringJobs.UpdateAsync(recurringJob);
    }

    private static void CopyDetails(Job job, RecurringJob recurringJob)
    {
        recurringJob.Name = job.Name;
        recurringJob.CustomerName = job.CustomerName;
        recurringJob.SiteName = job.SiteName;
        recurringJob.Address = job.Address;
    }
}
