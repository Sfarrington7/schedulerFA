// Purpose: Job scheduling rules: create, edit, and delete jobs with their buildings, assign technicians, link jobs to
// recurring jobs, manage a job's buildings (keeping a series' building list and upcoming jobs in step), and answer
// date-range schedule queries.
using FireAlarmScheduler.Entities;

namespace FireAlarmScheduler.Services;

public class JobService
{
    private readonly IJobRepository _jobs;
    private readonly ITechnicianRepository _technicians;
    private readonly IFileStorage _fileStorage;
    private readonly RecurringJobService _recurringJobs;
    private readonly TimeProvider _timeProvider;

    public JobService(IJobRepository jobs, ITechnicianRepository technicians, IFileStorage fileStorage,
        RecurringJobService recurringJobs, TimeProvider timeProvider)
    {
        _jobs = jobs;
        _technicians = technicians;
        _fileStorage = fileStorage;
        _recurringJobs = recurringJobs;
        _timeProvider = timeProvider;
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
    /// Creates a job with the buildings in <see cref="JobInput.Buildings"/>. With <see cref="JobInput.RecurringJobId"/>
    /// it joins that recurring job (starting with the series' building list if no buildings are given), and any
    /// building new to the series is added to its list and to its upcoming jobs. With
    /// <see cref="JobInput.SaveAsRecurring"/> its details and buildings are saved as a new recurring job.
    /// </summary>
    public async Task<Job> CreateAsync(JobInput input)
    {
        var job = new Job { Id = Guid.NewGuid() };
        await ApplyInputAsync(job, input);

        RecurringJob? recurringJob = null;
        if (input.RecurringJobId is { } recurringJobId)
        {
            recurringJob = await _recurringJobs.GetAsync(recurringJobId)
                ?? throw new ValidationFailedException("RecurringJobId", "That recurring job no longer exists. Choose another or start a new job.");
            job.RecurringJobId = recurringJobId;
        }

        var buildings = ValidateBuildings(LockRecurringBuildingNames(input.Buildings, recurringJob))
            ?? recurringJob?.Buildings.OrderBy(b => b.SortOrder).Select(b => (b.Name, b.Notes)).ToList();

        RecurringJob? created = null;

        job.Buildings = (buildings ?? [])
            .Select((b, i) => new Building { Id = Guid.NewGuid(), JobId = job.Id, Name = b.Name, Notes = b.Notes, SortOrder = i })
            .ToList();

        if (input.RecurringJobId is null && input.SaveAsRecurring)
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

        if (input.RecurringJobId is not null)
        {
            await ShareBuildingsWithSeriesAsync(job, job.Buildings.Select(b => (b.Name, b.Notes)).ToList());
            await _recurringJobs.SyncDetailsFromJobAsync(job);
        }

        return job;
    }

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
            await _recurringJobs.SyncDetailsFromJobAsync(validated);
        }

        return updated;
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

