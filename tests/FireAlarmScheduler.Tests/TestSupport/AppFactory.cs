// Purpose: Hosts the real web app in memory for end-to-end tests, with the SQLite database and uploads
// redirected to a temporary folder and the clock fixed to Nov 3, 2026. Also creates accounts and
// returns HTTP clients already signed in through the real login form.
using System.Net;
using FireAlarmScheduler.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    /// <summary>Meets the default Identity password rules; used for every test account.</summary>
    public const string TestPassword = "Test-Passw0rd!";

    private readonly TempDirectory _dir = new();

    public string DatabasePath => _dir.Combine("scheduler.db");
    public string UploadsDirectory => _dir.Combine("uploads");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath};Pooling=False");
        builder.UseSetting("Storage:UploadsDirectory", UploadsDirectory);
        builder.UseSetting("Storage:MaxFileSizeBytes", "1024");
        builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(ServiceHarness.Now)));
    }

    public HttpClient CreateBrowser(bool allowAutoRedirect = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = allowAutoRedirect,
        HandleCookies = true
    });

    /// <summary>Creates an account with the given role (and optional technician link) directly in the database.</summary>
    public async Task<ApplicationUser> CreateUserAsync(string email, string role, Guid? technicianId = null)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, DisplayName = email, TechnicianId = technicianId };
        var created = await users.CreateAsync(user, TestPassword);
        Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    /// <summary>Signs in through the login form and returns the client holding the auth cookie.</summary>
    public async Task<HttpClient> SignInAsync(string email, bool allowAutoRedirect = true)
    {
        var client = CreateBrowser(allowAutoRedirect);
        var response = await FormPoster.PostFormAsync(client, "/Account/Login", "/Account/Login",
            ("Email", email), ("Password", TestPassword));
        var signedIn = allowAutoRedirect
            ? response.RequestMessage?.RequestUri?.AbsolutePath != "/Account/Login"
            : response.StatusCode == HttpStatusCode.Redirect;
        Assert.True(signedIn, $"Sign-in failed for {email}.");
        return client;
    }

    /// <summary>Creates an account with <paramref name="role"/> named after the role and signs it in.</summary>
    public async Task<HttpClient> SignInAsNewUserAsync(string role, bool allowAutoRedirect = true, Guid? technicianId = null)
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(email, role, technicianId);
        return await SignInAsync(email, allowAutoRedirect);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _dir.Dispose();
        }
    }
}
