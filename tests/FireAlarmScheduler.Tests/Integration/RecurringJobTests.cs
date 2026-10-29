// Purpose: Tests recurring jobs and Create Job buildings over HTTP: building rows on the Create form, saving a new job
// as recurring, starting a job from a recurring job (details and building rows prefilled with locked names, renames
// ignored, new buildings saved to the series), editing a one-off job into a recurring one, the recurring job page's building list, Remove, and that
// technicians can't reach these pages.
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
    public async Task CreateForm_FromRecurringJob_PrefillsDetailsAndBuildingRows()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Jobs/Create?recurringJobId={series.RecurringJobId}"));

        Assert.Contains("value=\"Central Plaza Annual Fire Alarm Inspection\"", html);
        Assert.Contains("value=\"100 Main Street\"", html);
        Assert.Contains("value=\"Central Plaza LLC\"", html);
        Assert.Contains($"<option value=\"{series.RecurringJobId}\" selected=\"selected\">", html);
        Assert.Contains("name=\"Buildings[0].Name\" value=\"Building A\" class=\"form-control\" readonly", html);
        Assert.Contains("name=\"Buildings[0].RecurringBuildingId\"", html);
        Assert.Contains("name=\"Buildings[2].Name\" value=\"Parking Garage\"", html);
        Assert.Contains("name=\"Buildings[3].Name\" class=", html);
        Assert.DoesNotContain("name=\"SaveAsRecurring\"", html);
    }

    [Fact]
    public async Task CreateForm_SubmitFromRecurringJob_SavesRowsAndAddsNewBuildingToSeries()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, $"/Jobs/Create?recurringJobId={series.RecurringJobId}", "/Jobs/Create",
            JobFields("Central Plaza Annual Fire Alarm Inspection", "2027-11-01", "2027-11-05",
                ("RecurringJobId", series.RecurringJobId.ToString()!),
                ("Buildings[0].Name", "Building A"), ("Buildings[1].Name", "Parking Garage"),
                ("Buildings[2].Name", "Building C"), ("Buildings[2].Notes", "New wing"), ("Buildings[3].Name", "")));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Job created with 3 building(s).", html);
        var newJob = (await Service<JobService>().GetAllAsync()).Single(j => j.StartDate.Year == 2027);
        Assert.Equal(series.RecurringJobId, newJob.RecurringJobId);
        Assert.Equal(["Building A", "Parking Garage", "Building C"], newJob.Buildings.Select(b => b.Name));
        var seriesBuildings = (await Service<RecurringJobService>().GetAsync(series.RecurringJobId!.Value))!.Buildings.Select(b => b.Name);
        Assert.Equal(["Building A", "Building B", "Parking Garage", "Building C"], seriesBuildings);
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

    [Fact]
    public async Task CreateForm_NewJobWithBuildingRows_SavesBuildings()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/Jobs/Create", "/Jobs/Create",
            JobFields("Riverside Hospital", "2026-11-10", "2026-11-12",
                ("Buildings[0].Name", "Main Tower"), ("Buildings[0].Notes", "FACP in basement"),
                ("Buildings[1].Name", ""), ("Buildings[2].Name", "Clinic")));

        Assert.Contains("Job created with 2 building(s).", await response.Content.ReadAsStringAsync());
        var job = Assert.Single(await Service<JobService>().GetAllAsync());
        Assert.Equal(["Main Tower", "Clinic"], job.Buildings.Select(b => b.Name));
        Assert.Equal("FACP in basement", job.Buildings[0].Notes);
    }

    [Fact]
    public async Task CreateForm_DuplicateBuildingRows_ShowsErrorAndSavesNothing()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(client, "/Jobs/Create", "/Jobs/Create",
            JobFields("Riverside Hospital", "2026-11-10", "2026-11-12", ("Buildings[0].Name", "Clinic"), ("Buildings[1].Name", "clinic")));

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("The building \"Clinic\" is listed more than once.", html);
        Assert.Contains("name=\"Buildings[1].Name\" value=\"clinic\"", html);
        Assert.Empty(await Service<JobService>().GetAllAsync());
    }

    [Fact]
    public async Task RecurringJobPage_AddAndRemoveBuilding_UpdatesListAndUpcomingJobs()
    {
        var series = await SeedSeriesAsync();
        var upcoming = await Service<JobService>().CreateAsync(ServiceHarness.ValidJob() with
        {
            StartDate = new DateOnly(2027, 11, 1), EndDate = new DateOnly(2027, 11, 5), RecurringJobId = series.RecurringJobId
        });
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);
        var page = $"/RecurringJobs/Details/{series.RecurringJobId}";

        var added = await FormPoster.PostFormAsync(client, page, $"/RecurringJobs/AddBuilding/{series.RecurringJobId}",
            ("NewBuilding.Name", "Pump House"), ("NewBuilding.Notes", ""));

        var html = await added.Content.ReadAsStringAsync();
        Assert.Contains("Added Pump House to this recurring job and its upcoming jobs.", html);
        Assert.Contains("Pump House", (await Service<JobService>().GetAsync(upcoming.Id))!.Buildings.Select(b => b.Name));

        var recurring = (await Service<RecurringJobService>().GetAsync(series.RecurringJobId!.Value))!;
        var buildingB = recurring.Buildings.Single(b => b.Name == "Building B");
        var removed = await FormPoster.PostFormAsync(client, page, $"/RecurringJobs/RemoveBuilding/{recurring.Id}?buildingId={buildingB.Id}");

        Assert.Contains("Building removed from the list for future jobs.", await removed.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Building B", (await Service<RecurringJobService>().GetAsync(recurring.Id))!.Buildings.Select(b => b.Name));
        Assert.Contains("Building B", (await Service<JobService>().GetAsync(upcoming.Id))!.Buildings.Select(b => b.Name));
    }

    [Fact]
    public async Task JobPage_AddBuildingToSeriesJob_ShowsOnRecurringJobPage()
    {
        var series = await SeedSeriesAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        await FormPoster.PostFormAsync(client, $"/Jobs/Details/{series.Id}", $"/Jobs/{series.Id}/Buildings",
            ("NewBuilding.Name", "Building C"), ("NewBuilding.Notes", ""));

        Assert.Contains("Building C", await client.GetStringAsync($"/RecurringJobs/Details/{series.RecurringJobId}"));
    }

    [Fact]
    public async Task CreateForm_RenamedSeriesBuildingPosted_SavedWithOriginalName()
    {
        var series = await SeedSeriesAsync();
        var recurring = (await Service<RecurringJobService>().GetAsync(series.RecurringJobId!.Value))!;
        var buildingA = recurring.Buildings.Single(b => b.Name == "Building A");
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        await FormPoster.PostFormAsync(client, $"/Jobs/Create?recurringJobId={recurring.Id}", "/Jobs/Create",
            JobFields("Central Plaza Annual Fire Alarm Inspection", "2027-11-01", "2027-11-05",
                ("RecurringJobId", recurring.Id.ToString()),
                ("Buildings[0].RecurringBuildingId", buildingA.Id.ToString()), ("Buildings[0].Name", "Sneaky Rename")));

        var newJob = (await Service<JobService>().GetAllAsync()).Single(j => j.StartDate.Year == 2027);
        Assert.Equal("Building A", Assert.Single(newJob.Buildings).Name);
        Assert.DoesNotContain("Sneaky Rename", (await Service<RecurringJobService>().GetAsync(recurring.Id))!.Buildings.Select(b => b.Name));
    }
}
