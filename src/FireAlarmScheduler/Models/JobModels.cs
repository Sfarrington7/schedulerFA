// Purpose: View and form models for jobs (including the recurring-job choice and the technician picker with each
// technician's conflicts), their buildings, uploaded files, and the Recurring jobs page.
using System.ComponentModel.DataAnnotations;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Models;

public class JobFormModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(200), Display(Name = "Job / project name")]
    public string Name { get; set; } = "";

    [StringLength(200), Display(Name = "Customer (optional)")]
    public string? CustomerName { get; set; }

    [StringLength(200), Display(Name = "Site / location (optional)")]
    public string? SiteName { get; set; }

    [Required, StringLength(300), Display(Name = "Street address")]
    public string Address { get; set; } = "";

    [Display(Name = "Start date")]
    public DateOnly StartDate { get; set; }

    [Display(Name = "End date")]
    public DateOnly EndDate { get; set; }

    [Display(Name = "Daily start time")]
    public TimeOnly StartTime { get; set; } = new(8, 0);

    [Display(Name = "Daily end time")]
    public TimeOnly EndTime { get; set; } = new(16, 0);

    public JobStatus Status { get; set; } = JobStatus.Scheduled;

    [StringLength(4000), Display(Name = "Notes / instructions")]
    public string? Notes { get; set; }

    [Display(Name = "Assigned technicians")]
    public List<Guid> AssignedTechnicianIds { get; set; } = [];

    /// <summary>Choices for the technician picker, with any conflicts at the form's times; filled by the controller.</summary>
    public List<TechnicianChoice> AvailableTechnicians { get; set; } = [];

    /// <summary>False when the form's dates or times are invalid, so availability couldn't be checked.</summary>
    public bool AvailabilityChecked { get; set; }

    public JobSlot Slot => new(Id, StartDate, EndDate, StartTime, EndTime);

    /// <summary>On create: the recurring job this new job starts from, or null for a brand-new job.</summary>
    [Display(Name = "Job / project")]
    public Guid? RecurringJobId { get; set; }

    /// <summary>Save this job's details as a new recurring job (new jobs, or one-off jobs being edited).</summary>
    [Display(Name = "Save as a recurring job")]
    public bool SaveAsRecurring { get; set; }

    /// <summary>Recurring jobs to choose from on create; filled by the controller, not posted back.</summary>
    public List<RecurringJobOption> AvailableRecurringJobs { get; set; } = [];

    /// <summary>On edit: the name of the recurring job this job belongs to, if any; filled by the controller.</summary>
    public string? LinkedRecurringJobName { get; set; }

    /// <summary>On create: the job's buildings. Starts with the recurring job's building list when one is chosen.</summary>
    public List<BuildingRowModel> Buildings { get; set; } = [];

    public bool IsNew => Id is null;

    public JobInput ToInput() => new(Name, CustomerName, SiteName, Address, StartDate, EndDate,
        StartTime, EndTime, Status, Notes, AssignedTechnicianIds,
        IsNew ? RecurringJobId : null, SaveAsRecurring && (!IsNew || RecurringJobId is null),
        IsNew ? Buildings.Select(b => new BuildingInput(b.Name, b.Notes, b.RecurringBuildingId)).ToList() : null);

    public static JobFormModel FromEntity(Job job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        CustomerName = job.CustomerName,
        SiteName = job.SiteName,
        Address = job.Address,
        StartDate = job.StartDate,
        EndDate = job.EndDate,
        StartTime = job.StartTime,
        EndTime = job.EndTime,
        Status = job.Status,
        Notes = job.Notes,
        AssignedTechnicianIds = [.. job.AssignedTechnicianIds]
    };
}

/// <summary>
/// One building row on the Create Job form; rows left blank are ignored. Rows from a recurring job's building list
/// carry <see cref="RecurringBuildingId"/> and show their name read-only.
/// </summary>
public class BuildingRowModel
{
    public Guid? RecurringBuildingId { get; set; }

