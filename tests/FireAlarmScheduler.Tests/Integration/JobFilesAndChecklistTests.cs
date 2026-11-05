// Purpose: Tests the job files link and the building checklist over HTTP. Team leads add, change, or clear a job's link
// (any website; unsafe links are refused). Technicians see it on their job page, opening in a new tab, and tick buildings
// between "To do" and "Completed", by plain form post or the checklist script's JSON request. Team leads see the
// progress, but only the assigned technician's own account can change it. The old upload/download routes are gone.
using System.Net;
using System.Text.Json;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class JobFilesAndChecklistTests : IDisposable
{
    private const string DriveLink = "https://drive.google.com/drive/folders/1AbC_plaza?usp=sharing";

    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private T Service<T>() where T : notnull => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static async Task<string> GetAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url));

    private record Seeded(Job Job, Technician John, HttpClient JohnClient, HttpClient SaraClient, HttpClient Lead)
    {
        public Guid BuildingId(string name) => Job.Buildings.Single(b => b.Name == name).Id;
        public string JobPage => $"/TechnicianView/Job/{Job.Id}?technicianId={John.Id}";
        public string Tick(string name) => $"/TechnicianView/SetBuildingComplete/{Job.Id}?buildingId={BuildingId(name)}";
    }

    /// <summary>John (assigned) and Sara (not) with their own accounts, a team lead, and John's job with three buildings.</summary>
    private async Task<Seeded> SeedAsync()
    {
        var technicians = Service<TechnicianService>();
        var jobs = Service<JobService>();
        var john = await technicians.AddAsync("John", "Smith");
        var sara = await technicians.AddAsync("Sara", "Lee");
        var job = await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { FilesUrl = DriveLink });
        foreach (var name in new[] { "Building A", "Building B", "Parking Garage" })
        {
            await jobs.AddBuildingAsync(job.Id, name, null);
        }

        await _factory.CreateUserAsync("john@example.test", AppRoles.Technician, john.Id);
        await _factory.CreateUserAsync("sara@example.test", AppRoles.Technician, sara.Id);
        return new Seeded((await jobs.GetAsync(job.Id))!, john,
            await _factory.SignInAsync("john@example.test"), await _factory.SignInAsync("sara@example.test"),
            await _factory.SignInAsNewUserAsync(AppRoles.TeamLead));
    }

    /// <summary>The building names in the checklist's "To do" or "Completed" list, in page order.</summary>
    private static List<string> Section(string html, string list)
    {
        var start = html.IndexOf($"data-list=\"{list}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {list} list on the page.");
        var end = html.IndexOf("</ul>", start, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Matches(html[start..end], "class=\"checklist-name\">([^<]+)<")
            .Select(m => m.Groups[1].Value).ToList();
    }

    private static (string, string)[] JobFields(string filesUrl) =>
    [
        ("Name", "Riverside Hospital"), ("Address", "1 River Road"), ("StartDate", "2026-11-10"), ("EndDate", "2026-11-12"),
        ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "Scheduled"), ("FilesUrl", filesUrl)
    ];

    [Fact]
    public async Task CreateJob_FilesLink_SavedAndShownOnJobPage()
    {
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);
        Assert.Contains("name=\"FilesUrl\"", await lead.GetStringAsync("/Jobs/Create"));

        var response = await FormPoster.PostFormAsync(lead, "/Jobs/Create", "/Jobs/Create", JobFields("https://contoso.sharepoint.com/:f:/s/fire/Eab12"));

        response.EnsureSuccessStatusCode();
        var job = Assert.Single(await Service<JobService>().GetAllAsync());
        Assert.Equal("https://contoso.sharepoint.com/:f:/s/fire/Eab12", job.FilesUrl);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("href=\"https://contoso.sharepoint.com/:f:/s/fire/Eab12\"", page);
        Assert.Contains("contoso.sharepoint.com", page);
    }

    [Fact]
    public async Task CreateJob_LinkPastedWithoutHttps_SavedWithHttps()
    {
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        await FormPoster.PostFormAsync(lead, "/Jobs/Create", "/Jobs/Create", JobFields("drive.google.com/drive/folders/abc"));

        Assert.Equal("https://drive.google.com/drive/folders/abc", Assert.Single(await Service<JobService>().GetAllAsync()).FilesUrl);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("TBD")]
    public async Task CreateJob_UnsafeOrInvalidLink_ShowsErrorAndSavesNothing(string link)
    {
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var response = await FormPoster.PostFormAsync(lead, "/Jobs/Create", "/Jobs/Create", JobFields(link));

        Assert.Contains(JobFilesLink.InvalidMessage, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Empty(await Service<JobService>().GetAllAsync());
    }

    [Fact]
    public async Task EditJob_ClearFilesLink_JobPagesSayNoLink()
    {
        var seeded = await SeedAsync();
        var edit = $"/Jobs/Edit/{seeded.Job.Id}";
        Assert.Contains($"value=\"{DriveLink}\"", await GetAsync(seeded.Lead, edit));

        var response = await FormPoster.PostFormAsync(seeded.Lead, edit, edit,
            ("Name", seeded.Job.Name), ("Address", seeded.Job.Address), ("StartDate", "2026-11-02"), ("EndDate", "2026-11-06"),
            ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "Scheduled"), ("AssignedTechnicianIds", seeded.John.Id.ToString()),
            ("FilesUrl", ""));

        response.EnsureSuccessStatusCode();
        Assert.Null((await Service<JobService>().GetAsync(seeded.Job.Id))!.FilesUrl);
        Assert.Contains("No files link added", await response.Content.ReadAsStringAsync());
        Assert.Contains("No files link added", await GetAsync(seeded.JohnClient, seeded.JobPage));
    }

    [Fact]
    public async Task TechnicianJobPage_FilesLink_OpensInNewTabWithoutSharingThePage()
    {
        var seeded = await SeedAsync();

        var html = await GetAsync(seeded.JohnClient, seeded.JobPage);

        Assert.Contains($"href=\"{DriveLink}\" target=\"_blank\" rel=\"noopener noreferrer\"", html);
        Assert.Contains("Open job files", html);
        Assert.Contains("drive.google.com", html);
    }

    [Fact]
    public async Task Checklist_NewJob_EveryBuildingToDoInJobOrder()
    {
        var seeded = await SeedAsync();

        var html = await GetAsync(seeded.JohnClient, seeded.JobPage);

        Assert.Equal(["Building A", "Building B", "Parking Garage"], Section(html, "todo"));
        Assert.Empty(Section(html, "done"));
        Assert.Contains("0 of 3 buildings complete", html);
        Assert.Contains("data-empty=\"todo\" hidden", html);
        Assert.DoesNotContain("data-empty=\"done\" hidden", html);
        Assert.DoesNotContain("disabled", html[html.IndexOf("data-list=\"todo\"", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task Checklist_TickWithFormPost_MovesBuildingToCompleted()
    {
        var seeded = await SeedAsync();

        var response = await FormPoster.PostFormAsync(seeded.JohnClient, seeded.JobPage, seeded.Tick("Building B"), ("complete", "true"));

        response.EnsureSuccessStatusCode();
        Assert.Equal($"/TechnicianView/Job/{seeded.Job.Id}", response.RequestMessage!.RequestUri!.AbsolutePath);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(["Building A", "Parking Garage"], Section(html, "todo"));
        Assert.Equal(["Building B"], Section(html, "done"));
        Assert.Contains("1 of 3 buildings complete", html);
        Assert.Contains("Tue, Nov 3 · John Smith", html);
    }

    [Fact]
    public async Task Checklist_TickFromScript_ReturnsNewStateAsJson()
    {
        var seeded = await SeedAsync();

        var response = await FormPoster.PostFormForJsonAsync(seeded.JohnClient, seeded.JobPage, seeded.Tick("Parking Garage"), ("complete", "true"));

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("isComplete").GetBoolean());
        Assert.Equal("Tue, Nov 3 · John Smith", json.RootElement.GetProperty("completedText").GetString());
        Assert.Equal(["Parking Garage"], Section(await GetAsync(seeded.JohnClient, seeded.JobPage), "done"));
    }

    [Fact]
    public async Task Checklist_Untick_MovesBuildingBackToToDoInItsPlace()
    {
        var seeded = await SeedAsync();
        await FormPoster.PostFormAsync(seeded.JohnClient, seeded.JobPage, seeded.Tick("Building A"), ("complete", "true"));

        // An unticked checkbox posts nothing, so "complete" is simply missing.
        var response = await FormPoster.PostFormForJsonAsync(seeded.JohnClient, seeded.JobPage, seeded.Tick("Building A"));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("isComplete").GetBoolean());
        Assert.Equal("", json.RootElement.GetProperty("completedText").GetString());
        var html = await GetAsync(seeded.JohnClient, seeded.JobPage);
        Assert.Equal(["Building A", "Building B", "Parking Garage"], Section(html, "todo"));
        Assert.Empty(Section(html, "done"));
    }

    [Fact]
    public async Task Checklist_TechnicianNotOnJob_GetsNotFoundAndNothingChanges()
    {
        var seeded = await SeedAsync();
        var token = await FormPoster.GetAntiforgeryTokenAsync(seeded.SaraClient, "/TechnicianView");

        var response = await seeded.SaraClient.PostAsync(seeded.Tick("Building A"), new FormUrlEncodedContent(
            [new("complete", "true"), new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.All((await Service<JobService>().GetAsync(seeded.Job.Id))!.Buildings, b => Assert.False(b.IsComplete));
    }

    [Fact]
    public async Task Checklist_TeamLead_SeesItReadOnlyAndCannotTick()
    {
        var seeded = await SeedAsync();

        var html = await GetAsync(seeded.Lead, seeded.JobPage);
        Assert.Contains("Only John Smith can check buildings off this list.", html);
        Assert.Equal(3, html.Split("disabled=\"disabled\"").Length - 1);

        var token = await FormPoster.GetAntiforgeryTokenAsync(seeded.Lead, seeded.JobPage);
        var response = await seeded.Lead.PostAsync(seeded.Tick("Building A"), new FormUrlEncodedContent(
            [new("complete", "true"), new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.All((await Service<JobService>().GetAsync(seeded.Job.Id))!.Buildings, b => Assert.False(b.IsComplete));
    }

    [Fact]
    public async Task Checklist_WithoutAntiforgeryToken_IsRejected()
    {
        var seeded = await SeedAsync();

        var response = await seeded.JohnClient.PostAsync(seeded.Tick("Building A"), new FormUrlEncodedContent([new("complete", "true")]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.All((await Service<JobService>().GetAsync(seeded.Job.Id))!.Buildings, b => Assert.False(b.IsComplete));
    }

    [Fact]
    public async Task TeamLeadJobPage_ShowsProgressAndWhoCompletedEachBuilding()
    {
        var seeded = await SeedAsync();
        await FormPoster.PostFormAsync(seeded.JohnClient, seeded.JobPage, seeded.Tick("Building B"), ("complete", "true"));

        var html = await GetAsync(seeded.Lead, $"/Jobs/Details/{seeded.Job.Id}");

        Assert.Contains("1 of 3 buildings complete", html);
        Assert.Contains("33%", html);
        Assert.Equal(2, html.Split("data-building-status=\"todo\"").Length - 1);
        Assert.Equal(1, html.Split("data-building-status=\"complete\"").Length - 1);
        Assert.Contains("Tue, Nov 3 · John Smith", html);
        Assert.Contains($"/Jobs/{seeded.Job.Id}/Buildings/{seeded.BuildingId("Building B")}/Edit", html);
    }

    [Fact]
    public async Task EditOrRemoveBuilding_Technician_IsDeniedAndNothingChanges()
    {
        var seeded = await SeedAsync();
        var building = $"/Jobs/{seeded.Job.Id}/Buildings/{seeded.BuildingId("Building A")}";

        var edit = await seeded.JohnClient.GetAsync($"{building}/Edit");
        var token = await FormPoster.GetAntiforgeryTokenAsync(seeded.JohnClient, seeded.JobPage);
        var remove = await seeded.JohnClient.PostAsync($"{building}/Delete", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Contains("/Account/AccessDenied", edit.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("/Account/AccessDenied", remove.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(3, (await Service<JobService>().GetAsync(seeded.Job.Id))!.Buildings.Count);
    }

    [Fact]
    public async Task OldUploadAndDownloadRoutes_NoLongerExist()
    {
        var seeded = await SeedAsync();
        var building = $"/Jobs/{seeded.Job.Id}/Buildings/{seeded.BuildingId("Building A")}";

        Assert.Equal(HttpStatusCode.NotFound, (await seeded.Lead.GetAsync(building)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seeded.Lead.GetAsync($"{building}/Files/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await seeded.JohnClient.GetAsync($"{building}/Files/{Guid.NewGuid()}")).StatusCode);
        // Any unknown URL answers a POST with 405 (the static-file fallback only takes GET), so either code means "no upload here".
        var upload = await seeded.Lead.PostAsync($"{building}/Files", new MultipartFormDataContent { { new ByteArrayContent([1]), "files", "a.csv" } });
        Assert.Contains(upload.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
