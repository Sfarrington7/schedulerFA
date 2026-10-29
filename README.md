# Fire Alarm Field Scheduler

A lightweight scheduling website for fire alarm testing teams. The goal is to give field technicians the job information and building-specific spreadsheets they need without contacting the office for routine schedule details or documents.

> **Status:** First working MVP. Every feature in the MVP scope below works locally, and the primary workflow is covered by an automated end-to-end test. Not ready for deployment; see *Safety and limitations*.

## Who uses it?

Everyone signs in with an account (ASP.NET Core Identity). Each account has one or more of four fixed roles:

| Role | Can do |
| --- | --- |
| **SuperAdmin** | Everything an Admin can do, including creating and managing Admin and SuperAdmin accounts. |
| **Admin** | Manage TeamLead and Technician accounts, plus everything a team lead can do. |
| **TeamLead** | Add technicians; create and update jobs; assign technicians; add buildings; upload or remove building spreadsheets; see every technician's schedule; approve, deny, or revoke PTO; review scheduling conflicts. |
| **Technician** | See their own daily, weekly, or monthly schedule and download spreadsheets for jobs they are assigned to. Request PTO and cancel requests that are still pending. A technician account is linked to one technician record. |

## MVP scope

1. **Team management:** Add, edit, list, and remove technicians.
2. **Accounts and roles:** Sign-in, first-run SuperAdmin setup, and Users/Roles management screens (create, edit roles, link a technician, reset password, lock/unlock, delete).
3. **Jobs and assignments:** Create/edit/delete a job with a name, optional customer, address, start/end dates and times, status, notes, and assigned technicians.
4. **Buildings:** Associate multiple named buildings with a job, each with its own notes and attachments.
5. **Spreadsheet files:** Upload, list, download, and delete `.xlsx`, `.xls`, or `.csv` attachments per building. Files are transferred as-is; the app does not edit or analyze spreadsheet contents.
6. **Field schedule:** A mobile-friendly technician view with day, week (every day listed top to bottom), and month (calendar) views, plus job/building details.
7. **Team lead overview:** List upcoming jobs, technicians, and assignments in a clear scheduling interface.
8. **PTO:** Technicians request time off (start/end date and time, optional reason) on **My PTO**. Team leads and admins approve, deny, or revoke requests on **PTO Requests**, which warns when a request overlaps the technician's jobs. **Conflicts** lists double-booked technicians and technicians scheduled during approved PTO.

### Out of scope for version one

Self-service registration, password-reset emails, two-factor sign-in, multi-company support, cloud storage, notifications (including PTO decision emails), PTO balances/accrual, editing spreadsheets in the browser, equipment/device tracking, and drag-and-drop scheduling.

## Technology

| Layer | Initial choice |
| --- | --- |
| Framework | ASP.NET Core / .NET 10 |
| Language | C# |
| UI | ASP.NET Core MVC with Razor views (no code-behind) |
| Data persistence | EF Core 10 with SQL Server on `localhost` (database `FireAlarmScheduler`), schema managed by migrations |
| Accounts and roles | ASP.NET Core Identity (MVC screens, no Razor Pages UI) |
| Uploaded documents | Local filesystem, outside `wwwroot` |
| Testing | xUnit (`dotnet test`) |
| Deployment | Local development only, initially |

Services depend on repository interfaces, so the database provider can change (for example to SQL Server) without touching controllers or views.

## Primary workflow

1. A team lead adds technicians John Smith and Mike Johnson.
2. The lead creates **Central Plaza Annual Fire Alarm Inspection**, scheduled **November 2–6, 2026** at **100 Main Street**.
3. The lead assigns John and Mike.
4. The lead adds **Building A**, **Building B**, and **Parking Garage**.
5. The lead uploads a separate device-testing spreadsheet to each building.
6. John signs in with his Technician account, sees the job on **My Schedule**, opens it, selects Building A, and downloads its spreadsheet.

The automated workflow test (`WorkflowTests`) runs these steps over HTTP with real sign-ins.

## Solution structure

```text
schedulerFA/
├── FireAlarmScheduler.slnx           # Solution file (open this in Visual Studio)
├── CLAUDE.md / README.md
├── src/FireAlarmScheduler/           # ASP.NET Core MVC application
│   ├── Program.cs                    # Startup: database, Identity, authorization policies, services
│   ├── appsettings.json              # Connection string; "Storage" section: upload folder, max file size
│   ├── Controllers/                  # Home, Schedule, Jobs, Buildings, Technicians, TechnicianView, Account, Users, Roles
│   ├── Views/                        # Razor views, one folder per controller, plus Shared/
│   ├── Models/                       # View models and form models (what views see)
│   ├── Entities/                     # Technician, Job, JobAssignment, Building, UploadedFile, ApplicationUser (SQL tables)
│   ├── Data/                         # AppDbContext, startup migration/role seeding, Migrations/
│   ├── Services/                     # Business rules, EF Core repositories, account rules, local file storage
│   ├── wwwroot/                      # CSS and Bootstrap/jQuery (public static files only)
│   └── App_Data/                     # Created at runtime; ignored by Git (see below)
└── tests/
    ├── README.md                     # How to run tests and what each file covers
    └── FireAlarmScheduler.Tests/     # xUnit service tests and HTTP integration tests
```

