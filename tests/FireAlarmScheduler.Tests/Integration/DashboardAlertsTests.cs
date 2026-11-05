// Purpose: Tests the dashboard's summary cards and the sidebar's PTO/Conflicts count badges over HTTP: an empty
// schedule (zeros, no badges), a full schedule (counts match the PTO Requests and Conflicts pages), counts following
// each change made through the site (PTO submitted, approved, jobs created, edited and deleted), badges on every page
// for each reviewing role, and technicians seeing neither. The clock is fixed to Nov 3, 2026.
using System.Net;
using System.Text.RegularExpressions;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class DashboardAlertsTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private T Service<T>() where T : notnull => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private static DateOnly Day(int november) => new(2026, 11, november);

    /// <summary>The number and detail line of the dashboard card with this label.</summary>
    private static (int Count, string Detail) Card(string html, string label)
    {
        var match = Regex.Match(html, $"""data-card="{Regex.Escape(label)}"[\s\S]*?summary-count[^>]*>(\d+)<[\s\S]*?summary-detail[^>]*>([^<]*)<""");
        Assert.True(match.Success, $"No \"{label}\" card on the page.");
        return (int.Parse(match.Groups[1].Value), match.Groups[2].Value);
    }

    /// <summary>The sidebar badge text for "pto" or "conflicts", or null when no badge is shown.</summary>
    private static string? Badge(string html, string which)
    {
        var match = Regex.Match(html, $"""data-count="{which}">([^<]+)<""");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task<string> GetAsync(HttpClient client, string url) => WebUtility.HtmlDecode(await client.GetStringAsync(url));

    private static (string, string)[] JobFields(string name, DateOnly start, DateOnly end, params Guid[] technicians) =>
    [
        ("Name", name), ("Address", "1 Test Street"), ("StartDate", start.ToString("yyyy-MM-dd")), ("EndDate", end.ToString("yyyy-MM-dd")),
        ("StartTime", "08:00"), ("EndTime", "16:00"), ("Status", "Scheduled"),
        .. technicians.Select(id => ("AssignedTechnicianIds", id.ToString()))
    ];

    [Fact]
    public async Task Dashboard_EmptySchedule_ZerosAndNoBadges()
    {
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await GetAsync(lead, "/");

        Assert.Equal((0, "No jobs running today"), Card(html, "Jobs today"));
        Assert.Equal((0, "Nothing booked in the next 14 days"), Card(html, "Upcoming (14 days)"));
        Assert.Equal((0, "All caught up"), Card(html, "Pending PTO"));
        Assert.Equal((0, "No scheduling conflicts"), Card(html, "Conflicts"));
        Assert.Null(Badge(html, "pto"));
        Assert.Null(Badge(html, "conflicts"));
        Assert.Contains(">PTO Requests<", html);
        Assert.Contains(">Conflicts<", html);
    }

    [Fact]
    public async Task Dashboard_FullSchedule_CountsMatchThePtoAndConflictPages()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var sara = await Service<TechnicianService>().AddAsync("Sara", "Lee");
        var jobs = Service<JobService>();
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "Central Plaza" });
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "Harbor", StartDate = Day(19), EndDate = Day(20) });
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with { Name = "Lincoln", StartDate = Day(19), EndDate = Day(19) });
        await jobs.CreateAsync(ServiceHarness.ValidJob(sara.Id) with { Name = "Riverside", StartDate = Day(10), EndDate = Day(12) });
        var pto = Service<PtoService>();
        var approved = await pto.SubmitAsync(sara.Id, ServiceHarness.FullDays(Day(11), Day(11)));
        await pto.ReviewAsync(approved.Id, PtoStatus.Approved, "lead");
        await pto.SubmitAsync(john.Id, ServiceHarness.FullDays(Day(26), Day(27)));
        await pto.SubmitAsync(sara.Id, ServiceHarness.FullDays(Day(30), Day(30)));
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await GetAsync(lead, "/");
        var ptoPage = await GetAsync(lead, "/PtoRequests");
        var conflictsPage = await GetAsync(lead, "/Conflicts");

        Assert.Equal((1, "Central Plaza · day 2 of 5"), Card(html, "Jobs today"));
        Assert.Equal((1, "Next: Riverside, Tue, Nov 10"), Card(html, "Upcoming (14 days)"));
        Assert.Equal((2, "2 requests need review"), Card(html, "Pending PTO"));
        Assert.Equal((2, "Sara Lee booked during PTO on Wed, Nov 11 + 1 more"), Card(html, "Conflicts"));
        Assert.Equal("2", Badge(html, "pto"));
        Assert.Equal("2", Badge(html, "conflicts"));
        Assert.Contains("Pending <span class=\"badge text-bg-warning\">2</span>", ptoPage);
        Assert.Equal(2, Regex.Matches(conflictsPage, "<tr>\\s*<td>(John Smith|Sara Lee)</td>").Count);
        Assert.Equal("2", Badge(ptoPage, "pto"));
        Assert.Equal("2", Badge(conflictsPage, "conflicts"));
    }

    [Fact]
    public async Task Dashboard_ScheduleUpdatedThroughTheSite_CountsAndBadgesFollowEachChange()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        // A job today and one next week.
        await FormPoster.PostFormAsync(lead, "/Jobs/Create", "/Jobs/Create", JobFields("Today's job", Day(3), Day(3), john.Id));
        await FormPoster.PostFormAsync(lead, "/Jobs/Create", "/Jobs/Create", JobFields("Next week", Day(10), Day(11), john.Id));
        var html = await GetAsync(lead, "/");
        Assert.Equal((1, "Today's job"), Card(html, "Jobs today"));
        Assert.Equal((1, "Next: Next week, Tue, Nov 10"), Card(html, "Upcoming (14 days)"));
        Assert.Null(Badge(html, "pto"));

        // John asks for Nov 10 off: one request to review, no conflict yet (it isn't approved).
        await FormPoster.PostFormAsync(technician, "/Pto", "/Pto",
            ("Form.StartDate", "2026-11-10"), ("Form.StartTime", "00:00"), ("Form.EndDate", "2026-11-10"), ("Form.EndTime", "23:59"));
        html = await GetAsync(lead, "/");
        Assert.Equal((1, "1 request needs review"), Card(html, "Pending PTO"));
        Assert.Equal("1", Badge(html, "pto"));
        Assert.Equal(0, Card(html, "Conflicts").Count);

        // Approving it clears the PTO badge (on the page shown right after) and creates a conflict with "Next week".
        var request = Assert.Single(await Service<PtoService>().GetAllAsync());
        var afterApprove = WebUtility.HtmlDecode(await (await FormPoster.PostFormAsync(lead, "/PtoRequests", $"/PtoRequests/Approve/{request.Id}"))
            .Content.ReadAsStringAsync());
        Assert.Null(Badge(afterApprove, "pto"));
        Assert.Equal("1", Badge(afterApprove, "conflicts"));
        html = await GetAsync(lead, "/");
        Assert.Equal((0, "All caught up"), Card(html, "Pending PTO"));
        Assert.Equal((1, "John Smith booked during PTO on Tue, Nov 10"), Card(html, "Conflicts"));

        // Taking John off that job resolves the conflict.
        var nextWeek = (await Service<JobService>().GetAllAsync()).Single(j => j.Name == "Next week");
        await FormPoster.PostFormAsync(lead, $"/Jobs/Edit/{nextWeek.Id}", $"/Jobs/Edit/{nextWeek.Id}", JobFields("Next week", Day(10), Day(11)));
        html = await GetAsync(lead, "/");
        Assert.Equal((0, "No scheduling conflicts"), Card(html, "Conflicts"));
        Assert.Null(Badge(html, "conflicts"));

        // Deleting today's job empties the Jobs today card.
        var todays = (await Service<JobService>().GetAllAsync()).Single(j => j.Name == "Today's job");
        await FormPoster.PostFormAsync(lead, $"/Jobs/Delete/{todays.Id}", $"/Jobs/Delete/{todays.Id}");
        html = await GetAsync(lead, "/");
        Assert.Equal((0, "No jobs running today"), Card(html, "Jobs today"));
        Assert.Equal(1, Card(html, "Upcoming (14 days)").Count);
    }

    [Theory]
    [InlineData(AppRoles.TeamLead)]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.SuperAdmin)]
    public async Task Badges_ReviewingRoles_ShownOnEveryPage(string role)
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(Day(10), Day(10)));
        var client = await _factory.SignInAsNewUserAsync(role);

        foreach (var url in new[] { "/", "/Jobs", "/Schedule/Month", "/Technicians", "/Account/ChangePassword" })
        {
            Assert.Equal("1", Badge(await GetAsync(client, url), "pto"));
        }
    }

    [Fact]
    public async Task Badges_Technician_NoTimeOffGroupOrCounts()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(Day(10), Day(10)));
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var html = await GetAsync(technician, "/TechnicianView");

        Assert.DoesNotContain("data-count=", html);
        Assert.DoesNotContain(">PTO Requests<", html);
        Assert.DoesNotContain("summary-card", html);
    }

    [Fact]
    public async Task Dashboard_Cards_LinkToTheirPages()
    {
        var lead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = await GetAsync(lead, "/");

        Assert.Contains("data-card=\"Jobs today\" href=\"/Schedule\"", html);
        Assert.Contains("data-card=\"Upcoming (14 days)\" href=\"/Jobs/Calendar\"", html);
        Assert.Contains("data-card=\"Pending PTO\" href=\"/PtoRequests\"", html);
        Assert.Contains("data-card=\"Conflicts\" href=\"/Conflicts\"", html);
    }
}
