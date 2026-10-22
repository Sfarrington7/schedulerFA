# Fire Alarm Field Scheduler

A lightweight scheduling website for fire alarm testing teams. The goal is to give field technicians the job information and building-specific spreadsheets they need without contacting the office for routine schedule details or documents.

> **Status:** First working MVP. Every feature in the MVP scope below works locally, and the primary workflow is covered by an automated end-to-end test. Not ready for deployment; see *Safety and limitations*.

## Who uses it?

- **Team lead:** Adds technicians; creates and updates jobs; assigns technicians; sets dates, times, addresses, and instructions; adds buildings; uploads or removes building spreadsheets.
- **Technician:** Chooses their name in a prototype-only selector, reviews assigned jobs and building details, and downloads the spreadsheets needed for testing.

No login or permission system exists in the first version. The two views are for demonstrating workflows, **not access control**.

## MVP scope

1. **Team management:** Add, edit, list, and remove technicians.
2. **Jobs and assignments:** Create/edit/delete a job with a name, optional customer, address, start/end dates and times, status, notes, and assigned technicians.
3. **Buildings:** Associate multiple named buildings with a job, each with its own notes and attachments.
4. **Spreadsheet files:** Upload, list, download, and delete `.xlsx`, `.xls`, or `.csv` attachments per building. Files are transferred as-is; the app does not edit or analyze spreadsheet contents.
5. **Field schedule:** A mobile-friendly technician view with daily/weekly assignments and job/building details.
6. **Team lead overview:** List upcoming jobs, technicians, and assignments in a clear scheduling interface.

### Out of scope for version one

Authentication, role-based permissions, multi-company support, relational databases, cloud storage, notifications, editing spreadsheets in the browser, equipment/device tracking, and drag-and-drop scheduling.

## Technology

| Layer | Initial choice |
| --- | --- |
| Framework | ASP.NET Core / .NET 10 |
| Language | C# |
| UI | ASP.NET Core MVC with Razor views (no code-behind) |
| Data persistence | Local JSON files |
| Uploaded documents | Local filesystem, outside `wwwroot` |
| Testing | xUnit (`dotnet test`) |
| Deployment | Local development only, initially |

Use dependency injection and testable services so a database and authentication can be added later without redesigning the UI from scratch.

## Primary workflow

1. A team lead adds technicians John Smith and Mike Johnson.
2. The lead creates **Central Plaza Annual Fire Alarm Inspection**, scheduled **November 2–6, 2026** at **100 Main Street**.
3. The lead assigns John and Mike.
4. The lead adds **Building A**, **Building B**, and **Parking Garage**.
5. The lead uploads a separate device-testing spreadsheet to each building.
6. In **Technician View**, John selects his name, opens his assignment, selects Building A, and downloads its spreadsheet.

This workflow should work end-to-end in the MVP without a login or database.

## Solution structure

```text
schedulerFA/
├── FireAlarmScheduler.slnx           # Solution file (open this in Visual Studio)
├── CLAUDE.md / README.md
├── src/FireAlarmScheduler/           # ASP.NET Core MVC application
│   ├── Program.cs                    # Startup: registers services and storage paths
│   ├── appsettings.json              # "Storage" section: data/upload folders, max file size
│   ├── Controllers/                  # Home (dashboard), Schedule, Jobs, Buildings, Technicians, TechnicianView
│   ├── Views/                        # Razor views, one folder per controller, plus Shared/
│   ├── Models/                       # View models and form models (what views see)
│   ├── Entities/                     # Technician, Job, Building, UploadedFile, JobStatus (what's stored)
│   ├── Services/                     # Business rules, JSON repositories, local file storage
│   ├── wwwroot/                      # CSS and Bootstrap/jQuery (public static files only)
│   └── App_Data/                     # Created at runtime; ignored by Git (see below)
└── tests/
    ├── README.md                     # How to run tests and what each file covers
    └── FireAlarmScheduler.Tests/     # xUnit service tests and HTTP integration tests
```

### How the pieces connect

1. A **controller** receives the request (for example `JobsController.Create`), and calls a **service**.
2. **Services** (`TechnicianService`, `JobService`, `BuildingFileService`) enforce the rules: required fields, end date after start date, allowed file types and size. A broken rule throws `ValidationFailedException`, and the controller shows those messages on the form.
3. Services save data through **repository interfaces** (`ITechnicianRepository`, `IJobRepository`) and **`IFileStorage`**. Today these are JSON files and a local folder. To move to SQL Server later, write EF Core versions of the two repositories and register them in `Program.cs` instead; controllers and views don't change.
4. The controller maps **entities** to **view models** in `Models/` and returns a view. Views only display data.

