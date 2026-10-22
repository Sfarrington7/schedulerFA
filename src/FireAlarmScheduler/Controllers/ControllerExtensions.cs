// Purpose: Small helpers shared by controllers: copying service validation errors into ModelState
// and building technician lookups for view-model mapping.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
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
}
