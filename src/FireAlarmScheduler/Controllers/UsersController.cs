// Purpose: User management screens for Admins and SuperAdmins: list, create, edit (roles and technician
// link), reset password, lock/unlock, and delete accounts. Who may manage whom is decided by UserAdministrationService.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

[Authorize(Policy = AppPolicies.ManageUsers)]
public class UsersController : Controller
{
    private readonly UserAdministrationService _userAdmin;
    private readonly TechnicianService _technicianService;
    private readonly UserManager<ApplicationUser> _userManager;

    public UsersController(UserAdministrationService userAdmin, TechnicianService technicianService, UserManager<ApplicationUser> userManager)
    {
        _userAdmin = userAdmin;
        _technicianService = technicianService;
        _userManager = userManager;
    }

    private string ActorId => _userManager.GetUserId(User)!;

    public async Task<IActionResult> Index()
    {
        var lookup = (await _technicianService.GetAllAsync()).ToLookup();
        var model = new List<UserListItemViewModel>();
        foreach (var entry in await _userAdmin.GetAllAsync())
        {
            model.Add(await ToListItemAsync(entry, lookup));
        }

        return View(model);
    }

    public async Task<IActionResult> Create()
    {
        var form = new UserFormModel();
        await FillChoicesAsync(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserFormModel form)
    {
        if (string.IsNullOrEmpty(form.Password))
        {
            ModelState.AddModelError(nameof(form.Password), "Set an initial password.");
        }

        if (ModelState.IsValid)
        {
            try
            {
                var user = await _userAdmin.CreateAsync(ActorId, form.ToInput(), form.Password);
                TempData["Message"] = $"Created {user.DisplayName}.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        await FillChoicesAsync(form);
        return View(form);
    }

    public async Task<IActionResult> Edit(string id)
    {
        var entry = await _userAdmin.GetAsync(id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!await _userAdmin.CanManageAsync(ActorId, entry.User))
        {
            return Forbid();
        }

        var form = new UserFormModel
        {
            Id = entry.User.Id,
            Email = entry.User.Email ?? "",
            DisplayName = entry.User.DisplayName,
            Roles = [.. entry.Roles],
            TechnicianId = entry.User.TechnicianId
        };
        await FillChoicesAsync(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, UserFormModel form)
    {
        form.Id = id;
        ModelState.Remove(nameof(form.Password));
        if (ModelState.IsValid)
        {
            try
            {
                if (!await _userAdmin.UpdateAsync(ActorId, id, form.ToInput()))
                {
                    return NotFound();
                }

                TempData["Message"] = "Account updated.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        await FillChoicesAsync(form);
        return View(form);
    }

    public async Task<IActionResult> ResetPassword(string id)
    {
        var entry = await _userAdmin.GetAsync(id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!await _userAdmin.CanManageAsync(ActorId, entry.User))
        {
            return Forbid();
        }

        return View(new ResetPasswordFormModel { Id = id, DisplayName = entry.User.DisplayName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, ResetPasswordFormModel form)
    {
        form.Id = id;
        if (ModelState.IsValid)
        {
            try
            {
                if (!await _userAdmin.ResetPasswordAsync(ActorId, id, form.NewPassword))
                {
                    return NotFound();
                }

                TempData["Message"] = $"Password reset for {form.DisplayName}.";
                return RedirectToAction(nameof(Index));
            }
            catch (ValidationFailedException ex)
            {
                foreach (var (_, message) in ex.Errors)
                {
                    ModelState.AddModelError(nameof(form.NewPassword), message);
                }
            }
        }

        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Lock(string id) => RunListActionAsync(() => _userAdmin.SetLockedAsync(ActorId, id, true), "Account locked.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Unlock(string id) => RunListActionAsync(() => _userAdmin.SetLockedAsync(ActorId, id, false), "Account unlocked.");

    public async Task<IActionResult> Delete(string id)
    {
        var entry = await _userAdmin.GetAsync(id);
        if (entry is null)
        {
            return NotFound();
        }

        if (!await _userAdmin.CanManageAsync(ActorId, entry.User))
        {
            return Forbid();
        }

        return View(await ToListItemAsync(entry, (await _technicianService.GetAllAsync()).ToLookup()));
    }

    [HttpPost, ActionName(nameof(Delete)), ValidateAntiForgeryToken]
    public Task<IActionResult> DeleteConfirmed(string id) => RunListActionAsync(() => _userAdmin.DeleteAsync(ActorId, id), "Account deleted.");

    /// <summary>Runs a one-click action from the user list and reports its outcome back on the list.</summary>
    private async Task<IActionResult> RunListActionAsync(Func<Task<bool>> action, string successMessage)
    {
        try
        {
            if (!await action())
            {
                return NotFound();
            }

            TempData["Message"] = successMessage;
        }
        catch (ValidationFailedException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<UserListItemViewModel> ToListItemAsync(UserWithRoles entry, IReadOnlyDictionary<Guid, Technician> technicians) =>
        new(entry.User.Id,
            entry.User.DisplayName,
            entry.User.Email ?? "",
            entry.Roles,
            entry.User.TechnicianId is { } techId && technicians.TryGetValue(techId, out var tech) ? tech.FullName : null,
            UserAdministrationService.IsLocked(entry.User),
            await _userAdmin.CanManageAsync(ActorId, entry.User));

    private async Task FillChoicesAsync(UserFormModel form)
    {
        form.AssignableRoles = await _userAdmin.GetAssignableRolesAsync(ActorId);
        form.AvailableTechnicians = (await _technicianService.GetAllAsync())
            .Select(t => new TechnicianOption(t.Id, t.FullName))
            .ToList();
    }
}
