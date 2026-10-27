// Purpose: View and form models for the user and role management screens.
using System.ComponentModel.DataAnnotations;
using FireAlarmScheduler.Services;

namespace FireAlarmScheduler.Models;

public record UserListItemViewModel(
    string Id, string DisplayName, string Email, IReadOnlyList<string> Roles, string? TechnicianName, bool IsLocked, bool CanManage);

public class UserFormModel
{
    public string? Id { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    /// <summary>Initial password; only used when creating an account.</summary>
    [StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string? Password { get; set; }

    public List<string> Roles { get; set; } = [];

    [Display(Name = "Linked technician")]
    public Guid? TechnicianId { get; set; }

    /// <summary>Roles the signed-in administrator may grant; filled by the controller, not posted back.</summary>
    public IReadOnlyList<string> AssignableRoles { get; set; } = [];

    /// <summary>Choices for the technician dropdown; filled by the controller, not posted back.</summary>
    public List<TechnicianOption> AvailableTechnicians { get; set; } = [];

    public UserInput ToInput() => new(Email, DisplayName, Roles, TechnicianId);
}

public class ResetPasswordFormModel
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public record RoleListItemViewModel(string Name, string Description, int MemberCount);

public record RoleDetailsViewModel(string Name, string Description, List<UserListItemViewModel> Members);
