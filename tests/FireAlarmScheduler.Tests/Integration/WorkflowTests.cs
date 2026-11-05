// Purpose: Runs the README's primary workflow end to end over HTTP: a signed-in team lead adds technicians,
// creates and schedules a job with a link to its files, assigns technicians, and adds buildings; then a technician
// account signs in, opens the job, and checks buildings off while the team lead watches progress. Also covers
// validation, edit, and delete flows.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class WorkflowTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private readonly HttpClient _client;

    public WorkflowTests()
    {
        _client = _factory.SignInAsNewUserAsync(AppRoles.TeamLead).GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private async Task<List<Technician>> TechniciansAsync() =>
        await _factory.Services.GetRequiredService<ITechnicianRepository>().GetAllAsync();

    private async Task<List<Job>> JobsAsync() =>
        await _factory.Services.GetRequiredService<IJobRepository>().GetAllAsync();

    private async Task AddTechnicianAsync(string first, string last)
    {
        var response = await FormPoster.PostFormAsync(_client, "/Technicians/Create", "/Technicians/Create",
            ("FirstName", first), ("LastName", last));
        response.EnsureSuccessStatusCode();
    }

    private async Task<Job> CreateJobAsync(params Guid[] technicianIds)
    {
        var fields = new List<(string, string)>
        {
            ("Name", "Central Plaza Annual Fire Alarm Inspection"),
            ("CustomerName", "Central Plaza LLC"),
            ("Address", "100 Main Street"),
            ("StartDate", "2026-11-02"),
            ("EndDate", "2026-11-06"),
            ("StartTime", "08:00"),
            ("EndTime", "16:30"),
            ("Status", "Scheduled"),
            ("Notes", "Check in at the security desk."),
            ("FilesUrl", "https://drive.google.com/drive/folders/plaza-2026")
        };
        fields.AddRange(technicianIds.Select(id => ("AssignedTechnicianIds", id.ToString())));

        var response = await FormPoster.PostFormAsync(_client, "/Jobs/Create", "/Jobs/Create", [.. fields]);
        response.EnsureSuccessStatusCode();
        return Assert.Single(await JobsAsync());
    }

    private async Task AddBuildingAsync(Guid jobId, string name)
    {
        var response = await FormPoster.PostFormAsync(_client, $"/Jobs/Details/{jobId}", $"/Jobs/{jobId}/Buildings",
            ("NewBuilding.Name", name), ("NewBuilding.Notes", ""));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PrimaryWorkflow_TeamLeadSchedulesJob_TechnicianOpensFilesAndChecksOffBuildings()
    {
        // Steps 1–4: three technicians; a Nov 2–6 job assigned to two of them, with a link to its files.
        await AddTechnicianAsync("John", "Smith");
        await AddTechnicianAsync("Mike", "Johnson");
        await AddTechnicianAsync("Sara", "Lee");
        var techs = await TechniciansAsync();
        var john = techs.Single(t => t.FirstName == "John");
        var mike = techs.Single(t => t.FirstName == "Mike");
        var sara = techs.Single(t => t.FirstName == "Sara");
        var job = await CreateJobAsync(john.Id, mike.Id);
        Assert.Equal(new DateOnly(2026, 11, 2), job.StartDate);
        Assert.Equal(new TimeOnly(16, 30), job.EndTime);
        Assert.Equal("https://drive.google.com/drive/folders/plaza-2026", job.FilesUrl);

        // Step 5: three buildings.
        foreach (var name in new[] { "Building A", "Building B", "Parking Garage" })
        {
            await AddBuildingAsync(job.Id, name);
        }

        var buildings = Assert.Single(await JobsAsync()).Buildings;
        Assert.Equal(["Building A", "Building B", "Parking Garage"], buildings.Select(b => b.Name));
        var buildingA = buildings[0];

        // Steps 6–7: the team lead can view John's weekly schedule (job, address, buildings); Sara's lacks the job.
        var schedule = await _client.GetStringAsync($"/TechnicianView?technicianId={john.Id}&range=Week&date=2026-11-03");
        Assert.Contains("Central Plaza Annual Fire Alarm Inspection", schedule);
        Assert.Contains("100 Main Street", schedule);
        Assert.Contains("Parking Garage", schedule);
        var saraSchedule = await _client.GetStringAsync($"/TechnicianView?technicianId={sara.Id}&range=Week&date=2026-11-03");
        Assert.DoesNotContain("Central Plaza Annual Fire Alarm Inspection", saraSchedule);

        // Step 8: John signs in with his own technician account and sees his schedule without choosing a name.
        await _factory.CreateUserAsync("john@example.test", AppRoles.Technician, john.Id);
        var johnClient = await _factory.SignInAsync("john@example.test");
        var johnSchedule = await johnClient.GetStringAsync("/TechnicianView?range=Week&date=2026-11-03");
        Assert.Contains("My Schedule", johnSchedule);
        Assert.Contains("Central Plaza Annual Fire Alarm Inspection", johnSchedule);
        Assert.DoesNotContain("Sara Lee", johnSchedule);

        // Step 9: his job page links to the files and lists every building to do.
        var jobPageUrl = $"/TechnicianView/Job/{job.Id}?technicianId={john.Id}";
        var jobPage = await johnClient.GetStringAsync(jobPageUrl);
        Assert.Contains("href=\"https://drive.google.com/drive/folders/plaza-2026\"", jobPage);
        Assert.Contains("Check in at the security desk.", jobPage);
        Assert.Contains("0 of 3 buildings complete", jobPage);

        // Step 10: John checks Building A off; the team lead sees the progress and who did it.
        var tick = await FormPoster.PostFormAsync(johnClient, jobPageUrl,
            $"/TechnicianView/SetBuildingComplete/{job.Id}?buildingId={buildingA.Id}", ("complete", "true"));
        tick.EnsureSuccessStatusCode();
        Assert.Contains("1 of 3 buildings complete", await tick.Content.ReadAsStringAsync());
        var leadJobPage = WebUtility.HtmlDecode(await _client.GetStringAsync($"/Jobs/Details/{job.Id}"));
        Assert.Contains("1 of 3 buildings complete", leadJobPage);
        Assert.Contains("Tue, Nov 3 · John Smith", leadJobPage);

        // An unassigned technician cannot open the job page through the technician view.
        var saraJob = await _client.GetAsync($"/TechnicianView/Job/{job.Id}?technicianId={sara.Id}");
        Assert.Equal(HttpStatusCode.NotFound, saraJob.StatusCode);

        // Sara's own account can't see John's job, even with John's id in the URL.
        await _factory.CreateUserAsync("sara@example.test", AppRoles.Technician, sara.Id);
        var saraClient = await _factory.SignInAsync("sara@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await saraClient.GetAsync($"/TechnicianView/Job/{job.Id}?technicianId={john.Id}")).StatusCode);
        Assert.DoesNotContain("Central Plaza", await saraClient.GetStringAsync($"/TechnicianView?technicianId={john.Id}&date=2026-11-03"));
    }

    [Fact]
    public async Task CreateJob_EndDateBeforeStart_ShowsErrorAndSavesNothing()
    {
        var response = await FormPoster.PostFormAsync(_client, "/Jobs/Create", "/Jobs/Create",
            ("Name", "Bad dates"), ("Address", "1 Main St"), ("StartDate", "2026-11-06"), ("EndDate", "2026-11-02"),
            ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "Scheduled"));

        Assert.Contains("End date cannot be before the start date.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await JobsAsync());
    }

    [Fact]
    public async Task EditJob_ChangesAssignmentAndStatus()
    {
        await AddTechnicianAsync("John", "Smith");
        await AddTechnicianAsync("Mike", "Johnson");
        var techs = await TechniciansAsync();
        var job = await CreateJobAsync(techs[0].Id);

        var response = await FormPoster.PostFormAsync(_client, $"/Jobs/Edit/{job.Id}", $"/Jobs/Edit/{job.Id}",
            ("Name", job.Name), ("Address", job.Address), ("StartDate", "2026-11-02"), ("EndDate", "2026-11-06"),
            ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "InProgress"),
            ("AssignedTechnicianIds", techs[1].Id.ToString()));

        response.EnsureSuccessStatusCode();
        var updated = Assert.Single(await JobsAsync());
        Assert.Equal(JobStatus.InProgress, updated.Status);
        Assert.Equal([techs[1].Id], updated.AssignedTechnicianIds);
        Assert.Contains("In Progress", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RemoveBuildingAndDeleteJob_RemoveRecords()
    {
        var job = await CreateJobAsync();
        await AddBuildingAsync(job.Id, "Building A");
        await AddBuildingAsync(job.Id, "Building B");
        var building = (await JobsAsync())[0].Buildings[0];
        var editPage = $"/Jobs/{job.Id}/Buildings/{building.Id}/Edit";

        var removed = await FormPoster.PostFormAsync(_client, editPage, $"/Jobs/{job.Id}/Buildings/{building.Id}/Delete");
        removed.EnsureSuccessStatusCode();
        Assert.Contains("Building removed.", await removed.Content.ReadAsStringAsync());
        Assert.Equal("Building B", Assert.Single((await JobsAsync())[0].Buildings).Name);

        (await FormPoster.PostFormAsync(_client, $"/Jobs/Delete/{job.Id}", $"/Jobs/Delete/{job.Id}")).EnsureSuccessStatusCode();
        Assert.Empty(await JobsAsync());
    }

    [Fact]
    public async Task EditBuilding_Saved_ReturnsToJobPage()
    {
        var job = await CreateJobAsync();
        await AddBuildingAsync(job.Id, "Bldg A");
        var building = (await JobsAsync())[0].Buildings[0];
        var editPage = $"/Jobs/{job.Id}/Buildings/{building.Id}/Edit";

        var response = await FormPoster.PostFormAsync(_client, editPage, editPage, ("Name", "Building A"), ("Notes", "Roof access"));

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/Jobs/Details/{job.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("Building updated.", await response.Content.ReadAsStringAsync());
        Assert.Equal("Building A", (await JobsAsync())[0].Buildings[0].Name);
    }

    [Fact]
    public async Task WeeklySchedule_ShowsTechnicianRowsWithTheirJobs()
    {
        await AddTechnicianAsync("John", "Smith");
        var john = Assert.Single(await TechniciansAsync());
        await CreateJobAsync(john.Id);

        var html = await _client.GetStringAsync("/Schedule?week=2026-11-04");

        Assert.Contains("Week of Nov 2 – Nov 8, 2026", html);
        Assert.Contains("John Smith", html);
        Assert.Equal(5, html.Split("Central Plaza Annual Fire Alarm Inspection").Length - 1);
    }

    [Fact]
    public async Task TechnicianView_RemembersSelectedTechnician()
    {
        await AddTechnicianAsync("John", "Smith");
        var john = Assert.Single(await TechniciansAsync());
        await CreateJobAsync(john.Id);

        await _client.GetAsync($"/TechnicianView?technicianId={john.Id}");
        var html = await _client.GetStringAsync("/TechnicianView?range=Day&date=2026-11-03");

        Assert.Contains("Central Plaza Annual Fire Alarm Inspection", html);
        Assert.Contains("Tuesday, November 3, 2026", html);
    }
}
