# Tests

xUnit tests for the Fire Alarm Scheduler. They run offline and finish in a few seconds.

## Running

Most tests need a **SQL Server** (each test creates and drops its own `FasTest_*` database). By default they connect to `localhost` with Windows authentication. To use another server or SQL authentication, set `FAS_TEST_SQLSERVER` to a connection string **without** a database name:

```bash
# PowerShell: $env:FAS_TEST_SQLSERVER = "Server=localhost;User Id=sa;Password=<password>;TrustServerCertificate=True"
export FAS_TEST_SQLSERVER="Server=localhost;User Id=sa;Password=<password>;TrustServerCertificate=True"
```

The login needs permission to create and drop databases. From the repository root:

```bash
dotnet test
```

The full suite takes about a minute and a half, mostly creating databases.

Run one class or test by name:

```bash
dotnet test --filter WorkflowTests
dotnet test --filter "FullyQualifiedName~BuildingFileServiceTests"
```

## Data isolation

- Every test class creates its own temporary folder (`TestSupport/TempDirectory.cs`) and deletes it afterwards. If the test used a database (`FasTest_<folder id>`, see `TestSupport/TestDatabase.cs`), that is dropped too. Tests never read or write the app's real `App_Data` folder or `FireAlarmScheduler` database. File-only tests don't connect to SQL Server at all.
- Service tests build the real EF Core repositories, Identity stores, and services over the test database (`TestSupport/ServiceHarness.cs`). The schema is created by the app's real migrations, so the tests also check that the migrations work. Creating a second harness on the same folder simulates an app restart. Connections use `Pooling=False` so nothing keeps the database open when it is dropped.
- Integration tests host the real app in memory with `WebApplicationFactory` (`TestSupport/AppFactory.cs`). They point the connection string at the test database and `Storage:UploadsDirectory` at the temp folder, set a 1 KB upload limit, and fix the clock to **Nov 3, 2026** so date-based pages are predictable.
- `AppFactory.CreateUserAsync` adds an account with a role directly; `SignInAsync` / `SignInAsNewUserAsync` sign in through the real login form and return a client holding the auth cookie. All test accounts use `AppFactory.TestPassword`.
- `TestSupport/FormPoster.cs` submits forms like a browser does, by reading the page's antiforgery token first.

## What each file covers

| File | Covers |
| --- | --- |
| `Services/EfRepositoryTests.cs` | Full job graph (assignments, buildings, files, dates/times, status) survives a restart; status stored as text; a failed change saves nothing; reassigning technicians replaces join rows; deleting a job cascades; deleting a technician unlinks their account; role seeding is repeatable |
| `Services/UserAdministrationServiceTests.cs` | Which roles SuperAdmins/Admins may grant; who may manage whom; validation (email, name, roles, duplicate email); Technician role needs a unique, existing technician link; role updates rotate the security stamp; the last SuperAdmin can't be demoted, locked, or deleted; no self-delete; first-run setup works only once |
| `Services/TechnicianServiceTests.cs` | Add (trimming, required names), sorting, edit, delete, and unassigning a deleted technician from jobs |
| `Services/JobServiceTests.cs` | Create/edit/delete jobs; validation (name, address, date order, time order, unknown technicians); editing keeps buildings; multiple buildings; duplicate building names; deleting jobs/buildings removes their stored files; daily/weekly date-range filtering and sort order |
| `Services/BuildingFileServiceTests.cs` | `.xlsx`/`.xls`/`.csv` only (any case); size limit including exact boundary; empty files; unique IDs and generated stored names; path-traversal and Windows-path file names; byte-for-byte download; wrong building; missing bytes on disk; delete |
| `Services/LocalFileStorageTests.cs` | Stored names with `..`, `/`, or `\` are rejected; files outside the uploads folder can't be read; no overwriting |
| `Services/ScheduleCalendarTests.cs` | Monday-based week start, including year boundaries; inclusive day ranges |
| `Integration/PageTests.cs` | Every main page loads for a team lead with navigation; unknown IDs return 404; posts without an antiforgery token are rejected; form validation errors are shown |
| `Integration/WorkflowTests.cs` | The full README workflow over HTTP (team lead adds 3 technicians, job, 2 assignments, 3 buildings, per-building uploads; John signs in as a technician and downloads; Sara's account can't see or download John's job); mixed valid/invalid uploads; job date validation; editing assignments and status; deleting files and jobs; weekly grid; remembered technician selection |
| `Integration/AuthenticationTests.cs` | Anonymous requests redirect to sign-in; CSS/scripts load without sign-in; first-run setup creates a SuperAdmin and then closes; wrong password; lockout after 5 failures; external return URLs ignored; sign-out; change password |
| `Integration/AuthorizationTests.cs` | Page access for each of the four roles; technicians land on their own schedule, can't see other technicians via the URL, and see a message when not linked; Admins can't open a SuperAdmin's edit page |
| `Integration/UserManagementTests.cs` | Creating a linked technician account that can then sign in; Technician role without a link rejected; Admin can't grant SuperAdmin; weak passwords rejected; promoting a user takes effect; lock/unlock; password reset; deleting an account keeps the technician; Admin can't delete another Admin; role list and members |

## Gaps and manual checks

- Views are checked through HTML content, not visual layout. After UI changes, run the app and check pages at desktop and phone widths.
- If a test run is killed midway, leftover `FasTest_*` databases can be dropped by hand.
- The ~1-minute delay before role changes reach an already signed-in session (security-stamp validation interval) isn't tested; tests sign in again after changes.
