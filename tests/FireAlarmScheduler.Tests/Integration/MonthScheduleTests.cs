// Purpose: Tests the monthly calendars over HTTP: the team lead's month view and its technician picker
// (all by default, any subset once chosen, none), month navigation keeping the choice, and the
// technician's own month and full-week views, including that technician accounts only see their own jobs.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class MonthScheduleTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private Technician _john = null!;
    private Technician _mike = null!;
    private Technician _sara = null!;

    public void Dispose() => _factory.Dispose();

    /// <summary>John and Mike share a Nov 2–6 job; Mike and Sara each have one more November job; John has one in December.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var technicians = scope.ServiceProvider.GetRequiredService<TechnicianService>();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        _john = await technicians.AddAsync("John", "Smith");
        _mike = await technicians.AddAsync("Mike", "Johnson");
        _sara = await technicians.AddAsync("Sara", "Lee");
        await jobs.CreateAsync(ServiceHarness.ValidJob(_john.Id, _mike.Id) with { Name = "Central Plaza Inspection" });
        await jobs.CreateAsync(ServiceHarness.ValidJob(_mike.Id) with
        {
            Name = "Mike Hospital Test", StartDate = new DateOnly(2026, 11, 16), EndDate = new DateOnly(2026, 11, 16)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(_sara.Id) with
        {
            Name = "Sara School Test", StartDate = new DateOnly(2026, 11, 20), EndDate = new DateOnly(2026, 11, 20)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(_john.Id) with
        {
            Name = "John December Job", StartDate = new DateOnly(2026, 12, 14), EndDate = new DateOnly(2026, 12, 14)
        });
    }

    [Fact]
    public async Task Month_TeamLeadDefault_ShowsEveryTechniciansJobs()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync("/Schedule/Month?month=2026-11-01");

        Assert.Contains("November 2026", html);
        Assert.Contains("Mike Hospital Test", html);
        Assert.Contains("Sara School Test", html);
        Assert.DoesNotContain("John December Job", html);
        Assert.Equal(5, Count(html, ">Central Plaza Inspection<"));
        Assert.Equal(3, Count(html, "type=\"checkbox\" name=\"technicianIds\""));
        Assert.Equal(3, Count(html, "checked=\"checked\""));
    }

    [Fact]
    public async Task Month_TeamLeadChoosesTwoTechnicians_ShowsOnlyTheirJobsAndTags()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync(
            $"/Schedule/Month?month=2026-11-01&filtered=true&technicianIds={_john.Id}&technicianIds={_sara.Id}");

        Assert.Contains("Central Plaza Inspection", html);
        Assert.Contains("Sara School Test", html);
        Assert.DoesNotContain("Mike Hospital Test", html);
        Assert.Equal(2, Count(html, "checked=\"checked\""));
        // Only John's tag is on the shared job; Mike isn't selected, so his "MJ" appears only in the picker.
        Assert.Equal(1 + 5, Count(html, ">JS</span>"));
        Assert.Equal(1, Count(html, ">MJ</span>"));
    }

    [Fact]
    public async Task Month_TeamLeadChoosesNobody_ShowsNoJobsAndAPrompt()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync("/Schedule/Month?month=2026-11-01&filtered=true");

        Assert.Contains("No technicians selected", html);
        Assert.DoesNotContain("Central Plaza Inspection", html);
        Assert.DoesNotContain("checked=\"checked\"", html);
    }

    [Fact]
    public async Task Month_UnknownTechnicianId_IsIgnored()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync($"/Schedule/Month?month=2026-11-01&filtered=true&technicianIds={Guid.NewGuid()}");

        Assert.Contains("No technicians selected", html);
    }

    [Fact]
    public async Task Month_NextMonthLink_KeepsTechnicianChoice()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);
        var november = await client.GetStringAsync($"/Schedule/Month?month=2026-11-01&filtered=true&technicianIds={_john.Id}");
        var nextUrl = System.Text.RegularExpressions.Regex.Match(november, "href=\"([^\"]+)\">Next").Groups[1].Value.Replace("&amp;", "&");

        var december = await client.GetStringAsync(nextUrl);

        Assert.Contains("month=2026-12-01", nextUrl);
        Assert.Contains($"technicianIds={_john.Id}", nextUrl);
        Assert.Contains("December 2026", december);
        Assert.Contains("John December Job", december);
    }

    [Fact]
    public async Task Month_DefaultsToCurrentMonth()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync("/Schedule/Month");

        Assert.Contains("November 2026", html);
    }

    [Fact]
    public async Task Month_TechnicianAccount_IsDenied()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, allowAutoRedirect: false);

        var response = await client.GetAsync("/Schedule/Month");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TechnicianMonth_TechnicianAccount_ShowsOnlyOwnJobsWithLinks()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: _mike.Id);

        var html = await client.GetStringAsync($"/TechnicianView?range=Month&date=2026-11-10&technicianId={_sara.Id}");

        Assert.Contains("November 2026", html);
        Assert.Contains("Central Plaza Inspection", html);
        Assert.Contains("Mike Hospital Test", html);
        Assert.DoesNotContain("Sara School Test", html);
        Assert.Contains("Jobs this month", html);
        Assert.Contains($"/TechnicianView/Job/", html);
        Assert.Contains("range=Day&amp;date=2026-11-16", html);
    }

    [Fact]
    public async Task TechnicianMonth_JobInLeadingDaysFromPreviousMonth_ShowsOnGridButNotInMonthList()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var john = await scope.ServiceProvider.GetRequiredService<TechnicianService>().AddAsync("John", "Smith");
            _john = john;
            await scope.ServiceProvider.GetRequiredService<JobService>().CreateAsync(ServiceHarness.ValidJob(john.Id) with
            {
                Name = "October Wrap-up", StartDate = new DateOnly(2026, 10, 27), EndDate = new DateOnly(2026, 10, 27)
            });
        }

        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: _john.Id);

        var html = await client.GetStringAsync("/TechnicianView?range=Month&date=2026-11-03");

        Assert.Equal(1, Count(html, ">October Wrap-up<"));
        Assert.Contains("No jobs assigned to John Smith this month.", html);
    }

    [Fact]
    public async Task TechnicianWeek_ListsEveryDayIncludingEmptyOnes()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: _sara.Id);

        var html = await client.GetStringAsync("/TechnicianView?range=Week&date=2026-11-18");

        foreach (var day in new[] { "Monday, Nov 16", "Tuesday, Nov 17", "Friday, Nov 20", "Sunday, Nov 22" })
        {
            Assert.Contains(day, html);
        }

        Assert.Equal(6, Count(html, ">No jobs<"));
        Assert.Contains("Sara School Test", html);
    }

    private static int Count(string html, string value) => html.Split(value).Length - 1;
}
