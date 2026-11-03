// Purpose: Tests the Jobs page calendar over HTTP: every job in the month (assigned or not) shown as one colored bar
// spanning its days (no technician tags), a job running into the next month, the month's job list with matching color
// swatches and day counts, month navigation, the List/Calendar tabs, and that technicians can't open it.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class JobsCalendarTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    /// <summary>John's Nov 2–6 job, an unassigned Nov 18 job, and a December job.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobService>();
        var john = await scope.ServiceProvider.GetRequiredService<TechnicianService>().AddAsync("John", "Smith");
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "Central Plaza Inspection" });
        await jobs.CreateAsync(ServiceHarness.ValidJob() with
        {
            Name = "Riverside Hospital", StartDate = new DateOnly(2026, 11, 18), EndDate = new DateOnly(2026, 11, 18)
        });
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with
        {
            Name = "December Job", StartDate = new DateOnly(2026, 12, 14), EndDate = new DateOnly(2026, 12, 14)
        });
    }

    private static int Count(string html, string value) => html.Split(value).Length - 1;

    [Theory]
    [InlineData(AppRoles.TeamLead)]
    [InlineData(AppRoles.Admin)]
    public async Task Calendar_Month_ShowsEveryJobOnEachDayItRuns(string role)
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(role);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs/Calendar?month=2026-11-01"));

        Assert.Contains("November 2026", html);
        Assert.Equal(1, Count(html, "style=\"grid-column: 1 / span 5; grid-row: 2\""));
        Assert.Equal(1, Count(html, ">Central Plaza Inspection</a>"));
        Assert.Equal(1, Count(html, "style=\"grid-column: 3 / span 1; grid-row: 2\""));
        Assert.Equal(1, Count(html, ">Riverside Hospital</a>"));
        Assert.Contains("class=\"job-bar job-color-0\"", html);
        Assert.Contains("class=\"job-bar job-color-1\"", html);
        Assert.DoesNotContain("December Job", html);
        Assert.DoesNotContain("tech-tag", html);
    }

    [Fact]
    public async Task Calendar_JobIntoNextMonth_ShowsOnTrailingDaysAndContinues()
    {
        await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<JobService>().CreateAsync(ServiceHarness.ValidJob() with
            {
                Name = "City Hall", StartDate = new DateOnly(2026, 11, 27), EndDate = new DateOnly(2026, 12, 2)
            });
        }
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs/Calendar?month=2026-11-01"));

        Assert.Contains("class=\"job-bar job-color-2 continues-after\"", html);
        Assert.Contains("class=\"job-bar job-color-2 continues-before\"", html);
        Assert.Equal(2, Count(html, ">City Hall</a>"));
    }

    [Fact]
    public async Task Calendar_Month_ListsMonthsJobsWithLength()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Jobs/Calendar?month=2026-11-01"));

        Assert.Contains("Mon, Nov 2 – Fri, Nov 6, 2026 · 8:00 AM – 4:00 PM · <span class=\"text-body-secondary\">5 days</span>", html);
        var swatch = html.IndexOf("<span class=\"job-swatch job-color-0\" aria-hidden=\"true\"></span>", StringComparison.Ordinal);
        Assert.InRange(html.IndexOf("Central Plaza Inspection</strong>", StringComparison.Ordinal) - swatch, 1, 120);
        Assert.Contains("<span class=\"text-body-secondary\">1 day</span>", html);
        Assert.Contains("href=\"/Jobs/Calendar?month=2026-10-01\"", html);
        Assert.Contains("href=\"/Jobs/Calendar?month=2026-12-01\"", html);
    }

    [Fact]
    public async Task Calendar_DefaultMonth_IsThisMonthAndOtherMonthsAreEmpty()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var current = await client.GetStringAsync("/Jobs/Calendar");
        var empty = await client.GetStringAsync("/Jobs/Calendar?month=2027-03-01");

        Assert.Contains("November 2026", current);
        Assert.Contains("No jobs this month.", empty);
    }

    [Fact]
    public async Task JobsList_HasCalendarTab()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await client.GetStringAsync("/Jobs");

        Assert.Contains("<a class=\"nav-link active\" href=\"/Jobs\">List</a>", html);
        Assert.Contains("href=\"/Jobs/Calendar\">Calendar</a>", html);
    }

    [Fact]
    public async Task Calendar_Technician_AccessDenied()
    {
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, allowAutoRedirect: false);

        var response = await client.GetAsync("/Jobs/Calendar");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
    }
}
