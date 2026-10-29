// Purpose: Tests PTO over HTTP: a technician submits and cancels requests through the My PTO form, administrators
// (and team leads) approve and revoke them and see job overlaps, the conflicts page lists double bookings and jobs during approved
// PTO, and each role can only reach the pages it should. The clock is fixed to Nov 3, 2026.
using System.Net;
using FireAlarmScheduler.Entities;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace FireAlarmScheduler.Tests.Integration;

public class PtoTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private T Service<T>() where T : notnull => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<T>();

    private Task<HttpResponseMessage> RequestPtoAsync(HttpClient client, string start, string end, string startTime = "00:00",
        string endTime = "23:59", string reason = "") =>
        FormPoster.PostFormAsync(client, "/Pto", "/Pto",
            ("Form.StartDate", start), ("Form.StartTime", startTime), ("Form.EndDate", end), ("Form.EndTime", endTime),
            ("Form.Reason", reason));

    [Theory]
    [InlineData(AppRoles.Technician, "/Pto", true)]
    [InlineData(AppRoles.Technician, "/PtoRequests", false)]
    [InlineData(AppRoles.Technician, "/Conflicts", false)]
    [InlineData(AppRoles.TeamLead, "/Pto", false)]
    [InlineData(AppRoles.TeamLead, "/PtoRequests", true)]
    [InlineData(AppRoles.TeamLead, "/Conflicts", true)]
    [InlineData(AppRoles.Admin, "/PtoRequests", true)]
    [InlineData(AppRoles.Admin, "/PtoRequests?filter=All", true)]
    [InlineData(AppRoles.Admin, "/Conflicts", true)]
    [InlineData(AppRoles.SuperAdmin, "/PtoRequests", true)]
    [InlineData(AppRoles.SuperAdmin, "/Conflicts", true)]
    public async Task Get_PtoPageForRole_AllowedOrAccessDenied(string role, string url, bool allowed)
    {
        var client = await _factory.SignInAsNewUserAsync(role, allowAutoRedirect: false);

        var response = await client.GetAsync(url);

        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.ToString());
        }
    }

    [Fact]
    public async Task RequestAndApprove_TechnicianSubmits_AdminApproves_TechnicianSeesApproved()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var submitted = await RequestPtoAsync(technician, "2026-11-20", "2026-11-23", reason: "Wedding");

        var myPage = WebUtility.HtmlDecode(await submitted.Content.ReadAsStringAsync());
        Assert.Contains("PTO request sent for approval.", myPage);
        Assert.Contains("Fri, Nov 20, 12:00 AM – Mon, Nov 23, 2026, 11:59 PM", myPage);
        Assert.Contains(">Pending<", myPage);
        var request = Assert.Single(await Service<PtoService>().GetAllAsync());
        Assert.Equal(john.Id, request.TechnicianId);

        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);
        var reviewPage = await admin.GetStringAsync("/PtoRequests");
        Assert.Contains("John Smith", reviewPage);
        Assert.Contains("Wedding", reviewPage);

        var approved = await FormPoster.PostFormAsync(admin, "/PtoRequests", $"/PtoRequests/Approve/{request.Id}");
        Assert.Contains("PTO approved.", await approved.Content.ReadAsStringAsync());
        Assert.Equal(PtoStatus.Approved, (await Service<IPtoRepository>().GetByIdAsync(request.Id))!.Status);

        var after = await technician.GetStringAsync("/Pto");
        Assert.Contains(">Approved<", after);
        Assert.DoesNotContain("Cancel request", after);
    }

    [Fact]
    public async Task Request_InvalidDates_ShowsErrorAndSavesNothing()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var response = await RequestPtoAsync(technician, "2026-11-23", "2026-11-20");

        Assert.Contains("The end must be after the start.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await Service<PtoService>().GetAllAsync());
    }

    [Fact]
    public async Task Request_ExtraTechnicianIdField_IsIgnored()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var mike = await Service<TechnicianService>().AddAsync("Mike", "Johnson");
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        await FormPoster.PostFormAsync(technician, "/Pto", "/Pto",
            ("Form.StartDate", "2026-11-20"), ("Form.StartTime", "00:00"), ("Form.EndDate", "2026-11-20"), ("Form.EndTime", "23:59"),
            ("TechnicianId", mike.Id.ToString()), ("Form.TechnicianId", mike.Id.ToString()));

        Assert.Equal(john.Id, Assert.Single(await Service<PtoService>().GetAllAsync()).TechnicianId);
    }

    [Fact]
    public async Task Request_UnlinkedTechnicianAccount_ShowsMessageAndCannotSubmit()
    {
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician);

        var page = await technician.GetStringAsync("/Pto");
        var response = await RequestPtoAsync(technician, "2026-11-20", "2026-11-20");

        Assert.Contains("isn't linked to a technician", page);
        Assert.Equal("/Account/AccessDenied", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Empty(await Service<PtoService>().GetAllAsync());
    }

    [Fact]
    public async Task Cancel_OtherTechniciansRequest_NotFound()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var mike = await Service<TechnicianService>().AddAsync("Mike", "Johnson");
        var johnsRequest = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        var mikeClient = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: mike.Id);

        var response = await FormPoster.PostFormAsync(mikeClient, "/Pto", $"/Pto/Cancel/{johnsRequest.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Single(await Service<PtoService>().GetAllAsync());
    }

    [Fact]
    public async Task Cancel_OwnPendingRequest_Removed()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var request = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var response = await FormPoster.PostFormAsync(technician, "/Pto", $"/Pto/Cancel/{request.Id}");

        Assert.Contains("PTO request cancelled.", await response.Content.ReadAsStringAsync());
        Assert.Empty(await Service<PtoService>().GetAllAsync());
    }

    [Fact]
    public async Task Approve_TechnicianPosting_IsDenied()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var request = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        var technician = await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id);

        var response = await FormPoster.PostFormAsync(technician, "/Pto", $"/PtoRequests/Approve/{request.Id}");

        Assert.Equal("/Account/AccessDenied", response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.Equal(PtoStatus.Pending, (await Service<IPtoRepository>().GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task ReviewPage_PendingRequestOverlappingJob_ShowsWarningWithJob()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        await Service<JobService>().CreateAsync(ServiceHarness.ValidJob(john.Id));
        await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 9)));
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync("/PtoRequests"));

        Assert.Contains("Scheduled during this time off:", html);
        Assert.Contains("Central Plaza Annual Fire Alarm Inspection", html);
        Assert.Contains("Thu, Nov 5 – Fri, Nov 6", html);
    }

    [Fact]
    public async Task ReviewPage_RevokeApproved_ShowsDeniedUnderAll()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var request = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        await Service<PtoService>().ReviewAsync(request.Id, PtoStatus.Approved, "someone@example.test");
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var pending = await admin.GetStringAsync("/PtoRequests");
        var revoked = await FormPoster.PostFormAsync(admin, "/PtoRequests?filter=All", $"/PtoRequests/Deny/{request.Id}?filter=All");

        Assert.Contains("No requests are waiting for approval.", pending);
        var html = await revoked.Content.ReadAsStringAsync();
        Assert.Contains("PTO denied.", html);
        Assert.Contains(">Denied<", html);
        Assert.Equal(PtoStatus.Denied, (await Service<IPtoRepository>().GetByIdAsync(request.Id))!.Status);
    }

    [Fact]
    public async Task ConflictsPage_ListsDoubleBookingAndJobDuringApprovedPto()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var mike = await Service<TechnicianService>().AddAsync("Mike", "Johnson");
        var jobs = Service<JobService>();
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id, mike.Id));
        await jobs.CreateAsync(ServiceHarness.ValidJob(john.Id) with
        {
            Name = "Riverside Hospital Test", StartDate = new DateOnly(2026, 11, 4), EndDate = new DateOnly(2026, 11, 4),
            StartTime = new TimeOnly(13, 0), EndTime = new TimeOnly(17, 0)
        });
        var pto = await Service<PtoService>().SubmitAsync(mike.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6)));
        await Service<PtoService>().ReviewAsync(pto.Id, PtoStatus.Approved, "admin@example.test");
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        var html = await admin.GetStringAsync("/Conflicts");

        var doubleBooked = html[html.IndexOf("Double-booked technicians")..html.IndexOf("Scheduled during approved PTO")];
        Assert.Contains("John Smith", doubleBooked);
        Assert.Contains("Riverside Hospital Test", doubleBooked);
        Assert.DoesNotContain("Mike Johnson", doubleBooked);
        var onPto = html[html.IndexOf("Scheduled during approved PTO")..];
        Assert.Contains("Mike Johnson", onPto);
        Assert.Contains("Fri, Nov 6", onPto);
    }

    [Fact]
    public async Task ConflictsPage_NoConflicts_SaysSo()
    {
        var admin = await _factory.SignInAsNewUserAsync(AppRoles.Admin);

        Assert.Contains("No conflicts.", await admin.GetStringAsync("/Conflicts"));
    }

    [Fact]
    public async Task Navigation_ShowsPtoLinksByRole()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var technicianNav = await (await _factory.SignInAsNewUserAsync(AppRoles.Technician, technicianId: john.Id)).GetStringAsync("/TechnicianView");
        var adminNav = await (await _factory.SignInAsNewUserAsync(AppRoles.Admin)).GetStringAsync("/Jobs");
        var teamLeadNav = await (await _factory.SignInAsNewUserAsync(AppRoles.TeamLead)).GetStringAsync("/Jobs");

        Assert.Contains(">My PTO<", technicianNav);
        Assert.DoesNotContain(">PTO Requests<", technicianNav);
        Assert.Contains(">PTO Requests<", adminNav);
        Assert.Contains(">Conflicts<", adminNav);
        Assert.DoesNotContain(">My PTO<", adminNav);
        Assert.Contains(">PTO Requests<", teamLeadNav);
        Assert.Contains(">Conflicts<", teamLeadNav);
    }

    [Fact]
    public async Task Approve_TeamLead_CanApproveAndDeny()
    {
        var john = await Service<TechnicianService>().AddAsync("John", "Smith");
        var first = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20)));
        var second = await Service<PtoService>().SubmitAsync(john.Id, ServiceHarness.FullDays(new DateOnly(2026, 11, 27), new DateOnly(2026, 11, 27)));
        var teamLead = await _factory.SignInAsNewUserAsync(AppRoles.TeamLead);

        var approved = await FormPoster.PostFormAsync(teamLead, "/PtoRequests", $"/PtoRequests/Approve/{first.Id}");
        var denied = await FormPoster.PostFormAsync(teamLead, "/PtoRequests", $"/PtoRequests/Deny/{second.Id}");

        Assert.Contains("PTO approved.", await approved.Content.ReadAsStringAsync());
        Assert.Contains("PTO denied.", await denied.Content.ReadAsStringAsync());
        var stored = Service<IPtoRepository>();
        Assert.Equal(PtoStatus.Approved, (await stored.GetByIdAsync(first.Id))!.Status);
        Assert.Equal(PtoStatus.Denied, (await stored.GetByIdAsync(second.Id))!.Status);
        Assert.StartsWith("teamlead-", (await stored.GetByIdAsync(first.Id))!.ReviewedBy);
    }
}
