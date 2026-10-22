// Purpose: Hosts the real web app in memory for end-to-end tests, with JSON data and uploads
// redirected to a temporary folder and the clock fixed to Nov 3, 2026.
using FireAlarmScheduler.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly TempDirectory _dir = new();

    public string DataDirectory => _dir.Combine("data");
    public string UploadsDirectory => _dir.Combine("uploads");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Storage:DataDirectory", DataDirectory);
        builder.UseSetting("Storage:UploadsDirectory", UploadsDirectory);
        builder.UseSetting("Storage:MaxFileSizeBytes", "1024");
        builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(ServiceHarness.Now)));
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = true,
        HandleCookies = true
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _dir.Dispose();
        }
    }
}