    [StringLength(100)]
    public string? Name { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

/// <summary>
/// A technician in the job form's picker. <see cref="Conflicts"/> describes what assigning them would clash with;
/// a conflicting technician can still be assigned.
/// </summary>
public record TechnicianChoice(Guid Id, string FullName, List<string> Conflicts)
{
    public bool IsFree => Conflicts.Count == 0;

    /// <summary>One line describing a conflict for the proposed job, e.g. "Booked on Smith Tower, Thu, Nov 5, 8:00 AM – 4:00 PM".</summary>
    public static string Describe(ScheduleConflict conflict)
    {
        var dates = ConflictJobViewModel.From(conflict.Job, conflict.FirstDate, conflict.LastDate).DatesText;
        return conflict.OtherJob is { } other
            ? $"Booked on {other.Name}, {dates}, {other.StartTime:h:mm tt} – {other.EndTime:h:mm tt}"
            : $"On approved PTO, {dates}";
    }
}

/// <summary>A technician's availability, as returned to the job form's script when the dates or times change.</summary>
public record TechnicianAvailability(Guid Id, List<string> Conflicts);

/// <summary>A recurring job in the Create Job picker.</summary>
public record RecurringJobOption(Guid Id, string Name, string Detail);

/// <summary>A row on the Recurring jobs page.</summary>
public record RecurringJobListItemViewModel(Guid Id, string Name, string? CustomerName, string? SiteName, string Address,
    int JobCount, DateOnly? LatestStartDate, List<string> BuildingNames);

/// <summary>A recurring job's page: its details and the standing building list future jobs start with.</summary>
public class RecurringJobDetailsViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? SiteName { get; init; }
    public string Address { get; init; } = "";
    public List<BuildingViewModel> Buildings { get; init; } = [];
    public BuildingFormModel NewBuilding { get; init; } = new();
}

public class JobSummaryViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string? CustomerName { get; init; }
    public string? SiteName { get; init; }
    public string Address { get; init; } = "";
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public JobStatus Status { get; init; }
    public List<TechnicianOption> Technicians { get; init; } = [];
    public List<string> BuildingNames { get; init; } = [];
    public bool IsRecurring { get; init; }

    public string DateRangeText => StartDate == EndDate
        ? StartDate.ToString("ddd, MMM d, yyyy")
        : $"{StartDate:ddd, MMM d} – {EndDate:ddd, MMM d, yyyy}";

    public string TimeRangeText => $"{StartTime:h:mm tt} – {EndTime:h:mm tt}";

    /// <summary>True when the job runs on any day from <paramref name="from"/> to <paramref name="to"/> inclusive.</summary>
    public bool OverlapsRange(DateOnly from, DateOnly to) => StartDate <= to && EndDate >= from;

    public string TechnicianNamesText => Technicians.Count == 0 ? "Unassigned" : string.Join(", ", Technicians.Select(t => t.FullName));

    /// <summary>Builds a summary, looking up assigned technician names; unknown ids are skipped.</summary>
    public static JobSummaryViewModel FromEntity(Job job, IReadOnlyDictionary<Guid, Technician> technicians) => new()
    {
        Id = job.Id,
        Name = job.Name,
        CustomerName = job.CustomerName,
        SiteName = job.SiteName,
        Address = job.Address,
        StartDate = job.StartDate,
        EndDate = job.EndDate,
        StartTime = job.StartTime,
        EndTime = job.EndTime,
        Status = job.Status,
        Technicians = job.AssignedTechnicianIds
            .Where(technicians.ContainsKey)
            .Select(id => new TechnicianOption(id, technicians[id].FullName))
            .ToList(),
        BuildingNames = job.Buildings.Select(b => b.Name).ToList(),
        IsRecurring = job.RecurringJobId is not null
    };
}

public class JobDetailsViewModel
{
    public JobSummaryViewModel Summary { get; init; } = new();
    public string? Notes { get; init; }
    public List<BuildingViewModel> Buildings { get; init; } = [];

    /// <summary>Empty form for adding a building from the job page.</summary>
    public BuildingFormModel NewBuilding { get; init; } = new();

    public static JobDetailsViewModel FromEntity(Job job, IReadOnlyDictionary<Guid, Technician> technicians) => new()
    {
        Summary = JobSummaryViewModel.FromEntity(job, technicians),
        Notes = job.Notes,
        Buildings = job.Buildings.Select(BuildingViewModel.FromEntity).ToList(),
        NewBuilding = new BuildingFormModel { JobId = job.Id, JobName = job.Name }
    };
}

public record BuildingViewModel(Guid Id, string Name, string? Notes, List<FileViewModel> Files)
{
    public static BuildingViewModel FromEntity(Building building) => new(
        building.Id,
        building.Name,
        building.Notes,
        building.UploadedFiles.OrderBy(f => f.OriginalFileName, StringComparer.OrdinalIgnoreCase).Select(FileViewModel.FromEntity).ToList());
}

public record FileViewModel(Guid Id, string OriginalFileName, long SizeBytes, DateTime UploadDateUtc)
{
    public string SizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:0.#} KB",
        _ => $"{SizeBytes / (1024.0 * 1024):0.#} MB"
    };

    public static FileViewModel FromEntity(UploadedFile file) => new(file.Id, file.OriginalFileName, file.SizeBytes, file.UploadDate);
}

public class BuildingFormModel
{
    public Guid JobId { get; set; }
    public Guid? BuildingId { get; set; }
    public string JobName { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Building name")]
    public string Name { get; set; } = "";

    [StringLength(2000), Display(Name = "Building notes (optional)")]
    public string? Notes { get; set; }
}

/// <summary>The team lead's page for one building: its details and file upload/management.</summary>
public class BuildingDetailsViewModel
{
    public Guid JobId { get; init; }
    public string JobName { get; init; } = "";
    public BuildingViewModel Building { get; init; } = new(Guid.Empty, "", null, []);
    public string AllowedExtensionsText { get; init; } = "";
    public long MaxFileSizeMegabytes { get; init; }
}
