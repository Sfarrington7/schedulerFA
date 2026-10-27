// Purpose: View and form models for the technician (team management) pages.
using System.ComponentModel.DataAnnotations;

namespace FireAlarmScheduler.Models;

public class TechnicianFormModel
{
    public Guid? Id { get; set; }

    [Required, StringLength(100), Display(Name = "First name")]
    public string FirstName { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Last name")]
    public string LastName { get; set; } = "";
}

public record TechnicianListItemViewModel(Guid Id, string FirstName, string LastName, int UpcomingJobCount)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>A technician as shown in pickers and dropdowns.</summary>
public record TechnicianOption(Guid Id, string FullName);
