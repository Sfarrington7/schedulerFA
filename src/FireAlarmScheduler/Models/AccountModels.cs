// Purpose: Form models for signing in, first-run setup, and changing your own password.
using System.ComponentModel.DataAnnotations;

namespace FireAlarmScheduler.Models;

public class LoginFormModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    /// <summary>True when no accounts exist yet, so the page links to first-run setup.</summary>
    public bool SetupAvailable { get; set; }
}

public class SetupFormModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Display name")]
    public string DisplayName { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password)), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public class ChangePasswordFormModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}
