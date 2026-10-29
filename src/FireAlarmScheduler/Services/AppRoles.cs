// Purpose: The four fixed Identity roles and the authorization policies built from them.
// Roles are seeded at startup; they are not created or deleted through the UI.
namespace FireAlarmScheduler.Services;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";
    public const string TeamLead = "TeamLead";
    public const string Technician = "Technician";

    /// <summary>All roles, most privileged first.</summary>
    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, TeamLead, Technician];

    public static string Describe(string role) => role switch
    {
        SuperAdmin => "Full access, including managing Admin and SuperAdmin accounts.",
        Admin => "Manages TeamLead and Technician accounts, approves PTO, and reviews scheduling conflicts, plus everything a team lead can do.",
        TeamLead => "Manages technicians, jobs, buildings, files, and the team schedule.",
        Technician => "Sees their own schedule, downloads files for jobs they are assigned to, and requests PTO.",
        _ => ""
    };
}

public static class AppPolicies
{
    /// <summary>Team lead pages: dashboard, schedule grid, jobs, buildings, files, and technicians.</summary>
    public const string ManageSchedule = nameof(ManageSchedule);

    /// <summary>User and role management screens.</summary>
    public const string ManageUsers = nameof(ManageUsers);

    /// <summary>The technician schedule pages and file downloads; any role may use them.</summary>
    public const string ViewSchedule = nameof(ViewSchedule);

    /// <summary>Approving PTO and reviewing scheduling conflicts.</summary>
    public const string ManagePto = nameof(ManagePto);

    /// <summary>Requesting PTO for the technician linked to the signed-in account.</summary>
    public const string RequestPto = nameof(RequestPto);

    public static readonly string[] ManageScheduleRoles = [AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.TeamLead];
    public static readonly string[] ManageUsersRoles = [AppRoles.SuperAdmin, AppRoles.Admin];
    public static readonly string[] ManagePtoRoles = [AppRoles.SuperAdmin, AppRoles.Admin];
}