### How the pieces connect

1. A **controller** receives the request (for example `JobsController.Create`), and calls a **service**.
2. **Services** (`TechnicianService`, `JobService`, `BuildingFileService`, `UserAdministrationService`) enforce the rules: required fields, end date after start date, allowed file types and size. A broken rule throws `ValidationFailedException`, and the controller shows those messages on the form.
3. Services save data through **repository interfaces** (`ITechnicianRepository`, `IJobRepository`), implemented with EF Core (`EfTechnicianRepository`, `EfJobRepository`), and **`IFileStorage`** (a local folder). Accounts and roles go through Identity's `UserManager`.
4. **Authorization** is declared on controllers with three policies from `Services/AppRoles.cs`: `ManageSchedule` (TeamLead, Admin, SuperAdmin), `ManageUsers` (Admin, SuperAdmin), and `ViewSchedule` (any role). Every other page requires sign-in by default.
5. The controller maps **entities** to **view models** in `Models/` and returns a view. Views only display data.

### Where data is stored

| What | Default location |
| --- | --- |
| All records (technicians, jobs, assignments, buildings, file metadata, users, roles) | SQL Server database `FireAlarmScheduler` on `localhost` (`ConnectionStrings:DefaultConnection`) |
| Uploaded spreadsheets | `src/FireAlarmScheduler/App_Data/uploads/` (stored as `<random id>.<ext>`; `Storage:UploadsDirectory`) |

The upload path resolves against the project folder. `App_Data` is outside `wwwroot`, so uploads are only reachable through the app's download links, and it is ignored by Git. On startup the app creates the database if needed, applies any pending EF Core migrations, and creates the four roles if they are missing. To reset the app, stop it, drop the `FireAlarmScheduler` database, and delete `App_Data/uploads`.

Tables: `Technicians`, `Jobs`, `JobAssignments` (job ↔ technician), `Buildings`, `UploadedFiles`, plus the Identity tables (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, …). Deleting a job cascades to its assignments, buildings, and file records; deleting a technician removes their assignments and unlinks their account.

**Older JSON data is not imported.** Earlier builds stored data in `App_Data/technicians.json` and `jobs.json`. The app no longer reads those files and leaves them untouched.

### Changing the database schema

After changing an entity or `Data/AppDbContext.cs`, add a migration (one-time tool install: `dotnet tool install --global dotnet-ef`):

```bash
dotnet ef migrations add <Name> --project src/FireAlarmScheduler --output-dir Data/Migrations
```

The app applies it at the next start.

## Getting started

Prerequisites:

- The **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10` on Windows). Check it with `dotnet --version`.
- **SQL Server running on `localhost`** (default instance, port 1433), for example SQL Server 2022 Developer or Express. The login you run the app as needs permission to create a database.

The default connection string uses Windows authentication:

```text
Server=localhost;Database=FireAlarmScheduler;Trusted_Connection=True;TrustServerCertificate=True
```

To use a named instance (for example `localhost\SQLEXPRESS`) or SQL authentication, override it with user secrets rather than editing `appsettings.json`, so no password is committed:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=FireAlarmScheduler;User Id=<user>;Password=<password>;TrustServerCertificate=True" --project src/FireAlarmScheduler
```

`TrustServerCertificate=True` accepts the self-signed certificate a local SQL Server uses; don't use it for a remote server.

From the repository root:

```bash
dotnet build
dotnet test
dotnet run --project src/FireAlarmScheduler --launch-profile http
```

Then open **http://localhost:5242**. In Visual Studio, open `FireAlarmScheduler.slnx`, choose **FireAlarmScheduler** as the startup project, and press F5.

### First run

With no accounts in the database, the app opens **First-time setup**. Create the first **SuperAdmin** there; the page closes for good once any account exists. Add everyone else from **Users**. Passwords need at least 8 characters with upper- and lower-case letters, a digit, and a symbol.

### Try the workflow

1. Signed in as the SuperAdmin, go to **Technicians → Add technician**: add John Smith, Mike Johnson, and Sara Lee.
2. **Users → Add user**: create `john@example.test` with the **Technician** role, linked to John Smith.
3. **Dashboard → Create job**: "Central Plaza Annual Fire Alarm Inspection", 100 Main Street, Nov 2–6, 2026, and tick John and Mike.
4. On the job page, add **Building A**, **Building B**, and **Parking Garage**.
5. Open each building and upload its `.xlsx`, `.xls`, or `.csv` file.
6. **Sign out**, sign in as John, open the job from **My Schedule**, and download Building A's file.

The **Schedule** page has two tabs. **Week** is a grid of technicians and their jobs by day. **Month** is a calendar where you tick which technicians to show (one, several, or all); each technician has a color, and their initials appear on their jobs. Technicians get a **Month** button on their schedule too, showing a calendar with a list of that month's jobs under it.

## Coding, comments, and testing expectations

See [`CLAUDE.md`](CLAUDE.md) for the project-wide development rules. Claude should read that file and this README before editing code.

