// Purpose: Job scheduling rules: create, edit, and delete jobs, assign technicians, link jobs to recurring
// jobs (copying the series' building list), manage a job's buildings, and answer date-range schedule queries.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public class JobService
{
    private readonly IJobRepository _jobs;
    private readonly ITechnicianRepository _technicians;
    private readonly IFileStorage _fileStorage;
    private readonly RecurringJobService _recurringJobs;

    public JobService(IJobRepository jobs, ITechnicianRepository technicians, IFileStorage fileStorage, RecurringJobService recurringJobs)
    {
        _jobs = jobs;
        _technicians = technicians;
        _fileStorage = fileStorage;
        _recurringJobs = recurringJobs;
    }

    /// <summary>Returns all jobs ordered by start date and time.</summary>
    public async Task<List<Job>> GetAllAsync() => Sort(await _jobs.GetAllAsync());

    public Task<Job?> GetAsync(Guid id) => _jobs.GetByIdAsync(id);

    /// <summary>Returns jobs that run on at least one day between <paramref name="from"/> and <paramref name="to"/> inclusive.</summary>
    public async Task<List<Job>> GetJobsInRangeAsync(DateOnly from, DateOnly to) =>
        Sort((await _jobs.GetAllAsync()).Where(j => j.OverlapsRange(from, to)));

    /// <summary>Returns the technician's assigned jobs that run on at least one day in the range.</summary>
    public Task<List<Job>> GetJobsForTechnicianAsync(Guid technicianId, DateOnly from, DateOnly to) =>
        GetJobsForTechniciansAsync([technicianId], from, to);

    /// <summary>
    /// Returns jobs in the range assigned to at least one of <paramref name="technicianIds"/>, each job once.
    /// An empty set of technicians returns no jobs.
    /// </summary>
    public async Task<List<Job>> GetJobsForTechniciansAsync(IReadOnlyCollection<Guid> technicianIds, DateOnly from, DateOnly to) =>
        Sort((await _jobs.GetAllAsync())
            .Where(j => j.OverlapsRange(from, to) && j.AssignedTechnicianIds.Any(technicianIds.Contains)));

    /// <summary>
    /// Creates a job. With <see cref="JobInput.RecurringJobId"/> it joins that recurring job and starts with a copy
    /// of the building list (names and notes, not files) from the newest job in the series; with
    /// <see cref="JobInput.SaveAsRecurring"/> its details are saved as a new recurring job.
    /// </summary>
    public async Task<Job> CreateAsync(JobInput input)
    {
        var job = new Job { Id = Guid.NewGuid() };
        await ApplyInputAsync(job, input);

        RecurringJob? created = null;
        if (input.RecurringJobId is { } recurringJobId)
        {
            if (await _recurringJobs.GetAsync(recurringJobId) is null)
            {
                throw new ValidationFailedException("RecurringJobId", "That recurring job no longer exists. Choose another or start a new job.");
            }

            job.RecurringJobId = recurringJobId;
            CopyBuildings(await _recurringJobs.GetLatestJobAsync(recurringJobId), job);
        }
        else if (input.SaveAsRecurring)
        {
            created = await _recurringJobs.CreateFromAsync(job);
            job.RecurringJobId = created.Id;
        }

        try
        {
            await _jobs.AddAsync(job);
        }
        catch when (created is not null)
        {
            // Don't leave a recurring job behind with no job if saving the job itself failed.
            await _recurringJobs.DeleteAsync(created.Id);
            throw;
        }

        await _recurringJobs.SyncFromJobAsync(job);
        return job;
    }

    /// <summary>Updates the job's details and assignments, keeping its buildings and files. Returns false if not found.</summary>
    /// <remarks>
    /// The recurring link can't be changed here except that a one-off job can be saved as a new recurring job
    /// (<see cref="JobInput.SaveAsRecurring"/>). If this is the newest job in its series, the recurring job's
    /// details are updated to match.
    /// </remarks>
    public async Task<bool> UpdateAsync(Guid id, JobInput input)
    {
        // Validate against a scratch copy first so invalid input never reaches storage.
        var validated = new Job { Id = id };
        await ApplyInputAsync(validated, input);

        var existing = await _jobs.GetByIdAsync(id);
        if (existing is null)
        {
            return false;
        }

        validated.RecurringJobId = existing.RecurringJobId;
        if (validated.RecurringJobId is null && input.SaveAsRecurring)
        {
            validated.RecurringJobId = (await _recurringJobs.CreateFromAsync(validated)).Id;
        }

        var updated = await _jobs.UpdateAsync(id, job =>
        {
            job.RecurringJobId = validated.RecurringJobId;
            job.Name = validated.Name;
            job.CustomerName = validated.CustomerName;
            job.SiteName = validated.SiteName;
            job.Address = validated.Address;
            job.StartDate = validated.StartDate;
            job.EndDate = validated.EndDate;
            job.StartTime = validated.StartTime;
            job.EndTime = validated.EndTime;
            job.Status = validated.Status;
            job.Notes = validated.Notes;
            job.SetAssignedTechnicians(validated.AssignedTechnicianIds);
            return true;
        });

        if (updated)
        {
            await _recurringJobs.SyncFromJobAsync(validated);
        }

        return updated;
    }

    /// <summary>Gives <paramref name="job"/> new buildings with the same names, notes, and order as <paramref name="source"/>'s.</summary>
    private static void CopyBuildings(Job? source, Job job)
    {
        if (source is null)
        {
            return;
        }

        job.Buildings = source.Buildings
            .OrderBy(b => b.SortOrder)
            .Select((b, i) => new Building { Id = Guid.NewGuid(), JobId = job.Id, Name = b.Name, Notes = b.Notes, SortOrder = i })
            .ToList();
    }

    /// <summary>Deletes the job and every spreadsheet stored for its buildings. Returns false if not found.</summary>
    public async Task<bool> DeleteAsync(Guid id)
    {
        var job = await _jobs.DeleteAsync(id);
        if (job is null)
        {
            return false;
        }

        foreach (var file in job.Buildings.SelectMany(b => b.UploadedFiles))
        {
            _fileStorage.Delete(file.StoredFileName);
        }

        return true;
    }

    /// <summary>Adds a building to the job. Returns null if the job does not exist.</summary>
    public async Task<Building?> AddBuildingAsync(Guid jobId, string? name, string? notes)
    {
        var buildingName = RequireText(name, "Name", "Building name", 100);

        return await _jobs.UpdateAsync(jobId, job =>
        {
            EnsureUniqueBuildingName(job, buildingName, exceptId: null);
            var building = new Building
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Name = buildingName,
                Notes = Optional(notes),
                SortOrder = job.Buildings.Count == 0 ? 0 : job.Buildings.Max(b => b.SortOrder) + 1
            };
            job.Buildings.Add(building);
            return building;
        });
    }

    /// <summary>Renames a building or changes its notes. Returns false if the job or building does not exist.</summary>
    public async Task<bool> UpdateBuildingAsync(Guid jobId, Guid buildingId, string? name, string? notes)
    {
        var buildingName = RequireText(name, "Name", "Building name", 100);

        return await _jobs.UpdateAsync(jobId, job =>
        {
            var building = job.Buildings.FirstOrDefault(b => b.Id == buildingId);
            if (building is null)
            {
                return false;
            }

            EnsureUniqueBuildingName(job, buildingName, exceptId: buildingId);
            building.Name = buildingName;
            building.Notes = Optional(notes);
            return true;
        });
    }

    /// <summary>Removes a building and deletes its stored spreadsheets. Returns false if not found.</summary>
    public async Task<bool> DeleteBuildingAsync(Guid jobId, Guid buildingId)
    {
        var removed = await _jobs.UpdateAsync(jobId, job =>
        {
            var building = job.Buildings.FirstOrDefault(b => b.Id == buildingId);
            if (building is not null)
            {
                job.Buildings.Remove(building);
            }

            return building;
        });

        if (removed is null)
        {
            return false;
        }

        foreach (var file in removed.UploadedFiles)
        {
            _fileStorage.Delete(file.StoredFileName);
        }

        return true;
    }

    /// <summary>
    /// Validates <paramref name="input"/> and copies it onto <paramref name="job"/>.
    /// Throws ValidationFailedException listing every problem found.
    /// </summary>
    private async Task ApplyInputAsync(Job job, JobInput input)
    {
        var errors = new Dictionary<string, string>();

        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            errors["Name"] = "Job name is required.";
        }

        var address = input.Address?.Trim() ?? "";
        if (address.Length == 0)
        {
            errors["Address"] = "Street address is required.";
        }

        if (input.EndDate < input.StartDate)
        {
            errors["EndDate"] = "End date cannot be before the start date.";
        }

        if (input.EndTime <= input.StartTime)
        {
            errors["EndTime"] = "End time must be after the start time.";
        }

        if (!Enum.IsDefined(input.Status))
        {
            errors["Status"] = "Choose a valid status.";
        }

        var knownIds = (await _technicians.GetAllAsync()).Select(t => t.Id).ToHashSet();
        if (input.AssignedTechnicianIds.Any(id => !knownIds.Contains(id)))
        {
            errors["AssignedTechnicianIds"] = "One or more selected technicians no longer exist.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        job.Name = name;
        job.CustomerName = Optional(input.CustomerName);
        job.SiteName = Optional(input.SiteName);
        job.Address = address;
        job.StartDate = input.StartDate;
        job.EndDate = input.EndDate;
        job.StartTime = input.StartTime;
        job.EndTime = input.EndTime;
        job.Status = input.Status;
        job.Notes = Optional(input.Notes);
        job.SetAssignedTechnicians(input.AssignedTechnicianIds);
    }

    private static void EnsureUniqueBuildingName(Job job, string name, Guid? exceptId)
    {
        if (job.Buildings.Any(b => b.Id != exceptId && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationFailedException("Name", $"This job already has a building named \"{name}\".");
        }
    }

    private static string RequireText(string? value, string field, string label, int maxLength)
    {
        var trimmed = value?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw new ValidationFailedException(field, $"{label} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new ValidationFailedException(field, $"{label} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<Job> Sort(IEnumerable<Job> jobs) =>
        jobs.OrderBy(j => j.StartDate).ThenBy(j => j.StartTime).ThenBy(j => j.Name).ToList();
}
