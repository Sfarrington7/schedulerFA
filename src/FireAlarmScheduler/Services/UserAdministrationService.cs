// Purpose: Account management rules on top of ASP.NET Core Identity: create, edit, lock, reset, and delete
// users and assign the four fixed roles. Enforces who may manage whom and protects the last SuperAdmin.
using FireAlarmScheduler.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FireAlarmScheduler.Services;

/// <summary>The editable fields of an account. Roles are role names from <see cref="AppRoles"/>.</summary>
public record UserInput(string? Email, string? DisplayName, IReadOnlyCollection<string> Roles, Guid? TechnicianId);

public record UserWithRoles(ApplicationUser User, IReadOnlyList<string> Roles);

public class UserAdministrationService
{
    private const int MaxDisplayNameLength = 100;

    private readonly UserManager<ApplicationUser> _users;
    private readonly ITechnicianRepository _technicians;

    public UserAdministrationService(UserManager<ApplicationUser> users, ITechnicianRepository technicians)
    {
        _users = users;
        _technicians = technicians;
    }

    /// <summary>Returns every account with its roles, sorted by display name.</summary>
    public async Task<List<UserWithRoles>> GetAllAsync()
    {
        var users = await _users.Users.OrderBy(u => u.DisplayName).ThenBy(u => u.Email).ToListAsync();
        var result = new List<UserWithRoles>();
        foreach (var user in users)
        {
            result.Add(new UserWithRoles(user, await SortedRolesAsync(user)));
        }

        return result;
    }

    public async Task<UserWithRoles?> GetAsync(string id)
    {
        var user = await _users.FindByIdAsync(id);
        return user is null ? null : new UserWithRoles(user, await SortedRolesAsync(user));
    }

    /// <summary>Returns the accounts holding <paramref name="role"/>, sorted by display name.</summary>
    public async Task<List<ApplicationUser>> GetUsersInRoleAsync(string role) =>
        (await _users.GetUsersInRoleAsync(role)).OrderBy(u => u.DisplayName).ToList();

    public Task<bool> AnyUsersAsync() => _users.Users.AnyAsync();

    /// <summary>SuperAdmins may grant any role; Admins may grant only TeamLead and Technician.</summary>
    public async Task<IReadOnlyList<string>> GetAssignableRolesAsync(string actorId)
    {
        var actorRoles = await RolesOfAsync(actorId);
        if (actorRoles.Contains(AppRoles.SuperAdmin))
        {
            return AppRoles.All;
        }

        return actorRoles.Contains(AppRoles.Admin) ? [AppRoles.TeamLead, AppRoles.Technician] : [];
    }

    /// <summary>
    /// True when the actor may change the target account: SuperAdmins may change anyone; Admins may change
    /// only accounts that hold neither the Admin nor the SuperAdmin role.
    /// </summary>
    public async Task<bool> CanManageAsync(string actorId, ApplicationUser target)
    {
        var actorRoles = await RolesOfAsync(actorId);
        if (actorRoles.Contains(AppRoles.SuperAdmin))
        {
            return true;
        }

        var targetRoles = await _users.GetRolesAsync(target);
        return actorRoles.Contains(AppRoles.Admin)
            && !targetRoles.Contains(AppRoles.Admin) && !targetRoles.Contains(AppRoles.SuperAdmin);
    }

    /// <summary>
    /// Creates the very first account as a SuperAdmin. Only allowed while no accounts exist, so the
    /// unauthenticated setup page stops working as soon as it has been used.
    /// </summary>
    public async Task<ApplicationUser> CreateFirstSuperAdminAsync(string? email, string? displayName, string? password)
    {
        if (await AnyUsersAsync())
        {
            throw new ValidationFailedException("", "Setup has already been completed. Sign in instead.");
        }

        var user = NewUser(email, displayName, technicianId: null);
        await CreateWithRolesAsync(user, password, [AppRoles.SuperAdmin]);
        return user;
    }

