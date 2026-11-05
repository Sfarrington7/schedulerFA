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

The full suite takes about two and a half minutes, mostly creating databases.

### Browser layout tests

`Visual/LayoutTests.cs` opens every page in headless **Chromium** through Microsoft.Playwright at desktop, tablet, and phone sizes. Playwright needs its own copy of Chromium. Install it once after building the tests:

```bash
dotnet build
pwsh tests/FireAlarmScheduler.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
```

(`pwsh` is PowerShell 7; on Windows the same command works in a PowerShell window.) If Chromium isn't installed, these tests are **skipped** with that instruction, and the rest of the suite still runs. Run only the layout tests with `dotnet test --filter "FullyQualifiedName~Visual"`. Each run saves a full-page screenshot of every page to `tests/FireAlarmScheduler.Tests/bin/Debug/net10.0/layout-screenshots/<desktop|tablet|phone>/` for a quick look.

Run one class or test by name:

```bash
dotnet test --filter WorkflowTests
dotnet test --filter "FullyQualifiedName~JobFilesAndChecklistTests"
```

## Data isolation

- Every test class creates its own temporary folder (`TestSupport/TempDirectory.cs`) and deletes it afterwards. If the test used a database (`FasTest_<folder id>`, see `TestSupport/TestDatabase.cs`), that is dropped too. Tests never read or write the app's real `App_Data` folder or `FireAlarmScheduler` database. Tests of pure rules (such as `JobFilesLinkTests`) don't connect to SQL Server at all.
- Service tests build the real EF Core repositories, Identity stores, and services over the test database (`TestSupport/ServiceHarness.cs`). The schema is created by the app's real migrations, so the tests also check that the migrations work. Creating a second harness on the same folder simulates an app restart. Connections use `Pooling=False` so nothing keeps the database open when it is dropped.
- Integration tests host the real app in memory with `WebApplicationFactory` (`TestSupport/AppFactory.cs`). They point the connection string at the test database and fix the clock to **Nov 3, 2026** so date-based pages are predictable.
- `AppFactory.CreateUserAsync` adds an account with a role directly; `SignInAsync` / `SignInAsNewUserAsync` sign in through the real login form and return a client holding the auth cookie. All test accounts use `AppFactory.TestPassword`.
- `TestSupport/FormPoster.cs` submits forms like a browser does, by reading the page's antiforgery token first.
- Browser layout tests (`TestSupport/VisualTestApp.cs`) run the real app on a free local port with Kestrel instead of in memory, over their own test database seeded with a November 2026 schedule (four technicians, a recurring job with a Google Drive files link and three buildings, one already checked off, a double booking, a job during approved PTO, a pending request). They sign in once as a SuperAdmin and once as a technician through the real login form and reuse those cookies. Each viewport class has its own app, database, and browser.

## What each file covers

