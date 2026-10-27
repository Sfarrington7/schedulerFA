// Purpose: Tests the user and role management screens over HTTP: creating accounts with roles and a
// technician link, editing roles, locking, resetting passwords, deleting, and the Admin/SuperAdmin limits.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class UserManagementTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(ApplicationUser? User, IList<string> Roles)> FindAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByEmailAsync(email);
        return (user, user is null ? [] : await users.GetRolesAsync(user));
    }

    private async Task<Technician> AddTechnicianAsync(string first, string last)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TechnicianService>().AddAsync(first, last);
    }

    [Fact]
    public async Task CreateUser_TechnicianWithLink_CanSignInAndSeeOwnSchedule()
    {
        var john = await AddTechnicianAsync("John", "Smith");
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var response = await FormPoster.PostFormAsync(admin, "/Users/Create", "/Users/Create",
            ("Email", "john@example.test"), ("DisplayName", "John Smith"), ("Password", AppFactory.TestPassword),
            ("Roles", AppRoles.Technician), ("TechnicianId", john.Id.ToString()));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Created John Smith.", html);
        var (user, roles) = await FindAsync("john@example.test");
        Assert.Equal(john.Id, user!.TechnicianId);
        Assert.Equal([AppRoles.Technician], roles);
        var johnClient = await _factory.SignInAsync("john@example.test");
        Assert.Contains("No jobs assigned to John Smith", await johnClient.GetStringAsync("/TechnicianView"));
    }

    [Fact]
    public async Task CreateUser_TechnicianRoleWithoutLink_ShowsErrorAndCreatesNothing()
    {
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var response = await FormPoster.PostFormAsync(admin, "/Users/Create", "/Users/Create",
            ("Email", "tech@example.test"), ("DisplayName", "Tech"), ("Password", AppFactory.TestPassword),
            ("Roles", AppRoles.Technician));

        Assert.Contains("Link a technician so this account can see its schedule.", await response.Content.ReadAsStringAsync());
        Assert.Null((await FindAsync("tech@example.test")).User);
    }

    [Fact]
    public async Task CreateUser_AdminPostingSuperAdminRole_IsRejected()
    {
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var form = await admin.GetStringAsync("/Users/Create");
        var response = await FormPoster.PostFormAsync(admin, "/Users/Create", "/Users/Create",
            ("Email", "boss@example.test"), ("DisplayName", "Boss"), ("Password", AppFactory.TestPassword),
            ("Roles", AppRoles.SuperAdmin));

        Assert.DoesNotContain($"value=\"{AppRoles.SuperAdmin}\"", form);
        Assert.Contains("You aren&#x27;t allowed to grant", await response.Content.ReadAsStringAsync());
        Assert.Null((await FindAsync("boss@example.test")).User);
    }

    [Fact]
    public async Task CreateUser_WeakPassword_ShowsIdentityError()
    {
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.SuperAdmin);

        var response = await FormPoster.PostFormAsync(admin, "/Users/Create", "/Users/Create",
            ("Email", "lead@example.test"), ("DisplayName", "Lead"), ("Password", "alllowercase"), ("Roles", AppRoles.TeamLead));

        Assert.Contains("Passwords must have at least one", await response.Content.ReadAsStringAsync());
        Assert.Null((await FindAsync("lead@example.test")).User);
    }

    [Fact]
    public async Task EditUser_SuperAdminPromotesTeamLeadToAdmin_NewRoleTakesEffect()
    {
        var lead = await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var superAdmin = await _factory.SignInAsNewUserAsync(AppRoles.SuperAdmin);

        var response = await FormPoster.PostFormAsync(superAdmin, $"/Users/Edit/{lead.Id}", $"/Users/Edit/{lead.Id}",
            ("Email", "lead@example.test"), ("DisplayName", "Lead"), ("Roles", AppRoles.TeamLead), ("Roles", AppRoles.Admin));

        Assert.Contains("Account updated.", await response.Content.ReadAsStringAsync());
        Assert.Equal([AppRoles.Admin, AppRoles.TeamLead], (await FindAsync("lead@example.test")).Roles.Order());
        var leadClient = await _factory.SignInAsync("lead@example.test", allowAutoRedirect: false);
        Assert.Equal(HttpStatusCode.OK, (await leadClient.GetAsync("/Users")).StatusCode);
    }

    [Fact]
    public async Task LockUser_LockedAccountCannotSignIn_UnlockRestoresAccess()
    {
        var lead = await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var locked = await FormPoster.PostFormAsync(admin, "/Users", $"/Users/Lock/{lead.Id}");
        var login = await FormPoster.PostFormAsync(_factory.CreateBrowser(), "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", AppFactory.TestPassword));
        await FormPoster.PostFormAsync(admin, "/Users", $"/Users/Unlock/{lead.Id}");

        Assert.Contains("Account locked.", await locked.Content.ReadAsStringAsync());
        Assert.Contains("This account is locked.", await login.Content.ReadAsStringAsync());
        await _factory.SignInAsync("lead@example.test");
    }

    [Fact]
    public async Task ResetPassword_NewPasswordWorksForSignIn()
    {
        var lead = await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);
        const string newPassword = "Reset-Passw0rd!";

        var response = await FormPoster.PostFormAsync(admin, $"/Users/ResetPassword/{lead.Id}", $"/Users/ResetPassword/{lead.Id}",
            ("DisplayName", "lead@example.test"), ("NewPassword", newPassword), ("ConfirmPassword", newPassword));

        Assert.Contains("Password reset", await response.Content.ReadAsStringAsync());
        var login = await FormPoster.PostFormAsync(_factory.CreateBrowser(allowAutoRedirect: false), "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", newPassword));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_RemovesAccountButKeepsTechnician()
    {
        var john = await AddTechnicianAsync("John", "Smith");
        var user = await _factory.CreateUserAsync("john@example.test", AppRoles.Technician, john.Id);
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var response = await FormPoster.PostFormAsync(admin, $"/Users/Delete/{user.Id}", $"/Users/Delete/{user.Id}");

        Assert.Contains("Account deleted.", await response.Content.ReadAsStringAsync());
        Assert.Null((await FindAsync("john@example.test")).User);
        Assert.NotNull(await _factory.Services.GetRequiredService<ITechnicianRepository>().GetByIdAsync(john.Id));
    }

    [Fact]
    public async Task DeleteUser_AdminTargetingAdmin_IsRefused()
    {
        var otherAdmin = await _factory.CreateUserAsync("admin2@example.test", AppRoles.Admin);
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var token = await FormPoster.GetAntiforgeryTokenAsync(admin, "/Users");
        var response = await admin.PostAsync($"/Users/Delete/{otherAdmin.Id}",
            new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Contains("You aren&#x27;t allowed to change this account.", await response.Content.ReadAsStringAsync());
        Assert.NotNull((await FindAsync("admin2@example.test")).User);
    }

    [Fact]
    public async Task Roles_ListsFourRolesWithMemberCounts()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var superAdmin = await _factory.SignInAsNewUserAsync(AppRoles.SuperAdmin);

        var index = await superAdmin.GetStringAsync("/Roles");
        var details = await superAdmin.GetStringAsync("/Roles/Details/TeamLead");

        foreach (var role in AppRoles.All)
        {
            Assert.Contains($"<strong>{role}</strong>", index);
        }

        Assert.Contains("lead@example.test", details);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.GetAsync("/Roles/Details/Janitor")).StatusCode);
    }
}
