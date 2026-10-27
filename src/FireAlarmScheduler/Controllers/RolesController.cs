// Purpose: Read-only role screens for Admins and SuperAdmins: the four fixed roles, what each allows,
// and who holds them. Role membership is changed from the user edit screen.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageUsers)]
public class RolesController : Controller
{
    private readonly UserAdministrationService _userAdmin;
    private readonly UserManager<ApplicationUser> _userManager;

    public RolesController(UserAdministrationService userAdmin, UserManager<ApplicationUser> userManager)
    {
        _userAdmin = userAdmin;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var model = new List<RoleListItemViewModel>();
        foreach (var role in AppRoles.All)
        {
            model.Add(new RoleListItemViewModel(role, AppRoles.Describe(role), (await _userAdmin.GetUsersInRoleAsync(role)).Count));
        }

        return View(model);
    }

    public async Task<IActionResult> Details(string id)
    {
        if (!AppRoles.All.Contains(id))
        {
            return NotFound();
        }

        var actorId = _userManager.GetUserId(User)!;
        var members = new List<UserListItemViewModel>();
        foreach (var user in await _userAdmin.GetUsersInRoleAsync(id))
        {
            var roles = (await _userAdmin.GetAsync(user.Id))?.Roles ?? [];
            members.Add(new UserListItemViewModel(user.Id, user.DisplayName, user.Email ?? "", roles, null,
                UserAdministrationService.IsLocked(user), await _userAdmin.CanManageAsync(actorId, user)));
        }

        return View(new RoleDetailsViewModel(id, AppRoles.Describe(id), members));
    }
}
