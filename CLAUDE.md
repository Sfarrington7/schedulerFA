# Claude Development Instructions — Fire Alarm Scheduler

## Role and priorities
Act as a senior .NET engineer and pragmatic code reviewer. Produce maintainable, testable, understandable software, not just code that compiles. Optimize for a small working MVP and clear incremental changes. Prefer standard .NET features over new libraries or unnecessary abstractions.

## Mandatory start-of-task routine
1. Read this file and `README.md` before doing project work. Inspect the relevant existing code, tests, and configuration before proposing changes; never assume an uninspected file's contents.
2. Identify the smallest change that meets the request. For nontrivial work, briefly outline the approach and affected files before editing.
3. Follow existing conventions unless they violate these instructions. Do not rewrite unrelated code, change frameworks, or add dependencies without a specific reason.
4. If requirements are ambiguous but not blocking, choose a simple default and explain it. Ask when the decision would materially affect data safety or architecture.

## MVP technology and scope
- C# and ASP.NET Core MVC on .NET 10, with Razor views (`.cshtml`). No Blazor and no Razor Pages.
- Local JSON files for prototype metadata; local filesystem storage for uploaded spreadsheets.
- One team lead/team, simulated technician selection, no accounts or authentication, no SQL database.
- Team management; jobs with dates/times, locations, assigned technicians and multiple buildings; per-building Excel uploads/downloads; mobile-friendly technician schedules.
- Do not introduce React, a separate API/frontend, third-party cloud services, notifications, or complicated patterns unless requested.
- **Never treat view switching as security.** This unauthenticated prototype is for local development/demo use only, with synthetic data.

## Architecture and folder structure (MVC, no code-behind)
- **Do not put controller logic in Razor code-behind.** Do not create Razor Pages `PageModel` classes (`*.cshtml.cs`), and do not put request handling, data access, or business logic in `@functions` / `@code` blocks inside views.
- Views are for presentation only: a view receives a model from its controller and renders it. Keep Razor logic to display concerns (loops, show/hide conditionals, formatting).
- Controllers stay thin: handle the request, call services, map results to view models, and return a view or file result.

Keep these concerns in separate folders inside `src/FireAlarmScheduler/`:

```text
Controllers/   MVC controllers, one per feature area (e.g. JobsController.cs)
Views/         Razor views (.cshtml), in a subfolder per controller (e.g. Views/Jobs/Index.cshtml)
  Shared/      Layouts, partials, _ViewImports.cshtml, _ViewStart.cshtml
Models/        View models and input/form models passed between controllers and views
Entities/      Persisted domain data (Technician, Job, Building, UploadedFile)
Services/      Validation, scheduling rules, JSON persistence, and file storage
```

- **Entities** represent stored data. Do not pass entities directly to views; map them to a view model in `Models/` first.
- **Models** shape data for a particular view or form, including validation attributes.
- Name views after their action: `Views/Jobs/Index.cshtml` for `JobsController.Index()`.
- Namespaces follow the folders: `FireAlarmScheduler.Controllers`, `FireAlarmScheduler.Models`, `FireAlarmScheduler.Entities`, `FireAlarmScheduler.Services`.

## Code design standards
- Use clear C# naming conventions, nullable reference types, strongly typed models, dependency injection, and async I/O where appropriate.
- Keep views focused on presentation and controllers focused on request handling. Put validation, scheduling rules, persistence, and file operations in testable services.
- Use interfaces at genuine replaceable boundaries (e.g., persistence/file storage), not for every class.
- Inject/configure storage locations; never hard-code user-specific absolute paths.
- Handle invalid data and I/O failures explicitly, without swallowing exceptions or silently corrupting persisted data.
- Avoid premature generalization, duplication, long methods, magic values, and excessive comments.
- Preserve existing behavior unless a requested change intentionally alters it.

## Required documentation in source files
- Every newly created or substantially changed **handwritten source file** (`.cs`, `.cshtml`, `.css`, `.js`, etc.) must begin with a concise comment explaining **what that file does** and its main responsibility. Use the language's native comment syntax; exclude generated/vendor files and data/config files where comments are invalid.
- Keep the file summary accurate and short (normally 1–3 lines). Update it when the file's responsibilities change. Do not write boilerplate that merely repeats the filename.
- Before complex or non-obvious functions, add a short comment or C# XML documentation explaining **what the function accomplishes** and the important constraints, side effects, or edge cases. Explain *why* if not obvious. Usually 1–4 lines is sufficient.
- Give public methods XML `<summary>` documentation when their contract is not clear from their name and parameters. Do not comment trivial getters, self-explanatory functions, or every line of code.
- If logic is too difficult to explain concisely, simplify or split it first.

