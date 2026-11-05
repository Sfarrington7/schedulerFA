// Purpose: Fixture for the browser layout tests. Runs the real app on a local port (Kestrel) over a throwaway SQL
// Server database seeded with a realistic November 2026 schedule, launches headless Chromium through Playwright, and
// signs in once as a SuperAdmin and once as a technician so each test can open pages already signed in.
using System.Net;
using System.Net.Sockets;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class VisualTestApp : IAsyncLifetime
{
    public const string LeadEmail = "lead@example.test";
    public const string TechnicianEmail = "john@example.test";

    private readonly AppFactory _factory = new();
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Signed-in cookies for each account, keyed by role name ("" means signed out).</summary>
    public Dictionary<string, string?> SignedIn { get; } = new() { [""] = null };

    /// <summary>Values for the {placeholders} in page paths, such as {jobId}, filled once the data is seeded.</summary>
    public Dictionary<string, string> Ids { get; } = [];

    public async Task InitializeAsync()
    {
        var port = FreePort();
        _factory.UseKestrel(port);
        _factory.StartServer();
        BaseUrl = $"http://localhost:{port}";

        await SeedAsync();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
        SignedIn[AppRoles.SuperAdmin] = await SignInAsync(LeadEmail);
        SignedIn[AppRoles.Technician] = await SignInAsync(TechnicianEmail);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        _factory.Dispose();
    }

    /// <summary>Replaces {placeholders} in a page path with the seeded record IDs.</summary>
    public string Url(string path) =>
        BaseUrl + Ids.Aggregate(path, (current, id) => current.Replace("{" + id.Key + "}", id.Value));

    /// <summary>
    /// Four technicians; a recurring five-day job with a Google Drive files link and three buildings (Building A checked
    /// off by John); overlapping jobs that
    /// double-book John; a job during Sara's approved PTO; one pending PTO request; and a job running into December.
    /// </summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var technicians = services.GetRequiredService<TechnicianService>();
        var jobs = services.GetRequiredService<JobService>();
        var pto = services.GetRequiredService<PtoService>();

        var john = await technicians.AddAsync("John", "Smith");
        var mike = await technicians.AddAsync("Mike", "Johnson");
        var sara = await technicians.AddAsync("Sara", "Lee");
        var dave = await technicians.AddAsync("Dave", "Brown");

        var plaza = await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id) with
        {
            SaveAsRecurring = true, FilesUrl = "https://drive.google.com/drive/folders/1AbCdEfGhIjKlMnOpQrStUvWxYz0123456?usp=sharing"
        });
        var buildingA = await jobs.AddBuildingAsync(plaza.Id, "Building A", "Main lobby panel");
        var buildingB = await jobs.AddBuildingAsync(plaza.Id, "Building B", "Roof access via east stairwell");
        await jobs.AddBuildingAsync(plaza.Id, "Parking Garage", null);
        await jobs.SetBuildingCompleteAsync(plaza.Id, buildingA!.Id, john.Id, complete: true);

        await jobs.CreateAsync(ServiceHarness.ValidJob(sara.Id) with
        {
            Name = "Riverside Hospital Quarterly", StartDate = new DateOnly(2026, 11, 10), EndDate = new DateOnly(2026, 11, 12)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with
        {
            Name = "Harbor Warehouse Sprinkler Tie-in", StartDate = new DateOnly(2026, 11, 18), EndDate = new DateOnly(2026, 11, 20)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id, dave.Id) with
        {
            Name = "Lincoln Elementary Alarm Test", StartDate = new DateOnly(2026, 11, 19), EndDate = new DateOnly(2026, 11, 19)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(sara.Id, dave.Id) with
        {
            Name = "Oak Street Apartments", StartDate = new DateOnly(2026, 11, 24), EndDate = new DateOnly(2026, 11, 25)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(sara.Id, john.Id) with
        {
            Name = "City Hall Annual", StartDate = new DateOnly(2026, 11, 30), EndDate = new DateOnly(2026, 12, 2)
        });

        var approved = await pto.SubmitAsync(sara.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 11), "Appointment"));
        await pto.ReviewAsync(approved.Id, PtoStatus.Approved, LeadEmail);
        await pto.SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 27), "Family event"));

        await _factory.CreateUserAsync(LeadEmail, AppRoles.SuperAdmin);
        await _factory.CreateUserAsync(TechnicianEmail, AppRoles.Technician, john.Id);

        Ids["jobId"] = plaza.Id.ToString();
        Ids["buildingId"] = buildingA.Id.ToString();
        Ids["buildingBId"] = buildingB!.Id.ToString();
        Ids["recurringJobId"] = plaza.RecurringJobId!.Value.ToString();
        Ids["technicianId"] = john.Id.ToString();
    }

    /// <summary>Signs in through the real login form and returns the browser's cookies for reuse.</summary>
    private async Task<string> SignInAsync(string email)
    {
        await using var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(BaseUrl + "/Account/Login");
        await page.FillAsync("#Email", email);
        await page.FillAsync("#Password", AppFactory.TestPassword);
        await page.ClickAsync("main button[type=submit]");
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login"));
        return await context.StorageStateAsync();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
