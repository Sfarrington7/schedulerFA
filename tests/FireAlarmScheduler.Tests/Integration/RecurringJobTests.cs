// Purpose: Tests recurring jobs over HTTP: saving a new job as recurring from the Create form, starting a job from a
// recurring job (form prefilled, building preview, buildings copied on save), editing a one-off job into a recurring
// one, the Recurring jobs page with Remove, and that technicians can't reach it.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class RecurringJobTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private T Service<T>() where T : notnull => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static (string, string)[] JobFields(string name, string startDate, string endDate, params (string, string)[] extra) =>
    [
        ("Name", name), ("CustomerName", "Central Plaza LLC"), ("SiteName", "Central Plaza"), ("Address", "100 Main Street"),
        ("StartDate", startDate), ("EndDate", endDate), ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "Scheduled"),
        .. extra
    ];

    /// <summary>A recurring 2026 job with Building A, Building B, and Parking Garage.</summary>
    private async Task<Job> SeedSeriesAsync()
    {
        var jobs = Service<JobService>();
        var job = await jobs.CreateAsync(ServiceHarness.ValidJob() with { SaveAsRecurring = true });
        foreach (var name in new[] { "Building A", "Building B", "Parking Garage" })
        {
            await jobs.AddBuildingAsync(job.Id, name, null);
        }

        return (await jobs.GetAsync(job.Id))!;
    }

    [Fact]
    public async Task CreateForm_SaveAsRecurringTicked_JobAppearsInPickerAndRecurringPage()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/Jobs/Create", "/Jobs/Create",
            JobFields("Central Plaza Annual Inspection", "2026-11-02", "2026-11-06", ("SaveAsRecurring", "true")));

        response.EnsureSuccessStatusCode();
        var recurring = Assert.Single(await Service<RecurringJobService>().GetAllAsync()).RecurringJob;
        var createPage = await client.GetStringAsync("/Jobs/Create");
        Assert.Contains($"<option value=\"{recurring.Id}\">Central Plaza Annual Inspection", createPage);
        var listPage = await client.GetStringAsync("/RecurringJobs");
        Assert.Contains("Central Plaza Annual Inspection", listPage);
        Assert.Contains("Central Plaza LLC", listPage);
    }

    [Fact]
    public async Task CreateForm_FromRecurringJob_PrefillsDetailsAndPreviewsBuildings()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Jobs/Create?recurringJobId={series.RecurringJobId}"));

        Assert.Contains("value=\"Central Plaza Annual Fire Alarm Inspection\"", html);
        Assert.Contains("value=\"100 Main Street\"", html);
        Assert.Contains("value=\"Central Plaza LLC\"", html);
        Assert.Contains($"<option value=\"{series.RecurringJobId}\" selected=\"selected\">", html);
        Assert.Contains("Building A, Building B, Parking Garage", html);
        Assert.DoesNotContain("name=\"SaveAsRecurring\"", html);
    }

    [Fact]
    public async Task CreateForm_SubmitFromRecurringJob_CopiesBuildingsAndSaysSo()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, $"/Jobs/Create?recurringJobId={series.RecurringJobId}", "/Jobs/Create",
            JobFields("Central Plaza Annual Fire Alarm Inspection", "2027-11-01", "2027-11-05",
                ("RecurringJobId", series.RecurringJobId.ToString()!)));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Job created with 3 building(s) copied", html);
        var newJob = (await Service<JobService>().GetAllAsync()).Single(j => j.StartDate.Year == 2027);
        Assert.Equal(series.RecurringJobId, newJob.RecurringJobId);
        Assert.Equal(["Building A", "Building B", "Parking Garage"], newJob.Buildings.Select(b => b.Name));
        Assert.Equal($"/Jobs/Details/{newJob.Id}", response.RequestMessage?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task CreateForm_UnknownRecurringJobId_IsIgnoredOnLoad()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await client.GetAsync($"/Jobs/Create?recurringJobId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"SaveAsRecurring\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditForm_OneOffJob_CanBeSavedAsRecurring()
    {
        var job = await Service<JobService>().CreateAsync(ServiceHarness.ValidJob());
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, $"/Jobs/Edit/{job.Id}", $"/Jobs/Edit/{job.Id}",
            JobFields(job.Name, "2026-11-02", "2026-11-06", ("SaveAsRecurring", "true")));

        response.EnsureSuccessStatusCode();
        Assert.NotNull((await Service<JobService>().GetAsync(job.Id))!.RecurringJobId);
        var editPage = WebUtility.HtmlDecode(await client.GetStringAsync($"/Jobs/Edit/{job.Id}"));
        Assert.Contains("Part of <strong>Central Plaza Annual Fire Alarm Inspection</strong>", editPage);
        Assert.Contains("Recurring", await client.GetStringAsync("/Jobs"));
    }

    [Fact]
    public async Task RecurringPage_Remove_KeepsJobs()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/RecurringJobs", $"/RecurringJobs/Delete/{series.RecurringJobId}");

        Assert.Contains("Recurring job removed.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await Service<RecurringJobService>().GetAllAsync());
        Assert.NotNull(await Service<JobService>().GetAsync(series.Id));
    }

    [Theory]
    [InlineData("/RecurringJobs")]
    [InlineData("/Jobs/Create")]
    public async Task RecurringPages_Technician_AccessDenied(string url)
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, allowAutoRedirect: false);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
    }
}