Example C# file header:
```csharp
// Purpose: Manages scheduled jobs and their technician assignments.
// Responsibility: Validates updates and delegates persistence to the job repository.
```

Example complex-method documentation:
```csharp
/// <summary>
/// Saves an uploaded spreadsheet using a generated storage name and records its
/// building association. Rejects disallowed extensions and oversized uploads.
/// </summary>
```

For `.cshtml` views use `@* ... *@` at the top. For stylesheets use `/* ... */`.

## Testing requirements (mandatory)
- Use **xUnit** in a separate .NET test project (suggested name: `FireAlarmScheduler.Tests`). Add appropriate test dependencies only when needed.
- Every new feature, bug fix, or meaningful business rule must include automated tests covering expected behavior and at least one important failure/edge case. Prefer tests of real services over tests of implementation details.
- Name tests `MethodOrFeature_Scenario_ExpectedResult`. Arrange / Act / Assert is preferred.
- Every test source file must have a file-level summary. Document non-obvious test setup, fixtures, and scenarios briefly; test names should explain straightforward cases without lengthy comments.
- Keep tests deterministic, independent, fast, and offline. Use temporary folders and disposable test data; **never** write into real project data or depend on test execution order.
- Use mocks/fakes only for external boundaries where they help. Use integration tests for file upload/download routes and persistence when possible. Test controller routes end-to-end with `WebApplicationFactory` rather than testing view markup.
- Add or update a short `tests/README.md` when the test project is created. Describe how to run tests, what each test file covers, test fixtures/data isolation, and any gaps or manual UI checks.
- Never delete, weaken, skip, or rewrite an existing failing test solely to make the suite green. Fix the underlying defect or explain the failure.

Important test scenarios as features are implemented:
- Technician add/edit/delete and selection; job creation/update and assignment; correct daily/weekly filtering; multi-building associations.
- JSON save/load across application restarts, validation failures, and handling corrupted or missing files without silently discarding data.
- Excel/CSV extension restrictions, max upload size, generated safe storage names, unique file IDs, correct building association, byte-for-byte downloads, and deleted/missing files.
- Malicious filenames/path traversal and attempts to access unexpected local files.

## File handling and data safety
- Store uploaded files **outside `wwwroot`**; expose downloads through controlled application handlers rather than public static file URLs.
- Permit only `.xlsx`, `.xls`, and `.csv` for the MVP. Validate extension and file-size limits on the server; do not trust client-provided filenames, paths, MIME types, or IDs.
- Generate a unique stored filename and preserve the original name only as metadata/display/download name. Do not overwrite unrelated files.
- Treat uploads as untrusted content; never execute or automatically parse them. Stream uploads/downloads when practical and dispose streams correctly.
- Make JSON writes safely (temporary file and atomic replacement when supported) and guard against concurrent updates in the single-server prototype.
- Ignore real data, upload directories, secrets, and build artifacts in source control. Never commit customer records, production spreadsheets, or credentials.

## Verification and definition of done
For each completed coding task:
1. Check the affected code and tests for correctness and consistency.
2. Run `dotnet build` and `dotnet test` from the solution/repository root (or the appropriate project paths) when the environment allows. Run a relevant manual UI flow for UI changes when possible.
3. Update `README.md` when behavior, setup, status, or architecture changes; update `tests/README.md` when tests change materially.
4. Report: what changed, files affected, tests added, commands actually run and their outcomes, and any unverified behavior or known limitations.
5. Do **not** claim code was tested or works unless that verification actually occurred. If commands cannot run, explain why and supply exact commands for the user to run.

## Scope control and collaboration
- Build one complete, demonstrable feature at a time: (1) team, (2) jobs/assignments, (3) buildings/files, (4) technician schedule, (5) polish.
- Before large architectural changes, explain the tradeoff and get approval.
- Keep a concise record of material design decisions and open limitations in `README.md`; do not produce unnecessary planning documents.
- Prefer a small reviewed solution over a large unverified implementation. When a task is finished, suggest the next smallest useful step.
