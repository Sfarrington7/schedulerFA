// Purpose: A sign-in account (ASP.NET Core Identity user, stored in AspNetUsers).
// A user with the Technician role is linked to one Technician record to see that technician's schedule.
using Microsoft.AspNetCore.Identity;

namespace FireAlarmScheduler.Entities;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    /// <summary>The technician whose schedule this account sees; null for accounts that are not field technicians.</summary>
    public Guid? TechnicianId { get; set; }
}
