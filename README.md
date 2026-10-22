# Fire Alarm Field Scheduler

A lightweight scheduling website for fire alarm testing teams. The goal is to give field technicians the job information and building-specific spreadsheets they need without contacting the office for routine schedule details or documents.

> **Status:** MVP specification / initial development. Features below are intended functionality, not claims that they have already been implemented. Update this section as the project progresses.

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

## Suggested solution structure

The actual directories may change during initial setup; document the real layout after scaffolding.

```text
FireAlarmScheduler/
├── CLAUDE.md                 # AI contributor instructions
├── README.md                 # Project goals and setup
├── src/
│   └── FireAlarmScheduler/   # ASP.NET Core MVC application
│       ├── Controllers/      # Request handling, one controller per feature
│       ├── Views/            # Razor views, a subfolder per controller + Shared/
│       ├── Models/           # View models and form/input models
│       ├── Entities/         # Technician, Job, Building, UploadedFile
│       ├── Services/         # Scheduling, persistence, and document operations
│       └── ...
└── tests/
    ├── README.md            # Test instructions and coverage map
    └── FireAlarmScheduler.Tests/
```

Persisted JSON data and uploaded documents must be stored outside publicly served assets, in configurable local storage directories excluded from Git. Avoid placing real business records in the repository.

## Getting started (once scaffolded)

Prerequisite: Install the **.NET 10 SDK**.

From the repository root, after Claude creates the solution and projects:

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/FireAlarmScheduler
```

Open the local address shown by `dotnet run`. If the generated project uses different folders, update these commands rather than creating duplicate projects.

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

- **Current milestone:** Planning and project setup.
- **Decision:** Start with local JSON/filesystem storage to avoid database setup.
- **Decision:** Use a prototype view selector instead of login; never represent it as security.
- **Decision:** Use ASP.NET Core MVC instead of Blazor, with separate `Controllers/`, `Views/`, `Models/`, and `Entities/` folders and no Razor code-behind.
- **Open before real deployment:** Accounts, permissions, shared hosting, persistent database, backups, and customer-data protections.

Keep this section current as changes are implemented.
