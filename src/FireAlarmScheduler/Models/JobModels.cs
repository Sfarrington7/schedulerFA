// Purpose: View and form models for jobs, their buildings, and uploaded files.
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

    /// <summary>Choices for the technician checkboxes; filled by the controller, not posted back.</summary>
    public List<TechnicianOption> AvailableTechnicians { get; set; } = [];

    public JobInput ToInput() => new(Name, CustomerName, SiteName, Address, StartDate, EndDate,
        StartTime, EndTime, Status, Notes, AssignedTechnicianIds);

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
        BuildingNames = job.Buildings.Select(b => b.Name).ToList()
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
