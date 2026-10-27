// Purpose: Small helpers shared by controllers: copying service validation errors into ModelState,
// building technician lookups for view-model mapping, and reading the signed-in user's role and technician link.
using System.Security.Claims;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FireAlarmScheduler.Controllers;

internal static class ControllerExtensions
{
    public static void AddErrors(this ModelStateDictionary modelState, ValidationFailedException ex)
    {
        foreach (var (field, message) in ex.Errors)
        {
            modelState.AddModelError(field, message);
        }
    }

    public static Dictionary<Guid, Technician> ToLookup(this IEnumerable<Technician> technicians) =>
        technicians.ToDictionary(t => t.Id);

    /// <summary>True for team leads and administrators, who may see every technician's schedule and files.</summary>
    public static bool CanManageSchedule(this ClaimsPrincipal user) => AppPolicies.ManageScheduleRoles.Any(user.IsInRole);

    /// <summary>The technician record linked to the signed-in account, or null if there is none.</summary>
    public static async Task<Guid?> GetLinkedTechnicianIdAsync(this UserManager<ApplicationUser> users, ClaimsPrincipal user) =>
        (await users.GetUserAsync(user))?.TechnicianId;
}