    /// <summary>
    /// Adds a building to the job. If the job is part of a recurring series, the building is also added to the
    /// series' building list and to its upcoming jobs. Returns null if the job does not exist.
    /// </summary>
    public async Task<Building?> AddBuildingAsync(Guid jobId, string? name, string? notes)
    {
        var buildingName = RequireText(name, "Name", "Building name", 100);

        Job? owner = null;
        var added = await _jobs.UpdateAsync(jobId, job =>
        {
            owner = job;
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

        if (added is not null && owner is not null)
        {
            await ShareBuildingsWithSeriesAsync(owner, [(added.Name, added.Notes)]);
        }

        return added;
    }

    /// <summary>
    /// Renames a building or changes its notes. In a recurring series the same building on the series' list is
    /// updated too, so future jobs use the new name. Returns false if the job or building does not exist.
    /// </summary>
    public async Task<bool> UpdateBuildingAsync(Guid jobId, Guid buildingId, string? name, string? notes)
    {
        var buildingName = RequireText(name, "Name", "Building name", 100);

        var change = await _jobs.UpdateAsync(jobId, job =>
        {
            var building = job.Buildings.FirstOrDefault(b => b.Id == buildingId);
            if (building is null)
            {
                return null;
            }

            EnsureUniqueBuildingName(job, buildingName, exceptId: buildingId);
            var oldName = building.Name;
            building.Name = buildingName;
            building.Notes = Optional(notes);
            return new { job.RecurringJobId, OldName = oldName };
        });

        if (change?.RecurringJobId is { } recurringJobId)
        {
            await _recurringJobs.RenameBuildingAsync(recurringJobId, change.OldName, buildingName, Optional(notes));
        }

        return change is not null;
    }

    /// <summary>
    /// Adds a building to a recurring job's building list and to its upcoming jobs. Returns false if the recurring
    /// job doesn't exist; throws ValidationFailedException for a blank or too-long name.
    /// </summary>
    public async Task<bool> AddRecurringBuildingAsync(Guid recurringJobId, string? name, string? notes)
    {
        var buildingName = RequireText(name, "Name", "Building name", 100);
        if (await _recurringJobs.GetAsync(recurringJobId) is null)
        {
            return false;
        }

        await ShareBuildingsWithSeriesAsync(recurringJobId, exceptJobId: null, [(buildingName, Optional(notes))]);
        return true;
    }

    private Task ShareBuildingsWithSeriesAsync(Job job, List<(string Name, string? Notes)> buildings) =>
        job.RecurringJobId is { } recurringJobId
            ? ShareBuildingsWithSeriesAsync(recurringJobId, job.Id, buildings)
            : Task.CompletedTask;

    /// <summary>
    /// Adds buildings to the series' building list and to the series' upcoming jobs (not started yet and not
    /// completed, other than <paramref name="exceptJobId"/>) that don't already have a building with the same name.
    /// </summary>
    private async Task ShareBuildingsWithSeriesAsync(Guid recurringJobId, Guid? exceptJobId, List<(string Name, string? Notes)> buildings)
    {
        if (buildings.Count == 0)
        {
            return;
        }

        await _recurringJobs.AddBuildingsAsync(recurringJobId, buildings);

        var today = ScheduleCalendar.Today(_timeProvider);
        var upcoming = (await _jobs.GetAllAsync())
            .Where(j => j.RecurringJobId == recurringJobId && j.Id != exceptJobId && j.StartDate > today && j.Status != JobStatus.Completed);
        foreach (var other in upcoming)
        {
            await _jobs.UpdateAsync(other.Id, target =>
            {
                foreach (var (name, notes) in buildings.Where(b => !target.Buildings.Any(t => string.Equals(t.Name, b.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    target.Buildings.Add(new Building
                    {
                        Id = Guid.NewGuid(),
                        JobId = target.Id,
                        Name = name,
                        Notes = notes,
                        SortOrder = target.Buildings.Count == 0 ? 0 : target.Buildings.Max(b => b.SortOrder) + 1
                    });
                }

                return true;
            });
        }
    }

    /// <summary>
    /// Rows that came from the recurring job's building list can't be renamed on the Create form: each such row takes
    /// the saved name of its recurring building, whatever name was posted. Throws if a row points at a building that
    /// is no longer on the list. Rows without a recurring building, or for a job not from a recurring job, are unchanged.
    /// </summary>
    private static IReadOnlyList<BuildingInput>? LockRecurringBuildingNames(IReadOnlyList<BuildingInput>? rows, RecurringJob? recurringJob)
    {
        if (rows is null || recurringJob is null)
        {
            return rows;
        }

        return rows.Select(row =>
        {
            if (row.RecurringBuildingId is not { } id)
            {
                return row;
            }

            var saved = recurringJob.Buildings.FirstOrDefault(b => b.Id == id)
                ?? throw new ValidationFailedException("Buildings",
                    "A building from the recurring job's list was removed while you were editing. Reload the form and try again.");
            return row with { Name = saved.Name };
        }).ToList();
    }

    /// <summary>
    /// Checks Create Job building rows: blank rows are dropped, names are trimmed and must be unique (ignoring case)
    /// and at most 100 characters. Returns null when no rows were given at all.
    /// </summary>
    private static List<(string Name, string? Notes)>? ValidateBuildings(IReadOnlyList<BuildingInput>? rows)
    {
        if (rows is null)
        {
            return null;
        }

        var buildings = rows.Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => (Name: r.Name!.Trim(), Notes: Optional(r.Notes)))
            .ToList();

        var tooLong = buildings.FirstOrDefault(b => b.Name.Length > 100 || b.Notes?.Length > 2000);
        if (tooLong.Name is not null)
        {
            throw new ValidationFailedException("Buildings", $"\"{tooLong.Name[..Math.Min(40, tooLong.Name.Length)]}\": building names are limited to 100 characters and notes to 2000.");
        }

        var duplicate = buildings.GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ValidationFailedException("Buildings", $"The building \"{duplicate.Key}\" is listed more than once.");
        }

        return buildings;
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
