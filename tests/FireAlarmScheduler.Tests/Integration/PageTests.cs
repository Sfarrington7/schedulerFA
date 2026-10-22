// Purpose: Smoke-tests every main page through HTTP, plus not-found handling and
// antiforgery protection on form posts.
using System.Net;
using FireAlarmScheduler.Tests.TestSupport;

namespace FireAlarmScheduler.Tests.Integration;

public class PageTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData("/")]
    [InlineData("/Schedule")]
    [InlineData("/Schedule?week=2026-11-02")]
    [InlineData("/Jobs")]
    [InlineData("/Jobs/Create")]
    [InlineData("/Technicians")]
    [InlineData("/Technicians/Create")]
    [InlineData("/TechnicianView")]
    [InlineData("/TechnicianView?range=Day&date=2026-11-03")]
    public async Task Get_MainPage_ReturnsSuccessWithNavigation(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Team Lead View", html);
        Assert.Contains("Technician View", html);
    }

    [Theory]
    [InlineData("/Jobs/Details/00000000-0000-0000-0000-000000000001")]
    [InlineData("/Technicians/Edit/00000000-0000-0000-0000-000000000001")]
    [InlineData("/Jobs/00000000-0000-0000-0000-000000000001/Buildings/00000000-0000-0000-0000-000000000002")]
    [InlineData("/Jobs/00000000-0000-0000-0000-000000000001/Buildings/00000000-0000-0000-0000-000000000002/Files/00000000-0000-0000-0000-000000000003")]
    [InlineData("/TechnicianView/Job/00000000-0000-0000-0000-000000000001?technicianId=00000000-0000-0000-0000-000000000002")]
    public async Task Get_UnknownIds_ReturnsNotFound(string url)
    {
        var response = await _factory.CreateBrowser().GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTechnician_WithoutAntiforgeryToken_IsRejected()
    {
        var content = new FormUrlEncodedContent([new("FirstName", "John"), new("LastName", "Smith")]);

        var response = await _factory.CreateBrowser().PostAsync("/Technicians/Create", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTechnician_MissingLastName_ShowsValidationError()
    {
        var client = _factory.CreateBrowser();

        var response = await FormPoster.PostFormAsync(client, "/Technicians/Create", "/Technicians/Create",
            ("FirstName", "John"), ("LastName", ""));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The Last name field is required.", html);
        Assert.DoesNotContain("John", await client.GetStringAsync("/Technicians"));
    }
}
