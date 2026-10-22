# Tests

xUnit tests for the Fire Alarm Scheduler. They run offline and finish in a few seconds.

## Running

From the repository root:

```bash
dotnet test
```

Run one class or test by name:

```bash
dotnet test --filter WorkflowTests
dotnet test --filter "FullyQualifiedName~BuildingFileServiceTests"
```

## Data isolation

- Every test class creates its own temporary folder (`TestSupport/TempDirectory.cs`) and deletes it afterwards. Tests never read or write the app's real `App_Data` folder.
- Service tests build the real repositories and services over that folder (`TestSupport/ServiceHarness.cs`). Creating a second harness on the same folder simulates an app restart.
- Integration tests host the real app in memory with `WebApplicationFactory` (`TestSupport/AppFactory.cs`). They point `Storage:*` settings at a temp folder, set a 1 KB upload limit, and fix the clock to **Nov 3, 2026** so date-based pages are predictable.
- `TestSupport/FormPoster.cs` submits forms like a browser does, by reading the page's antiforgery token first.

## What each file covers

| File | Covers |
| --- | --- |
| `Services/JsonFileStoreTests.cs` | Missing file reads as empty; save/reload across restarts; enum and date/time serialization; no leftover temp file; a corrupted file throws and is not overwritten; a failed change saves nothing; concurrent writes all kept |
| `Services/TechnicianServiceTests.cs` | Add (trimming, required names), sorting, edit, delete, and unassigning a deleted technician from jobs |
| `Services/JobServiceTests.cs` | Create/edit/delete jobs; validation (name, address, date order, time order, unknown technicians); editing keeps buildings; multiple buildings; duplicate building names; deleting jobs/buildings removes their stored files; daily/weekly date-range filtering and sort order |
| `Services/BuildingFileServiceTests.cs` | `.xlsx`/`.xls`/`.csv` only (any case); size limit including exact boundary; empty files; unique IDs and generated stored names; path-traversal and Windows-path file names; byte-for-byte download; wrong building; missing bytes on disk; delete |
| `Services/LocalFileStorageTests.cs` | Stored names with `..`, `/`, or `\` are rejected; files outside the uploads folder can't be read; no overwriting |
| `Services/ScheduleCalendarTests.cs` | Monday-based week start, including year boundaries; inclusive day ranges |
| `Integration/PageTests.cs` | Every main page loads with navigation; unknown IDs return 404; posts without an antiforgery token are rejected; form validation errors are shown |
| `Integration/WorkflowTests.cs` | The full README workflow over HTTP (3 technicians, job, 2 assignments, 3 buildings, per-building uploads, technician schedule, download); mixed valid/invalid uploads; job date validation; editing assignments and status; deleting files and jobs; weekly grid; remembered technician selection |

## Gaps and manual checks

- Views are checked through HTML content, not visual layout. After UI changes, run the app and check pages at desktop and phone widths.
- Tests don't cover running more than one app instance against the same JSON files. That isn't supported (see the main README).
