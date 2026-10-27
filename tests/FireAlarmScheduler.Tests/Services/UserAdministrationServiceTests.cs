// Purpose: Tests the account management rules against real Identity stores on a throwaway SQL Server database:
// who may grant which roles, who may change whom, technician links, and last-SuperAdmin protection.
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Services;

public class UserAdministrationServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly ServiceHarness _harness;

    public UserAdministrationServiceTests()
    {
        _harness = new ServiceHarness(_dir.Path);
    }

    public void Dispose() => _dir.Dispose();

    private UserAdministrationService Admin => _harness.UserAdmin;

    private static UserInput Input(string email, params string[] roles) => new(email, email, roles, null);

    [Fact]
    public async Task GetAssignableRolesAsync_DependsOnActorRole()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var admin = await _harness.CreateUserAsync("admin@example.test", [AppRoles.Admin]);
        var lead = await _harness.CreateUserAsync("lead@example.test", [AppRoles.TeamLead]);

        Assert.Equal(AppRoles.All, await Admin.GetAssignableRolesAsync(superAdmin.Id));
        Assert.Equal([AppRoles.TeamLead, AppRoles.Technician], await Admin.GetAssignableRolesAsync(admin.Id));
        Assert.Empty(await Admin.GetAssignableRolesAsync(lead.Id));
    }

    [Fact]
    public async Task CreateAsync_ValidInput_CreatesAccountWithRolesAndTrimmedFields()
    {
        var admin = await _harness.CreateUserAsync("admin@example.test", [AppRoles.Admin]);

        var user = await Admin.CreateAsync(admin.Id, new UserInput(" lead@example.test ", " Lead ", [AppRoles.TeamLead], null),
            AppFactory.TestPassword);

        var stored = await Admin.GetAsync(user.Id);
        Assert.Equal("lead@example.test", stored!.User.Email);
        Assert.Equal("Lead", stored.User.DisplayName);
        Assert.Equal([AppRoles.TeamLead], stored.Roles);
        Assert.True(await _harness.UserManager.CheckPasswordAsync(stored.User, AppFactory.TestPassword));
    }

    [Fact]
    public async Task CreateAsync_AdminGrantingAdmin_ThrowsAndCreatesNothing()
    {
        var admin = await _harness.CreateUserAsync("admin@example.test", [AppRoles.Admin]);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(admin.Id, Input("other@example.test", AppRoles.Admin), AppFactory.TestPassword));

        Assert.True(ex.Errors.ContainsKey("Roles"));
        Assert.Null(await _harness.UserManager.FindByEmailAsync("other@example.test"));
    }

    [Fact]
    public async Task CreateAsync_InvalidInput_ReportsEveryProblem()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(superAdmin.Id, new UserInput(" ", "", [], null), AppFactory.TestPassword));

        Assert.Equal(["DisplayName", "Email", "Roles"], ex.Errors.Keys.Order());
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsEmailError()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);

        var ex = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(superAdmin.Id, Input("owner@example.test", AppRoles.TeamLead), AppFactory.TestPassword));

        Assert.True(ex.Errors.ContainsKey("Email"));
    }

    [Fact]
    public async Task CreateAsync_TechnicianLinkRules_Enforced()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var john = await _harness.TechnicianService.AddAsync("John", "Smith");
        await Admin.CreateAsync(superAdmin.Id, Input("john@example.test", AppRoles.Technician) with { TechnicianId = john.Id },
            AppFactory.TestPassword);

        var missingLink = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(superAdmin.Id, Input("a@example.test", AppRoles.Technician), AppFactory.TestPassword));
        var alreadyLinked = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(superAdmin.Id, Input("b@example.test", AppRoles.Technician) with { TechnicianId = john.Id },
                AppFactory.TestPassword));
        var unknown = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateAsync(superAdmin.Id, Input("c@example.test", AppRoles.Technician) with { TechnicianId = Guid.NewGuid() },
                AppFactory.TestPassword));

        Assert.Contains("Link a technician", missingLink.Errors["TechnicianId"]);
        Assert.Contains("already linked", alreadyLinked.Errors["TechnicianId"]);
        Assert.Contains("no longer exists", unknown.Errors["TechnicianId"]);
    }

    [Fact]
    public async Task CanManageAsync_AdminCannotManageAdminsOrSuperAdmins()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var admin = await _harness.CreateUserAsync("admin@example.test", [AppRoles.Admin]);
        var otherAdmin = await _harness.CreateUserAsync("admin2@example.test", [AppRoles.Admin, AppRoles.TeamLead]);
        var lead = await _harness.CreateUserAsync("lead@example.test", [AppRoles.TeamLead]);

        Assert.True(await Admin.CanManageAsync(admin.Id, lead));
        Assert.False(await Admin.CanManageAsync(admin.Id, otherAdmin));
        Assert.False(await Admin.CanManageAsync(admin.Id, superAdmin));
        Assert.True(await Admin.CanManageAsync(superAdmin.Id, otherAdmin));
        Assert.False(await Admin.CanManageAsync(lead.Id, lead));
    }

    [Fact]
    public async Task UpdateAsync_ChangesRolesAndRotatesSecurityStamp()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var lead = await _harness.CreateUserAsync("lead@example.test", [AppRoles.TeamLead]);
        var stampBefore = lead.SecurityStamp;

        Assert.True(await Admin.UpdateAsync(superAdmin.Id, lead.Id, Input("lead@example.test", AppRoles.Admin)));

        var stored = await Admin.GetAsync(lead.Id);
        Assert.Equal([AppRoles.Admin], stored!.Roles);
        Assert.NotEqual(stampBefore, stored.User.SecurityStamp);
    }

    [Fact]
    public async Task UpdateAsync_RemovingLastSuperAdminRole_Throws()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.UpdateAsync(superAdmin.Id, superAdmin.Id, Input("owner@example.test", AppRoles.Admin)));

        Assert.Equal([AppRoles.SuperAdmin], (await Admin.GetAsync(superAdmin.Id))!.Roles);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsFalse()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);

        Assert.False(await Admin.UpdateAsync(superAdmin.Id, "missing", Input("x@example.test", AppRoles.TeamLead)));
    }

    [Fact]
    public async Task DeleteAsync_SelfOrLastSuperAdmin_Throws()
    {
        var superAdmin = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var second = await _harness.CreateUserAsync("owner2@example.test", [AppRoles.SuperAdmin]);

        await Assert.ThrowsAsync<ValidationFailedException>(() => Admin.DeleteAsync(superAdmin.Id, superAdmin.Id));
        Assert.True(await Admin.DeleteAsync(superAdmin.Id, second.Id));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Admin.DeleteAsync(superAdmin.Id, superAdmin.Id));
        Assert.NotNull(await Admin.GetAsync(superAdmin.Id));
    }

    [Fact]
    public async Task SetLockedAsync_LastActiveSuperAdmin_CannotBeLocked()
    {
        var first = await _harness.CreateUserAsync("owner@example.test", [AppRoles.SuperAdmin]);
        var second = await _harness.CreateUserAsync("owner2@example.test", [AppRoles.SuperAdmin]);

        Assert.True(await Admin.SetLockedAsync(first.Id, second.Id, locked: true));
        await Assert.ThrowsAsync<ValidationFailedException>(() => Admin.SetLockedAsync(second.Id, first.Id, locked: true));
        Assert.True(UserAdministrationService.IsLocked((await Admin.GetAsync(second.Id))!.User));
        Assert.False(UserAdministrationService.IsLocked((await Admin.GetAsync(first.Id))!.User));
    }

    [Fact]
    public async Task CreateFirstSuperAdminAsync_OnlyWhileNoAccountsExist()
    {
        var owner = await Admin.CreateFirstSuperAdminAsync("owner@example.test", "Owner", AppFactory.TestPassword);

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            Admin.CreateFirstSuperAdminAsync("second@example.test", "Second", AppFactory.TestPassword));

        Assert.Equal([AppRoles.SuperAdmin], (await Admin.GetAsync(owner.Id))!.Roles);
        Assert.Null(await _harness.UserManager.FindByEmailAsync("second@example.test"));
    }
}