    /// <summary>Creates an account with an initial password and roles. Throws ValidationFailedException on any rule break.</summary>
    public async Task<ApplicationUser> CreateAsync(string actorId, UserInput input, string? password)
    {
        var roles = await ValidateInputAsync(actorId, input, existingUserId: null);
        var user = NewUser(input.Email, input.DisplayName, input.TechnicianId);
        await CreateWithRolesAsync(user, password, roles);
        return user;
    }

    /// <summary>Updates an account's details and roles. Returns false if it does not exist.</summary>
    public async Task<bool> UpdateAsync(string actorId, string id, UserInput input)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        await EnsureCanManageAsync(actorId, user);
        var roles = await ValidateInputAsync(actorId, input, existingUserId: user.Id);

        var currentRoles = await _users.GetRolesAsync(user);
        if (currentRoles.Contains(AppRoles.SuperAdmin) && !roles.Contains(AppRoles.SuperAdmin))
        {
            await EnsureNotLastSuperAdminAsync(user, "remove the SuperAdmin role from");
        }

        var email = RequireEmail(input.Email);
        user.DisplayName = RequireDisplayName(input.DisplayName);
        user.TechnicianId = input.TechnicianId;
        ThrowIfFailed(await _users.SetEmailAsync(user, email));
        ThrowIfFailed(await _users.SetUserNameAsync(user, email));
        ThrowIfFailed(await _users.UpdateAsync(user));

        ThrowIfFailed(await _users.RemoveFromRolesAsync(user, currentRoles.Except(roles)));
        ThrowIfFailed(await _users.AddToRolesAsync(user, roles.Except(currentRoles)));

