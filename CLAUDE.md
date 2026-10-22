# schedulerFA

## Architecture: ASP.NET Core MVC (no code-behind)

This project uses the MVC pattern with controllers, views, models and entities kept apart.

- **Do not put controller logic in Razor code-behind.** Do not create Razor Pages
  `PageModel` classes (`*.cshtml.cs`), and do not put request handling, data access or
  business logic in `@functions` / `@code` blocks inside views.
- Views are for presentation only. A view receives a model from its controller and
  renders it. Keep Razor logic to display concerns: loops, conditionals for showing or
  hiding markup, formatting.
- All request handling lives in controller classes under `Controllers/`.

## Folder structure

```
Controllers/   MVC controllers (e.g. ScheduleController.cs), one per feature area
Views/         Razor views (.cshtml), in a subfolder per controller
  Shared/      layouts, partials, _ViewImports.cshtml, _ViewStart.cshtml
Models/        view models and input models passed between controllers and views
Entities/      domain/database entities (e.g. EF Core entity classes)
```

- **Entities** represent stored data. Do not pass entities directly to views. Map them to a
  view model in `Models/` first.
- **Models** shape data for a particular view or form, including validation attributes.
- Name views after their action: `Views/Schedule/Index.cshtml` for
  `ScheduleController.Index()`.
- Namespaces follow the folders: `schedulerFA.Controllers`, `schedulerFA.Models`,
  `schedulerFA.Entities`.
