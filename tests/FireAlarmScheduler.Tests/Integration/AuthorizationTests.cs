// Purpose: Tests which pages each of the four roles can open, and that technician accounts are limited
// to their own linked schedule and the files of jobs they are assigned to.
using System.Net;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class AuthorizationTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(AppRoles.Technician, "/TechnicianView", true)]
    [InlineData(AppRoles.Technician, "/Jobs", false)]
    [InlineData(AppRoles.Technician, "/Jobs/Create", false)]
    [InlineData(AppRoles.Technician, "/Technicians", false)]
    [InlineData(AppRoles.Technician, "/Schedule", false)]
    [InlineData(AppRoles.Technician, "/Users", false)]
    [InlineData(AppRoles.Technician, "/Roles", false)]
    [InlineData(AppRoles.TeamLead, "/", true)]
    [InlineData(AppRoles.TeamLead, "/Jobs", true)]
    [InlineData(AppRoles.TeamLead, "/Technicians", true)]
    [InlineData(AppRoles.TeamLead, "/TechnicianView", true)]
    [InlineData(AppRoles.TeamLead, "/Users", false)]
    [InlineData(AppRoles.TeamLead, "/Roles", false)]
    [InlineData(AppRoles.Admin, "/Jobs", true)]
    [InlineData(AppRoles.Admin, "/Users", true)]
    [InlineData(AppRoles.Admin, "/Users/Create", true)]
    [InlineData(AppRoles.Admin, "/Roles", true)]
    [InlineData(AppRoles.SuperAdmin, "/Jobs", true)]
    [InlineData(AppRoles.SuperAdmin, "/Users", true)]
    [InlineData(AppRoles.SuperAdmin, "/Roles/Details/SuperAdmin", true)]
    public async Task Get_PageForRole_AllowedOrAccessDenied(string role, string url, bool allowed)
    {
        var client = await _factory.SignInAsNewUserAsync(role, allowAutoRedirect: false);

        var response = await client.GetAsync(url);

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
        }
    }

    [Fact]
    public async Task Dashboard_Technician_RedirectsToOwnSchedule()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician);

        var response = await client.GetAsync("/");

        Assert.Equal("/TechnicianView", response.RequestMessage?.RequestUri?.AbsolutePath);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("My Schedule", html);
        Assert.DoesNotContain(">Jobs<", html);
    }

    [Fact]
    public async Task TechnicianView_TechnicianWithoutLink_ShowsLinkMessageAndNoPicker()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TechnicianService>().AddAsync("John", "Smith");
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician);

        var html = await client.GetStringAsync("/TechnicianView");

        Assert.Contains("isn't linked to a technician", html);
        Assert.DoesNotContain("John Smith", html);
    }

    [Fact]
    public async Task TechnicianView_TechnicianAccount_IgnoresOtherTechnicianIdInUrl()
    {
        using var scope = _factory.Services.CreateScope();
        var technicians = scope.ServiceProvider.GetRequiredService<TechnicianService>();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var john = await technicians.AddAsync("John", "Smith");
        var mike = await technicians.AddAsync("Mike", "Johnson");
        await jobs.CreateAsync(ServiceHarness.ValidJob(mike.Id) with { Name = "Mike only job" });
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var html = await client.GetStringAsync($"/TechnicianView?technicianId={mike.Id}&date=2026-11-03");

        Assert.Contains("No jobs assigned to John Smith", html);
        Assert.DoesNotContain("Mike only job", html);
    }

    [Fact]
    public async Task EditUser_AdminOpeningSuperAdmin_IsDenied()
    {
        var superAdmin = await _factory.CreateUserAsync("owner@example.test", AppRoles.SuperAdmin);
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin, allowAutoRedirect: false);

        var response = await admin.GetAsync($"/Users/Edit/{superAdmin.Id}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
    }
}