        // A new security stamp makes signed-in sessions pick up the role change at their next validation.
        ThrowIfFailed(await _users.UpdateSecurityStampAsync(user));
        return true;
    }

    /// <summary>Sets a new password chosen by the administrator. Returns false if the account does not exist.</summary>
    public async Task<bool> ResetPasswordAsync(string actorId, string id, string? newPassword)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        await EnsureCanManageAsync(actorId, user);
        var token = await _users.GeneratePasswordResetTokenAsync(user);
        ThrowIfFailed(await _users.ResetPasswordAsync(user, token, newPassword ?? ""));
        return true;
    }

    /// <summary>Locks (indefinitely) or unlocks an account. Returns false if it does not exist.</summary>
    public async Task<bool> SetLockedAsync(string actorId, string id, bool locked)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        await EnsureCanManageAsync(actorId, user);
        if (locked)
        {
            if (user.Id == actorId)
            {
                throw new ValidationFailedException("", "You can't lock your own account.");
            }

            await EnsureNotLastSuperAdminAsync(user, "lock");
            ThrowIfFailed(await _users.SetLockoutEnabledAsync(user, true));
            ThrowIfFailed(await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
            ThrowIfFailed(await _users.UpdateSecurityStampAsync(user));
        }
        else
        {
            ThrowIfFailed(await _users.SetLockoutEndDateAsync(user, null));
            ThrowIfFailed(await _users.ResetAccessFailedCountAsync(user));
        }

        return true;
    }

    /// <summary>Deletes an account. Returns false if it does not exist.</summary>
    public async Task<bool> DeleteAsync(string actorId, string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
        {
            return false;
        }

        if (user.Id == actorId)
        {
            throw new ValidationFailedException("", "You can't delete your own account.");
        }

        await EnsureCanManageAsync(actorId, user);
        await EnsureNotLastSuperAdminAsync(user, "delete");
        ThrowIfFailed(await _users.DeleteAsync(user));
        return true;
    }

    public static bool IsLocked(ApplicationUser user) => user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow;

    /// <summary>
    /// Checks the email, display name, roles, and technician link, and returns the distinct role list.
    /// Throws ValidationFailedException listing every problem found.
    /// </summary>
    private async Task<List<string>> ValidateInputAsync(string actorId, UserInput input, string? existingUserId)
    {
        var errors = new Dictionary<string, string>();
        var assignable = await GetAssignableRolesAsync(actorId);
        var roles = input.Roles.Distinct().ToList();

        if (string.IsNullOrWhiteSpace(input.Email))
        {
            errors["Email"] = "Email is required.";
        }

        if (string.IsNullOrWhiteSpace(input.DisplayName))
        {
            errors["DisplayName"] = "Display name is required.";
        }
        else if (input.DisplayName.Trim().Length > MaxDisplayNameLength)
        {
            errors["DisplayName"] = $"Display name must be {MaxDisplayNameLength} characters or fewer.";
        }

        if (roles.Count == 0)
        {
            errors["Roles"] = "Choose at least one role.";
        }
        else if (roles.Any(r => !assignable.Contains(r)))
        {
            errors["Roles"] = "You aren't allowed to grant one or more of the selected roles.";
        }

        if (input.TechnicianId is { } technicianId)
        {
            if (await _technicians.GetByIdAsync(technicianId) is null)
            {
                errors["TechnicianId"] = "The selected technician no longer exists.";
            }
            else if (await _users.Users.AnyAsync(u => u.TechnicianId == technicianId && u.Id != existingUserId))
            {
                errors["TechnicianId"] = "That technician is already linked to another account.";
            }
        }
        else if (roles.Contains(AppRoles.Technician))
        {
            errors["TechnicianId"] = "Link a technician so this account can see its schedule.";
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        return roles;
    }

    private async Task CreateWithRolesAsync(ApplicationUser user, string? password, IEnumerable<string> roles)
    {
        ThrowIfFailed(await _users.CreateAsync(user, password ?? ""));
        var addRoles = await _users.AddToRolesAsync(user, roles);
        if (!addRoles.Succeeded)
        {
            // Don't leave behind an account without its intended roles.
            await _users.DeleteAsync(user);
            ThrowIfFailed(addRoles);
        }
    }

    private async Task EnsureCanManageAsync(string actorId, ApplicationUser target)
    {
        if (!await CanManageAsync(actorId, target))
        {
            throw new ValidationFailedException("", "You aren't allowed to change this account.");
        }
    }

    private async Task EnsureNotLastSuperAdminAsync(ApplicationUser user, string action)
    {
        if (await _users.IsInRoleAsync(user, AppRoles.SuperAdmin)
            && (await _users.GetUsersInRoleAsync(AppRoles.SuperAdmin)).Count(u => !IsLocked(u)) <= 1)
        {
            throw new ValidationFailedException("", $"You can't {action} the last active SuperAdmin.");
        }
    }

    private async Task<IList<string>> RolesOfAsync(string actorId)
    {
        var actor = await _users.FindByIdAsync(actorId);
        return actor is null ? [] : await _users.GetRolesAsync(actor);
    }

    private async Task<IReadOnlyList<string>> SortedRolesAsync(ApplicationUser user)
    {
        var roles = await _users.GetRolesAsync(user);
        return AppRoles.All.Where(roles.Contains).ToList();
    }

    private static ApplicationUser NewUser(string? email, string? displayName, Guid? technicianId)
    {
        var address = RequireEmail(email);
        return new ApplicationUser
        {
            UserName = address,
            Email = address,
            DisplayName = RequireDisplayName(displayName),
            TechnicianId = technicianId,
            LockoutEnabled = true
        };
    }

    private static string RequireEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? throw new ValidationFailedException("Email", "Email is required.") : email.Trim();

    private static string RequireDisplayName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName)
            ? throw new ValidationFailedException("DisplayName", "Display name is required.")
            : displayName.Trim();

    /// <summary>Turns Identity errors into field-keyed validation errors (password, email, or general).</summary>
    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = new Dictionary<string, string>();
        foreach (var error in result.Errors)
        {
            var field = error.Code switch
            {
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => "Password",
                "DuplicateUserName" or "DuplicateEmail" or "InvalidEmail" or "InvalidUserName" => "Email",
                _ => ""
            };
            errors[field] = errors.TryGetValue(field, out var existing) ? $"{existing} {error.Description}" : error.Description;
        }

        throw new ValidationFailedException(errors);
    }
}
