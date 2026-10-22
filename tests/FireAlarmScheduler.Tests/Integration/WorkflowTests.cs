// Purpose: Runs the README's primary workflow end to end over HTTP: add technicians, create and
// schedule a job, assign technicians, add buildings, upload spreadsheets, then download one
// from the technician view. Also covers upload rejection and edit/delete flows.
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
        _client = _factory.CreateBrowser();
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
            ("Notes", "Check in at the security desk.")
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
    public async Task PrimaryWorkflow_TeamLeadSchedulesJob_TechnicianDownloadsBuildingSpreadsheet()
    {
        // Steps 1–4: three technicians; a Nov 2–6 job assigned to two of them.
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

        // Step 5: three buildings.
        foreach (var name in new[] { "Building A", "Building B", "Parking Garage" })
        {
            await AddBuildingAsync(job.Id, name);
        }

        var buildings = Assert.Single(await JobsAsync()).Buildings;
        Assert.Equal(["Building A", "Building B", "Parking Garage"], buildings.Select(b => b.Name));

        // Step 6: a separate spreadsheet per building.
        var buildingABytes = "Device,Location\nSmoke detector,Lobby\n"u8.ToArray();
        foreach (var building in buildings)
        {
            var bytes = building.Name == "Building A" ? buildingABytes : [1, 2, 3];
            var page = $"/Jobs/{job.Id}/Buildings/{building.Id}";
            var upload = await FormPoster.PostFilesAsync(_client, page, $"{page}/Files", ($"{building.Name} devices.csv", bytes));
            upload.EnsureSuccessStatusCode();
            Assert.Contains("Uploaded 1 file.", await upload.Content.ReadAsStringAsync());
        }

        var buildingA = (await JobsAsync())[0].Buildings.Single(b => b.Name == "Building A");
        var fileA = Assert.Single(buildingA.UploadedFiles);

        // Steps 7–8: John's weekly schedule shows the job, address, and buildings; Sara's does not.
        var schedule = await _client.GetStringAsync($"/TechnicianView?technicianId={john.Id}&range=Week&date=2026-11-03");
        Assert.Contains("Central Plaza Annual Fire Alarm Inspection", schedule);
        Assert.Contains("100 Main Street", schedule);
        Assert.Contains("Parking Garage", schedule);
        var saraSchedule = await _client.GetStringAsync($"/TechnicianView?technicianId={sara.Id}&range=Week&date=2026-11-03");
        Assert.DoesNotContain("Central Plaza Annual Fire Alarm Inspection", saraSchedule);

        var jobPage = await _client.GetStringAsync($"/TechnicianView/Job/{job.Id}?technicianId={john.Id}");
        Assert.Contains("Building A devices.csv", jobPage);
        Assert.Contains($"/Jobs/{job.Id}/Buildings/{buildingA.Id}/Files/{fileA.Id}", jobPage);
        Assert.Contains("Check in at the security desk.", jobPage);

        // Step 9: download Building A's spreadsheet, byte for byte, with its original name.
        var download = await _client.GetAsync($"/Jobs/{job.Id}/Buildings/{buildingA.Id}/Files/{fileA.Id}");
        download.EnsureSuccessStatusCode();
        Assert.Equal(buildingABytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("text/csv", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Building A devices.csv", download.Content.Headers.ContentDisposition?.FileNameStar);

        // An unassigned technician cannot open the job page through the technician view.
        var saraJob = await _client.GetAsync($"/TechnicianView/Job/{job.Id}?technicianId={sara.Id}");
        Assert.Equal(HttpStatusCode.NotFound, saraJob.StatusCode);
    }

    [Fact]
    public async Task Upload_DisallowedAndValidFilesTogether_StoresOnlyValidAndReportsRejection()
    {
        var job = await CreateJobAsync();
        await AddBuildingAsync(job.Id, "Building A");
        var building = (await JobsAsync())[0].Buildings[0];
        var page = $"/Jobs/{job.Id}/Buildings/{building.Id}";

        var response = await FormPoster.PostFilesAsync(_client, page, $"{page}/Files",
            ("virus.exe", [1]), ("too-big.xlsx", new byte[2048]), ("devices.xlsx", [1, 2]));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Uploaded 1 file.", html);
        Assert.Contains("&quot;virus.exe&quot; is not allowed", html);
        Assert.Contains("&quot;too-big.xlsx&quot; is larger than", html);
        Assert.Equal("devices.xlsx", Assert.Single((await JobsAsync())[0].Buildings[0].UploadedFiles).OriginalFileName);
        Assert.Single(Directory.EnumerateFiles(_factory.UploadsDirectory));
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
    public async Task DeleteFileAndJob_RemoveRecordsAndStoredFiles()
    {
        var job = await CreateJobAsync();
        await AddBuildingAsync(job.Id, "Building A");
        var building = (await JobsAsync())[0].Buildings[0];
        var page = $"/Jobs/{job.Id}/Buildings/{building.Id}";
        await FormPoster.PostFilesAsync(_client, page, $"{page}/Files", ("a.csv", [1]), ("b.csv", [2]));
        var files = (await JobsAsync())[0].Buildings[0].UploadedFiles;

        (await FormPoster.PostFormAsync(_client, page, $"{page}/Files/{files[0].Id}/Delete")).EnsureSuccessStatusCode();
        Assert.Single((await JobsAsync())[0].Buildings[0].UploadedFiles);
        Assert.Single(Directory.EnumerateFiles(_factory.UploadsDirectory));

        (await FormPoster.PostFormAsync(_client, $"/Jobs/Delete/{job.Id}", $"/Jobs/Delete/{job.Id}")).EnsureSuccessStatusCode();
        Assert.Empty(await JobsAsync());
        Assert.Empty(Directory.EnumerateFiles(_factory.UploadsDirectory));
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
