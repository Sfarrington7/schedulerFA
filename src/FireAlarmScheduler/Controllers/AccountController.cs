// Purpose: Sign-in, sign-out, access-denied, change-password, and first-run setup pages.
// Setup creates the first SuperAdmin and only works while no accounts exist.
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FireAlarmScheduler.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly UserAdministrationService _userAdmin;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
        UserAdministrationService userAdmin)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _userAdmin = userAdmin;
    }

    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        if (!await _userAdmin.AnyUsersAsync())
        {
            return RedirectToAction(nameof(Setup));
        }

        return View(new LoginFormModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginFormModel form)
    {
        if (ModelState.IsValid)
        {
            var result = await _signInManager.PasswordSignInAsync(form.Email, form.Password, form.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return LocalRedirect(Url.IsLocalUrl(form.ReturnUrl) ? form.ReturnUrl : "/");
            }

            ModelState.AddModelError("", result.IsLockedOut
                ? "This account is locked. Try again later or ask an administrator."
                : "Incorrect email or password.");
        }

        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    public IActionResult ChangePassword() => View(new ChangePasswordFormModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordFormModel form)
    {
        if (!ModelState.IsValid)
        {
            return View(form);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var result = await _userManager.ChangePasswordAsync(user, form.CurrentPassword, form.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code == "PasswordMismatch" ? nameof(form.CurrentPassword) : nameof(form.NewPassword),
                    error.Description);
            }

            return View(form);
        }

        // Re-issue the cookie: changing the password rotates the security stamp.
        await _signInManager.RefreshSignInAsync(user);
        TempData["Message"] = "Password changed.";
        return LocalRedirect("/");
    }

    [AllowAnonymous]
    public async Task<IActionResult> Setup()
    {
        if (await _userAdmin.AnyUsersAsync())
        {
            return RedirectToAction(nameof(Login));
        }

        return View(new SetupFormModel());
    }

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Setup(SetupFormModel form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var user = await _userAdmin.CreateFirstSuperAdminAsync(form.Email, form.DisplayName, form.Password);
                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData["Message"] = "Setup complete. You are signed in as the SuperAdmin.";
                return RedirectToAction("Index", "Users");
            }
            catch (ValidationFailedException ex)
            {
                ModelState.AddErrors(ex);
            }
        }

        return View(form);
    }
}
