// Purpose: Tests that approved PTO appears on the schedule pages over HTTP: the team lead's month calendar
// (only for ticked technicians) and weekly grid, and the technician's own month, week, and day views.
// Pending and denied PTO never appear. The clock is fixed to Nov 3, 2026.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class PtoCalendarTests : IDisposable
{
    private readonly AppFactory _factory = new();
    private Technician _john = null!;
    private Technician _sara = null!;

    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// John: approved PTO Nov 18 (all day) and Nov 19 from 12:00 PM, plus a pending request on Nov 24.
    /// Sara: approved PTO Nov 20 and a denied request on Nov 25.
    /// </summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var technicians = scope.ServiceProvider.GetRequiredService<TechnicianService>();
        var pto = scope.ServiceProvider.GetRequiredService<PtoService>();
        _john = await technicians.AddAsync("John", "Smith");
        _sara = await technicians.AddAsync("Sara", "Lee");

        async Task AddAsync(Guid technicianId, PtoInput input, PtoStatus? decision)
        {
            var request = await pto.SubmitAsync(technicianId, input);
            if (decision is { } status)
            {
                await pto.ReviewAsync(request.Id, status, "lead@example.test");
            }
        }

        await AddAsync(_john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18)), PtoStatus.Approved);
        await AddAsync(_john.Id, new PtoInput(new DateOnly(2026, 11, 19), new TimeOnly(12, 0), new DateOnly(2026, 11, 19), new TimeOnly(23, 59), null), PtoStatus.Approved);
        await AddAsync(_john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 24)), null);
        await AddAsync(_sara.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)), PtoStatus.Approved);
        await AddAsync(_sara.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 25), new DateOnly(2026, 11, 25)), PtoStatus.Denied);
    }

    private static int Count(string html, string value) => html.Split(value).Length - 1;

    [Fact]
    public async Task TeamLeadMonth_AllTechnicians_ShowsEachApprovedPtoDayOnly()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Schedule/Month?month=2026-11-01"));

        Assert.Equal(3, Count(html, "class=\"calendar-entry pto-entry\""));
        Assert.Contains("John Smith · PTO · All day", html);
        Assert.Contains("John Smith · PTO · From 12:00 PM", html);
        Assert.Contains("Sara Lee · PTO · All day", html);
    }

    [Fact]
    public async Task TeamLeadMonth_OnlySaraTicked_ShowsOnlySarasPto()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Schedule/Month?month=2026-11-01&filtered=true&technicianIds={_sara.Id}"));

        Assert.Equal(1, Count(html, "class=\"calendar-entry pto-entry\""));
        Assert.Contains("Sara Lee · PTO", html);
        Assert.DoesNotContain("John Smith · PTO", html);
    }

    [Fact]
    public async Task TeamLeadWeek_ShowsPtoInTechnicianRows()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Schedule?week=2026-11-18"));

        Assert.Equal(3, Count(html, "schedule-chip pto-entry"));
        Assert.Contains("From 12:00 PM", html);
    }

    [Fact]
    public async Task TechnicianMonth_ShowsOwnApprovedPtoOnly()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: _john.Id);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/TechnicianView?range=Month&date=2026-11-10"));

        Assert.Equal(2, Count(html, "class=\"calendar-entry pto-entry\""));
        Assert.DoesNotContain("Sara Lee", html);
    }

    [Fact]
    public async Task TechnicianWeekAndDay_ShowApprovedPtoWithHours()
    {
        await SeedAsync();
        var client = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: _john.Id);

        var week = WebUtility.HtmlDecode(await client.GetStringAsync("/TechnicianView?range=Week&date=2026-11-18"));
        var day = WebUtility.HtmlDecode(await client.GetStringAsync("/TechnicianView?range=Day&date=2026-11-19"));
        var pendingWeek = WebUtility.HtmlDecode(await client.GetStringAsync("/TechnicianView?range=Week&date=2026-11-24"));

        Assert.Contains("<strong>Approved PTO</strong> · All day", week);
        Assert.Contains("<strong>Approved PTO</strong> · From 12:00 PM", week);
        Assert.Equal(5, Count(week, ">No jobs<"));
        Assert.Contains("<strong>Approved PTO</strong> · From 12:00 PM", day);
        Assert.DoesNotContain("Approved PTO", pendingWeek);
    }
}
