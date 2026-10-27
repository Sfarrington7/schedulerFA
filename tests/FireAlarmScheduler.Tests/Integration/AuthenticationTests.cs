// Purpose: Tests sign-in over HTTP: anonymous access redirects to login, first-run SuperAdmin setup
// (and that it can't be reused), wrong passwords, lockout, sign-out, and changing your own password.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class AuthenticationTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/")]
    [InlineData("/Jobs")]
    [InlineData("/TechnicianView")]
    [InlineData("/Users")]
    public async Task Get_Anonymous_RedirectsToLogin(string url)
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);

        var response = await _factory.CreateBrowser(allowAutoRedirect: false).GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/css/site.css")]
    [InlineData("/lib/bootstrap/dist/css/bootstrap.min.css")]
    public async Task Get_StaticAssetAnonymously_IsServed(string url)
    {
        var response = await _factory.CreateBrowser(allowAutoRedirect: false).GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Login_NoAccountsYet_RedirectsToSetup()
    {
        var response = await _factory.CreateBrowser().GetAsync("/Account/Login");

        Assert.Equal("/Account/Setup", response.RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Setup_FirstRun_CreatesSignedInSuperAdmin()
    {
        var client = _factory.CreateBrowser();

        var response = await FormPoster.PostFormAsync(client, "/Account/Setup", "/Account/Setup",
            ("Email", "owner@example.test"), ("DisplayName", "Owner"),
            ("Password", AppFactory.TestPassword), ("ConfirmPassword", AppFactory.TestPassword));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal("/Users", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains("Setup complete", html);
        Assert.Contains("SuperAdmin", html);
    }

    [Fact]
    public async Task Setup_AfterAnAccountExists_IsClosed()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var client = _factory.CreateBrowser();

        var page = await client.GetAsync("/Account/Setup");
        var token = await FormPoster.GetAntiforgeryTokenAsync(client, "/Account/Login");
        var post = await client.PostAsync("/Account/Setup", new FormUrlEncodedContent([
            new("Email", "intruder@example.test"), new("DisplayName", "Intruder"),
            new("Password", AppFactory.TestPassword), new("ConfirmPassword", AppFactory.TestPassword),
            new("__RequestVerificationToken", token)]));

        Assert.Equal("/Account/Login", page.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Contains("Setup has already been completed", await post.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync("intruder@example.test"));
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsErrorAndStaysSignedOut()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var client = _factory.CreateBrowser();

        var response = await FormPoster.PostFormAsync(client, "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", "wrong"));

        Assert.Contains("Incorrect email or password.", await response.Content.ReadAsStringAsync());
        Assert.Equal("/Account/Login", (await client.GetAsync("/Jobs")).RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task Login_FiveWrongPasswords_LocksAccountEvenForCorrectPassword()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var client = _factory.CreateBrowser();
        for (var i = 0; i < 5; i++)
        {
            await FormPoster.PostFormAsync(client, "/Account/Login", "/Account/Login", ("Email", "lead@example.test"), ("Password", "wrong"));
        }

        var response = await FormPoster.PostFormAsync(client, "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", AppFactory.TestPassword));

        Assert.Contains("This account is locked.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_ExternalReturnUrl_IsIgnored()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var client = _factory.CreateBrowser(allowAutoRedirect: false);

        var response = await FormPoster.PostFormAsync(client, "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", AppFactory.TestPassword), ("ReturnUrl", "https://evil.example/"));

        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Logout_EndsSession()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        await FormPoster.PostFormAsync(client, "/Jobs", "/Account/Logout");

        Assert.Equal("/Account/Login", (await client.GetAsync("/Jobs")).RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ChangePassword_CorrectCurrentPassword_NewPasswordWorks()
    {
        await _factory.CreateUserAsync("lead@example.test", AppRoles.TeamLead);
        var client = await _factory.SignInAsync("lead@example.test");
        const string newPassword = "Brand-New-Passw0rd";

        var wrong = await FormPoster.PostFormAsync(client, "/Account/ChangePassword", "/Account/ChangePassword",
            ("CurrentPassword", "wrong"), ("NewPassword", newPassword), ("ConfirmPassword", newPassword));
        var right = await FormPoster.PostFormAsync(client, "/Account/ChangePassword", "/Account/ChangePassword",
            ("CurrentPassword", AppFactory.TestPassword), ("NewPassword", newPassword), ("ConfirmPassword", newPassword));

        Assert.Contains("Incorrect password.", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("Password changed.", await right.Content.ReadAsStringAsync());
        var fresh = _factory.CreateBrowser(allowAutoRedirect: false);
        var login = await FormPoster.PostFormAsync(fresh, "/Account/Login", "/Account/Login",
            ("Email", "lead@example.test"), ("Password", newPassword));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }
}