Key expectations:
- File-level summaries on every handwritten source file; concise notes above complex functions.
- Clear separation of controllers, views, models, entities, business rules, and storage code.
- Automated xUnit tests for every feature/bug fix, including failures and edge cases.
- Tests use isolated temporary data; run `dotnet build` and `dotnet test` before marking work complete.
- When the test project is created, document test suites and execution in `tests/README.md`.

## Safety and limitations

**Do not put this version on the public internet or populate it with real customer, building, or employee information.** It now has sign-in and role-based authorization, but it has not had a security review, and real deployment still needs HTTPS-only hosting, a production database with backups, secret management, and email-based password recovery.

- Until the first SuperAdmin is created, anyone who can reach the app can open the setup page and become SuperAdmin. Create that account immediately after the first start.
- Technician accounts can only see their own schedule and download files for jobs they are assigned to; this is enforced on the server, not just hidden in the UI.
- Role changes, locks, and deletions take effect for signed-in users within about a minute (security-stamp validation).
- File uploads are untrusted. Types and sizes are validated on the server; files are stored outside `wwwroot` under generated names and served only through the download action.

## Development order

1. Scaffold .NET 10 ASP.NET Core MVC app and xUnit test project; document setup.
2. Add technician management with JSON persistence and tests (later moved to EF Core).
3. Add jobs, scheduling, technician assignments, and tests.
4. Add buildings and secure file upload/download functionality with tests.
5. Build technician day/week schedule and mobile-friendly views.
6. Test the complete primary workflow and fix usability issues.
7. Replace JSON storage with EF Core (now SQL Server on localhost) and add Identity sign-in, four roles, and user/role management.

## Progress and decisions

- **Current milestone:** Steps 1–7 complete. Next: usability fixes from real use.
- **Decision (PTO):** A request is one continuous period from a start date/time to an end date/time; whole days default to 12:00 AM–11:59 PM. Requests can't start before today or overlap the technician's own pending or approved requests. Technicians can cancel only pending requests (cancelling deletes them); admins can approve, deny, or revoke (deny an approved request). Team leads, Admins, and SuperAdmins review PTO and see the Conflicts page. Approved PTO does not block assigning the technician to a job; it shows up on the Conflicts page instead.
- **Decision (PTO on calendars):** Approved PTO shows as a striped "PTO" block on the team lead's Week grid and Month calendar (only for ticked technicians, with their colored initials) and on a technician's Day, Week, and Month views. Partial days show their hours ("From 12:00 PM", "Until 10:30 AM", or a range); time off that runs to 11:59 PM or past midnight counts as the rest of the day. Pending and denied requests are never shown on calendars.
- **Decision (conflicts):** A technician is double-booked when two of their jobs share a day and their daily hours overlap (hours that only touch, like 8–12 and 12–4, are fine). A job conflicts with PTO when its hours on any day overlap the PTO period. Completed jobs and days before today are ignored; pending PTO appears as a warning on PTO Requests, not on the Conflicts page.
- **Decision:** Store all records in SQL Server tables through EF Core, using a local SQL Server (`localhost`) for development. Tests use throwaway databases on a SQL Server too, so they run against the same provider and migrations as the app.
- **Decision:** Use ASP.NET Core Identity with four fixed roles (SuperAdmin, Admin, TeamLead, Technician), seeded at startup. Roles are not created or deleted in the UI. Identity screens are hand-written MVC controllers and views, because the stock Identity UI is built on Razor Pages.
- **Decision:** Admins manage only TeamLead and Technician accounts; only SuperAdmins can grant or change Admin/SuperAdmin. The last active SuperAdmin can't be demoted, locked, or deleted, and no one can delete or lock their own account.
- **Decision:** The first SuperAdmin is created through a setup page that only works while the database has no accounts, so no default password is committed to the repository.
- **Decision:** Each technician account links to at most one technician record; the Technician role requires a link.
- **Decision:** Use ASP.NET Core MVC instead of Blazor, with separate `Controllers/`, `Views/`, `Models/`, and `Entities/` folders and no Razor code-behind.
- **Decision:** Buildings keep the order they were added (`SortOrder`); file bytes live outside the database under generated names.
- **Decision:** Job times are daily working hours that apply to every day from the start date to the end date.
- **Decision:** Building names must be unique within a job. Deleting a technician unassigns them from all jobs. Deleting a job or building deletes its files.
- **Decision:** Month calendars run Monday–Sunday and include the days from neighboring months that fill the first and last rows. The team lead's month view shows every technician until the picker is used; the choice is kept in the page address (so Previous/Next keep it), not saved per user. Technician colors follow the technician list order, so a technician's color can change when technicians are added or removed.
- **Decision:** Team leads and administrators can pick any technician in the Technician View (the choice is remembered in a cookie); technician accounts always see their own.
- **Limitation:** Two people editing the same job at the same moment: the last save wins (no optimistic-concurrency check). Building-name uniqueness is checked in code, not by a database constraint.
- **Open before real deployment:** HTTPS-only hosting, production database and backups, email confirmation and password-reset emails, audit logging, and customer-data protections.

Keep this section current as changes are implemented.
