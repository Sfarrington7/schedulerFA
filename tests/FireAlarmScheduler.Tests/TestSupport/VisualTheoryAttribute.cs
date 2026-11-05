// Purpose: [VisualFact]/[VisualTheory] mark browser layout tests. They are skipped, with instructions, when the
// Chromium build that Microsoft.Playwright expects isn't installed, so the rest of the suite still runs anywhere.
using System.Text.Json;

namespace FireAlarmScheduler.Tests.TestSupport;

public sealed class VisualFactAttribute : FactAttribute
{
    public VisualFactAttribute() => Skip = PlaywrightBrowser.MissingReason;
}

public sealed class VisualTheoryAttribute : TheoryAttribute
{
    public VisualTheoryAttribute() => Skip = PlaywrightBrowser.MissingReason;
}

public static class PlaywrightBrowser
{
    /// <summary>Null when the expected Chromium is installed; otherwise how to install it.</summary>
    public static string? MissingReason { get; } = FindMissingReason();

    /// <summary>
    /// Looks for the Chromium revision listed in the Playwright package's browsers.json, in PLAYWRIGHT_BROWSERS_PATH
    /// or Playwright's default per-user folder for this operating system.
    /// </summary>
    private static string? FindMissingReason()
    {
        const string install = "Install it with: pwsh tests/FireAlarmScheduler.Tests/bin/Debug/net10.0/playwright.ps1 install chromium";
        var manifest = Path.Combine(AppContext.BaseDirectory, ".playwright", "package", "browsers.json");
        if (!File.Exists(manifest))
        {
            return "Playwright's browser list wasn't found next to the tests. Rebuild the test project.";
        }

        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var revision = json.RootElement.GetProperty("browsers").EnumerateArray()
            .First(b => b.GetProperty("name").GetString() == "chromium")
            .GetProperty("revision").GetString();

        var folder = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH") switch
        {
            { Length: > 0 } path => path,
            _ when OperatingSystem.IsWindows() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright"),
            _ when OperatingSystem.IsMacOS() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "ms-playwright"),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright")
        };

        var installed = Directory.Exists(folder) && Directory.EnumerateDirectories(folder)
            .Select(Path.GetFileName)
            .Any(name => name == $"chromium-{revision}" || name == $"chromium_headless_shell-{revision}");
        return installed ? null : $"Chromium {revision} for Playwright isn't installed in {folder}. {install}";
    }
}