| File | Covers |
| --- | --- |
| `Services/EfRepositoryTests.cs` | Full job graph (assignments, buildings with checklist progress, files link, dates/times, status) survives a restart; status stored as text; a failed change saves nothing; reassigning technicians replaces join rows; deleting a job cascades; deleting a technician unlinks their account and leaves buildings they checked off complete without their name; role seeding is repeatable |
| `Services/UserAdministrationServiceTests.cs` | Which roles SuperAdmins/Admins may grant; who may manage whom; validation (email, name, roles, duplicate email); Technician role needs a unique, existing technician link; role updates rotate the security stamp; the last SuperAdmin can't be demoted, locked, or deleted; no self-delete; first-run setup works only once |
| `Services/TechnicianServiceTests.cs` | Add (trimming, required names), sorting, edit, delete, and unassigning a deleted technician from jobs |
| `Services/JobServiceTests.cs` | Create/edit/delete jobs; validation (name, address, date order, time order, unknown technicians); the files link saved trimmed, changed, cleared, and an unsafe or invalid link rejected with nothing saved; editing keeps buildings; multiple buildings; duplicate building names; deleting a building removes only that one; checking a building off records the technician and time, unticking clears it, a second tick keeps the first technician, technicians not on the job (and unknown jobs/buildings) change nothing, and renaming a building or editing the job keeps its progress; daily/weekly date-range filtering and sort order; jobs for several technicians at once (each job once, none for an empty choice) |
| `Services/JobFilesLinkTests.cs` | Files link rules: any http(s) site is accepted unchanged (Google Drive, Docs, SharePoint, Dropbox, an intranet host); blank means no link; spaces trimmed; a link without a scheme gets `https://`; `javascript:`, `data:`, `vbscript:`, `file:`, `ftp:`, `mailto:`, plain words, and over-long links are refused |
| `Integration/JobFilesAndChecklistTests.cs` | Over HTTP: creating a job with a files link (shown on the job page), a link without `https://` fixed up, unsafe or invalid links rejected with nothing saved, clearing the link on edit; the technician's link opens in a new tab with `noopener noreferrer`; the checklist starts with every building to do in job order; ticking by plain form post or by the script's JSON request moves a building to Completed with the date and technician; unticking puts it back in its place; another technician, a team lead, and a post without an antiforgery token are all refused and change nothing; team leads see the checklist read-only and the progress and who completed each building on the job page; the old upload and download URLs are gone |
| `Services/ScheduleCalendarTests.cs` | Monday-based week start, including year boundaries; inclusive day ranges; month grids (6-row month starting Sunday, 4-row February starting Monday); start of month |
| `Integration/PageTests.cs` | Every main page loads for a team lead with navigation; unknown IDs (job, technician, building edit, technician job page) return 404; posts without an antiforgery token are rejected; form validation errors are shown |
| `Integration/WorkflowTests.cs` | The full README workflow over HTTP (team lead adds 3 technicians, a job with a files link, 2 assignments, 3 buildings; John signs in as a technician, sees the link, and checks Building A off; the team lead sees the progress and his name; Sara's account can't see John's job); job date validation; editing assignments and status; editing a building returns to the job; removing a building and deleting the job; weekly grid; remembered technician selection |
| `Visual/LayoutTests.cs` | Every page (32, signed out, as a SuperAdmin, and as a technician) at desktop 1366×900, tablet 820×1180, and phone 390×844: loads with no script errors, console errors, or missing files; no sideways scrolling; nothing running off the screen (except inside scrolling tables and calendars); no overlapping links, buttons, or fields; no text under 10px; no text cut off without an ellipsis; the sidebar beside the content on desktop and hidden behind a 44px menu button on narrower screens. Also: the desktop sidebar stays in view on a long scrolled page; the sign-in page's logo doesn't change when hovered; on a phone the menu opens on screen with its PTO and conflict badges (and their screen-reader text), navigates, and closes; the job form's technician dropdown fits a phone screen; on a phone, ticking a building runs the checklist script for real (moves to Completed in job order without a reload, updates the counts and progress bar, survives a reload, and unticks back), and a failed save (offline) undoes the tick and shows an error |
| `Integration/DashboardAlertsTests.cs` | Dashboard summary cards and sidebar badges: an empty schedule (zeros, empty messages, no badges); a full schedule whose counts match the PTO Requests and Conflicts pages; counts following each change made through the site (job created today and next week, PTO submitted, approved — clearing the PTO badge and creating a conflict on the very next page — technician taken off the job, job deleted); badges on every page for TeamLead, Admin, and SuperAdmin; technicians see no Time off group, badges, or cards; cards link to their pages |
| `Integration/JobsCalendarTests.cs` | Jobs page calendar: every job in the month (assigned or not) as one colored bar at the right column and length, no technician tags, a job running into the next month continuing across rows, the month's job list with color swatches and day counts, month links, default month and an empty month, the List/Calendar tabs, technicians denied |
| `Integration/MonthScheduleTests.cs` | Team lead month calendar: all technicians by default, any chosen subset (only their jobs and initials), none chosen, unknown IDs ignored, Next keeps the choice, defaults to the current month, technicians denied; technician month view shows only their own jobs with job and day links, leading days from the previous month stay off the month's job list; week view lists every day including empty ones |
| `Services/PtoServiceTests.cs` | Submitting PTO (saved as pending across a restart, today allowed, end before start, zero length, past start, long reason, unknown technician, overlap with own active request; touching, denied, and other technicians' requests allowed); cancelling (own pending only); approve/deny/revoke with reviewer recorded; pending-first sort; approved requests in a date range; deleting a technician removes their requests |
| `Services/ScheduleAlertServiceTests.cs` | The counts behind the cards and badges against the real database: empty schedule, only pending PTO counted (not approved or denied), conflicts from today on earliest first (past double bookings left out), one instance reusing its first lookup while a new one sees changes |
| `Services/ScheduleConflictServiceTests.cs` | Double bookings (shared day range, touching hours, other technicians/days, two shared technicians, completed and past jobs, overlap starting before today); jobs during PTO (affected day range, partial-day and overnight PTO, touching times, other technicians, completed jobs); only approved PTO counts on the conflicts page; job form picker conflicts (booked technician only, proposed job before or after the existing one, the edited job doesn't clash with itself, touching hours and completed jobs, approved PTO, invalid times) |
| `Services/MonthCalendarTests.cs` | Month grid job layout: lanes kept across a job's days with gaps, lanes reused after a job ends, bars joining neighboring days, a job running over the weekend into the next row; Jobs calendar bars (start column and length, split over a weekend with the same color, overlapping jobs on separate lines with different colors, the month's job list) |
| `Services/DashboardSummaryTests.cs` | Summary card wording and numbers: empty schedule, each card's link, a multi-day job's "day 2 of 5", several jobs today (earliest first, "+ 1 more"), the 14-day upcoming window edges and the next job, singular/plural pending PTO, conflict descriptions (PTO and double booking, "+ 1 more") and colors, badge text capped at 99+ |
| `Services/PtoDayTests.cs` | Splitting a PTO period into calendar days: all day, from/until a time, same-day ranges, running past midnight, and days it doesn't cover |
| `Integration/PtoCalendarTests.cs` | Approved PTO on the team lead month calendar (all technicians, and only ticked ones), the weekly grid, and a technician's own month, week, and day views; pending and denied PTO never shown |
| `Integration/PtoTests.cs` | PTO page access for each role (team leads can review); technician submits, admin approves, technician sees Approved; invalid dates; a posted technician ID is ignored; unlinked accounts can't submit; cancelling own vs. another technician's request; technicians can't approve; overlap warning on the review page; revoking an approved request; team leads approving and denying; conflicts page lists double bookings and jobs during approved PTO; nav links by role |
| `Services/RecurringJobServiceTests.cs` | Saving a job as recurring (details and buildings become the series list); Create building rows (order, blank rows ignored, duplicates rejected with nothing saved); creating from a recurring job uses the series list (with no checklist progress) or the edited rows, adding only new buildings to the list; series rows keep their saved name even if a different one is posted, and a row pointing at a removed series building is rejected; buildings added to a series job reach the list and upcoming jobs but not past, completed, or one-off jobs, without duplicates; renames follow to the list; removing from one job keeps it on the list; adding/removing on the recurring job itself; unknown recurring jobs and duplicate names rejected; saved details follow the newest job; removing a recurring job keeps its jobs |
| `Services/MigrationTests.cs` | The `AddRecurringJobBuildings` migration seeds each series' building list from its newest job, in order, ignoring older and one-off jobs |
| `Integration/RecurringJobTests.cs` | Create form building rows (saved, blank rows ignored, duplicate error keeps the rows); saving as recurring then picking it from the list; starting from a recurring job prefills details and read-only building rows, a posted rename is ignored, and a new building is added to the series; edit form saves a one-off as recurring; recurring job page adds a building (also to upcoming jobs) and removes one (list only); a building added on a series job's page appears on the recurring page; Remove; technicians denied |
| `Integration/TechnicianPickerTests.cs` | Job form technician picker: the availability endpoint (booked, on approved PTO, free; the edited job doesn't clash with itself; invalid or missing times return 400; technicians denied), Edit form conflict marks and warning, invalid hours show "Not checked", and a conflicting technician can still be saved |
| `Integration/AuthenticationTests.cs` | Anonymous requests redirect to sign-in; CSS/scripts load without sign-in; first-run setup creates a SuperAdmin and then closes; wrong password; lockout after 5 failures; external return URLs ignored; sign-out; change password |
| `Integration/AuthorizationTests.cs` | Page access for each of the four roles; technicians land on their own schedule, can't see other technicians via the URL, and see a message when not linked; Admins can't open a SuperAdmin's edit page |
| `Integration/UserManagementTests.cs` | Creating a linked technician account that can then sign in; Technician role without a link rejected; Admin can't grant SuperAdmin; weak passwords rejected; promoting a user takes effect; lock/unlock; password reset; deleting an account keeps the technician; Admin can't delete another Admin; role list and members |

## Gaps and manual checks

- The layout tests catch broken layout (overflow, overlap, tiny or cut-off text, wrong navigation), not taste: colors, spacing, and alignment still need a look. After UI changes, skim the saved `layout-screenshots`. Pages are checked as loaded; open menus and dropdowns are only checked by the dedicated tests above.
- If a test run is killed midway, leftover `FasTest_*` databases can be dropped by hand.
- The ~1-minute delay before role changes reach an already signed-in session (security-stamp validation interval) isn't tested; tests sign in again after changes.
- The technician picker's script (`wwwroot/js/technician-picker.js`: refreshing the marks when dates change, the selected-names summary, and the warning list) has no automated test; check it in a browser.
- On Linux machines with a low inotify limit (for example 128 instances in some containers), a full run can fail about two minutes in with HTTP 500s and `IOException: The configured user limit (128) on the number of inotify instances has been reached` in the logs, because each test web host watches its config files. Run with `DOTNET_USE_POLLING_FILE_WATCHER=true dotnet test`, or raise `fs.inotify.max_user_instances`.