### Where data is stored

Paths come from the `Storage` section of `appsettings.json` and resolve against the project folder:

| What | Default location |
| --- | --- |
| Technicians | `src/FireAlarmScheduler/App_Data/technicians.json` |
| Jobs, buildings, and file metadata | `src/FireAlarmScheduler/App_Data/jobs.json` |
| Uploaded spreadsheets | `src/FireAlarmScheduler/App_Data/uploads/` (stored as `<random id>.<ext>`) |

`App_Data` is outside `wwwroot`, so uploads are only reachable through the app's download links, and it is ignored by Git. To reset the app, stop it and delete `App_Data`. If a JSON file becomes invalid, the app shows an error rather than overwriting it. Fix or restore the file, or delete it to start fresh.

## Getting started

Prerequisite: the **.NET 10 SDK** (`winget install Microsoft.DotNet.SDK.10` on Windows). Check it with `dotnet --version`.

From the repository root:

```bash
dotnet build
dotnet test
dotnet run --project src/FireAlarmScheduler --launch-profile http
```

Then open **http://localhost:5242**. In Visual Studio, open `FireAlarmScheduler.slnx`, choose **FireAlarmScheduler** as the startup project, and press F5.

### Try the workflow

1. **Technicians → Add technician**: add John Smith, Mike Johnson, and Sara Lee.
2. **Dashboard → Create job**: "Central Plaza Annual Fire Alarm Inspection", 100 Main Street, Nov 2–6, 2026, and tick John and Mike.
3. On the job page, add **Building A**, **Building B**, and **Parking Garage**.
4. Open each building and upload its `.xlsx`, `.xls`, or `.csv` file.
5. Click **Technician View**, choose **John Smith**, open the job, and download Building A's file.

The **Schedule** page shows a weekly grid of technicians and their jobs.

## Coding, comments, and testing expectations

See [`CLAUDE.md`](CLAUDE.md) for the project-wide development rules. Claude should read that file and this README before editing code.

Key expectations:
- File-level summaries on every handwritten source file; concise notes above complex functions.
- Clear separation of controllers, views, models, entities, business rules, and storage code.
- Automated xUnit tests for every feature/bug fix, including failures and edge cases.
- Tests use isolated temporary data; run `dotnet build` and `dotnet test` before marking work complete.
- When the test project is created, document test suites and execution in `tests/README.md`.

## Safety and limitations

**Do not put this unauthenticated version on the public internet or populate it with real customer, building, or employee information.** Anyone who can reach it may be able to view or alter data and download files. Before field deployment, add authentication, authorization, HTTPS, secure hosting, backups, and an appropriate data/access model.

File uploads are untrusted. Validate types/sizes on the server; store them outside `wwwroot`; use generated storage names and safe download endpoints. JSON storage suits local prototyping, not a general-purpose multi-server deployment.

## Development order

1. Scaffold .NET 10 ASP.NET Core MVC app and xUnit test project; document setup.
2. Add technician management with JSON persistence and tests.
3. Add jobs, scheduling, technician assignments, and tests.
4. Add buildings and secure file upload/download functionality with tests.
5. Build technician day/week schedule and mobile-friendly views.
6. Test the complete primary workflow and fix usability issues.

## Progress and decisions

- **Current milestone:** MVP complete (steps 1–6 of the development order). Next: usability fixes from real use.
- **Decision:** Start with local JSON/filesystem storage to avoid database setup.
- **Decision:** Use a prototype view selector instead of login; never represent it as security.
- **Decision:** Use ASP.NET Core MVC instead of Blazor, with separate `Controllers/`, `Views/`, `Models/`, and `Entities/` folders and no Razor code-behind.
- **Decision:** One job record holds its buildings and file metadata (`jobs.json`); file bytes live separately under generated names.
- **Decision:** Job times are daily working hours that apply to every day from the start date to the end date.
- **Decision:** Building names must be unique within a job. Deleting a technician unassigns them from all jobs. Deleting a job or building deletes its files.
- **Decision:** The Technician View remembers the last chosen technician in a cookie for convenience. This is not authentication.
- **Limitation:** JSON storage supports one running app instance. Writes are locked within that process, but two instances sharing the same folder could overwrite each other.
- **Open before real deployment:** Accounts, permissions, shared hosting, persistent database, backups, and customer-data protections.

Keep this section current as changes are implemented.
