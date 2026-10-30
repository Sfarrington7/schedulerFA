// Purpose: Tests the job form's technician picker over HTTP: the availability endpoint (booked, on PTO, free, the job
// being edited, invalid times, technicians denied), conflict marks and the warning on the Edit form, and that a
// conflicting technician can still be assigned.
using System.Net;
using System.Net.Http.Json;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Models;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class TechnicianPickerTests : IDisposable
{
    private const string Nov10Morning = "startDate=2026-11-10&endDate=2026-11-10&startTime=09:00&endTime=12:00";
    private readonly AppFactory _factory = new();
    private Technician _john = null!;
    private Technician _mike = null!;
    private Technician _sara = null!;
    private Job _smithTower = null!;

    public void Dispose() => _factory.Dispose();

    /// <summary>John is booked on Smith Tower Nov 10, 8–4; Mike has approved PTO all day Nov 10; Sara is free.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var technicians = scope.ServiceProvider.GetRequiredService<TechnicianService>();
        var pto = scope.ServiceProvider.GetRequiredService<PtoService>();
        _john = await technicians.AddAsync("John", "Smith");
        _mike = await technicians.AddAsync("Mike", "Jones");
        _sara = await technicians.AddAsync("Sara", "Lee");
        _smithTower = await scope.ServiceProvider.GetRequiredService<JobService>().CreateAsync(ServiceHarness.ValidJob(_john.Id) with
        {
            Name = "Smith Tower", StartDate = new DateOnly(2026, 11, 10), EndDate = new DateOnly(2026, 11, 10)
        });
        var request = await pto.SubmitAsync(_mike.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10)));
        await pto.ReviewAsync(request.Id, PtoStatus.Approved, "lead@example.test");
    }

    private static async Task<Dictionary<Guid, List<string>>> GetAvailabilityAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/Jobs/TechnicianAvailability?{query}");
        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<TechnicianAvailability>>();
        return results!.ToDictionary(r => r.Id, r => r.Conflicts);
    }

    [Fact]
    public async Task Availability_BookedAndOnPto_ReportsConflictsAndOthersFree()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var availability = await GetAvailabilityAsync(client, Nov10Morning);

        Assert.Equal(["Booked on Smith Tower, Tue, Nov 10, 8:00 AM – 4:00 PM"], availability[_john.Id]);
        Assert.Equal(["On approved PTO, Tue, Nov 10"], availability[_mike.Id]);
        Assert.Empty(availability[_sara.Id]);
    }

    [Fact]
    public async Task Availability_EditingTheBookedJob_TechnicianIsFree()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var availability = await GetAvailabilityAsync(client, $"{Nov10Morning}&jobId={_smithTower.Id}");

        Assert.Empty(availability[_john.Id]);
    }

    [Theory]
    [InlineData("startDate=2026-11-10&endDate=2026-11-10&startTime=12:00&endTime=09:00")]
    [InlineData("startDate=2026-11-10&endDate=2026-11-09&startTime=09:00&endTime=12:00")]
    [InlineData("startDate=2026-11-10&endDate=2026-11-10&startTime=&endTime=12:00")]
    public async Task Availability_InvalidTimes_BadRequest(string query)
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await client.GetAsync($"/Jobs/TechnicianAvailability?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Availability_Technician_AccessDenied()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, allowAutoRedirect: false);

        var response = await client.GetAsync($"/Jobs/TechnicianAvailability?{Nov10Morning}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EditForm_AssignedTechnicianWithConflict_MarkedAndWarned()
    {
        await SeedAsync();
        var overlapping = await Service<JobService>().CreateAsync(ServiceHarness.ValidJob(_john.Id) with
        {
            Name = "Oak Clinic", StartDate = new DateOnly(2026, 11, 10), EndDate = new DateOnly(2026, 11, 10)
        });
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Jobs/Edit/{overlapping.Id}"));

        Assert.Contains("data-status=\"conflict\" data-name=\"John Smith\"", html);
        Assert.Contains("data-status=\"conflict\" data-name=\"Mike Jones\"", html);
        Assert.Contains("data-status=\"free\" data-name=\"Sara Lee\"", html);
        Assert.Contains("<li><strong>John Smith</strong>: Booked on Smith Tower, Tue, Nov 10, 8:00 AM – 4:00 PM</li>", html);
        Assert.DoesNotContain("<li><strong>Mike Jones</strong>", html);
    }

    [Fact]
    public async Task CreateForm_InvalidHoursAfterPost_TechniciansNotChecked()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/Jobs/Create", "/Jobs/Create", JobFields("12:00", "09:00"));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-status=\"unknown\" data-name=\"John Smith\"", html);
        Assert.Contains("Enter valid dates and hours to check availability.", html);
    }

    [Fact]
    public async Task CreateForm_ConflictingTechnician_StillSaved()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/Jobs/Create", "/Jobs/Create",
            JobFields("09:00", "12:00", ("AssignedTechnicianIds", _john.Id.ToString())));

        response.EnsureSuccessStatusCode();
        var created = Assert.Single(await Service<JobService>().GetAllAsync(), j => j.Name == "Oak Clinic");
        Assert.Equal([_john.Id], created.AssignedTechnicianIds);
    }

    private T Service<T>() where T : notnull => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static (string, string)[] JobFields(string startTime, string endTime, params (string, string)[] extra) =>
    [
        ("Name", "Oak Clinic"), ("Address", "5 Oak Street"), ("StartDate", "2026-11-10"), ("EndDate", "2026-11-10"),
        ("StartTime", startTime), ("EndTime", endTime), ("Status", "Scheduled"), .. extra
    ];
}
