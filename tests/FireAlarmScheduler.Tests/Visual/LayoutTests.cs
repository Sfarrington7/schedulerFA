// Purpose: Browser layout tests for the site's look on desktop (1366×900), tablet (820×1180) and phone (390×844).
// Every page is opened in headless Chromium and checked for: a successful load with no script errors or missing
// files; no sideways scrolling or content running off the screen; no buttons, links or fields overlapping; no text too
// small to read; and the right navigation for the width (sidebar on desktop, top bar and slide-out menu below 992px).
// Each page's screenshot is saved under bin/.../layout-screenshots/<viewport>/ for a quick manual look. The phone tests
// also drive the menu, the technician picker, and the building checklist script (ticking, saving, and a failed save).
using System.Text.Json;
using FireAlarmScheduler.Services;
using FireAlarmScheduler.Tests.TestSupport;
using Microsoft.Playwright;

namespace FireAlarmScheduler.Tests.Visual;

public abstract class LayoutTests(VisualTestApp app, string viewportName, int width, int height)
{
    private const string Lead = AppRoles.SuperAdmin;
    private const string Tech = AppRoles.Technician;
    private const int MinFontPx = 10;

    protected VisualTestApp App { get; } = app;
    protected bool IsDesktop => width >= 992;

    /// <summary>Every page, as (account role, path); "" is signed out. {placeholders} are seeded record IDs.</summary>
    public static TheoryData<string, string> Pages => new()
    {
        { "", "/Account/Login" },
        { Lead, "/" },
        { Lead, "/Schedule" },
        { Lead, "/Schedule/Month?month=2026-11-01" },
        { Lead, "/Jobs" },
        { Lead, "/Jobs/Calendar?month=2026-11-01" },
        { Lead, "/Jobs/Create" },
        { Lead, "/Jobs/Create?recurringJobId={recurringJobId}" },
        { Lead, "/Jobs/Details/{jobId}" },
        { Lead, "/Jobs/Edit/{jobId}" },
        { Lead, "/Jobs/Delete/{jobId}" },
        { Lead, "/Jobs/{jobId}/Buildings/{buildingId}/Edit" },
        { Lead, "/RecurringJobs" },
        { Lead, "/RecurringJobs/Details/{recurringJobId}" },
        { Lead, "/Technicians" },
        { Lead, "/Technicians/Create" },
        { Lead, "/Technicians/Edit/{technicianId}" },
        { Lead, "/TechnicianView?technicianId={technicianId}&range=Week&date=2026-11-02" },
        { Lead, "/TechnicianView?technicianId={technicianId}&range=Month&date=2026-11-02" },
        { Lead, "/PtoRequests" },
        { Lead, "/PtoRequests?filter=All" },
        { Lead, "/Conflicts" },
        { Lead, "/Users" },
        { Lead, "/Users/Create" },
        { Lead, "/Roles" },
        { Lead, "/Roles/Details/TeamLead" },
        { Lead, "/Account/ChangePassword" },
        { Tech, "/TechnicianView" },
        { Tech, "/TechnicianView?range=Day&date=2026-11-03" },
        { Tech, "/TechnicianView?range=Month&date=2026-11-01" },
        { Tech, "/TechnicianView/Job/{jobId}?technicianId={technicianId}" },
        { Tech, "/Pto" }
    };

    [VisualTheory]
    [MemberData(nameof(Pages))]
    public async Task Page_HasNoLayoutProblems(string role, string path)
    {
        await using var context = await NewContextAsync(role);
        var page = await context.NewPageAsync();
        var problems = new List<string>();
        page.PageError += (_, error) => problems.Add($"Script error: {error}");
        page.Console += (_, message) => { if (message.Type == "error") problems.Add($"Console error: {message.Text}"); };
        page.Response += (_, response) => { if (response.Status >= 400) problems.Add($"HTTP {response.Status}: {response.Url}"); };

        var response = await page.GotoAsync(App.Url(path), new() { WaitUntil = WaitUntilState.NetworkIdle });
        await SaveScreenshotAsync(page, path);

        Assert.Equal(200, response?.Status);
        problems.AddRange(await InspectAsync(page));
        problems.AddRange(await CheckNavigationAsync(page, signedIn: role != ""));
        Assert.True(problems.Count == 0, $"{viewportName} {path}:\n" + string.Join("\n", problems));
    }

    protected async Task<IBrowserContext> NewContextAsync(string role) => await App.Browser.NewContextAsync(new()
    {
        ViewportSize = new() { Width = width, Height = height },
        StorageState = App.SignedIn[role],
        IsMobile = !IsDesktop,
        HasTouch = !IsDesktop
    });

    /// <summary>Runs the in-page checks (see <see cref="InspectScript"/>) and returns one line per problem found.</summary>
    private static async Task<List<string>> InspectAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(InspectScript, MinFontPx);
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    /// <summary>Desktop: the sidebar sits beside the content and the phone top bar is hidden. Narrower: the reverse.</summary>
    private async Task<List<string>> CheckNavigationAsync(IPage page, bool signedIn)
    {
        var problems = new List<string>();
        if (!signedIn)
        {
            if (await page.Locator(".app-sidebar").CountAsync() > 0)
            {
                problems.Add("The sidebar shows while signed out.");
            }

            return problems;
        }

        var sidebar = await page.Locator(".app-sidebar").BoundingBoxAsync();
        var content = await page.Locator(".app-content").BoundingBoxAsync();
        var sidebarVisible = await page.Locator(".app-sidebar").IsVisibleAsync();
        var topbarVisible = await page.Locator(".app-topbar").IsVisibleAsync();
        if (IsDesktop)
        {
            if (!sidebarVisible || sidebar is null || content is null || content.X < sidebar.X + sidebar.Width - 1)
            {
                problems.Add("On desktop the sidebar should be visible beside the content, not over it.");
            }

            if (topbarVisible)
            {
                problems.Add("The phone top bar shows on desktop.");
            }
        }
        else
        {
            if (sidebarVisible)
            {
                problems.Add("The sidebar shows on a narrow screen without opening the menu.");
            }

            var button = await page.Locator(".app-menu-button").BoundingBoxAsync();
            if (!topbarVisible || button is null || button.Height < 40 || button.Width < 40)
            {
                problems.Add("The top bar's menu button is missing or smaller than a 40px tap target.");
            }
        }

        return problems;
    }

    private async Task SaveScreenshotAsync(IPage page, string path)
    {
        var name = string.Concat(path.Select(c => char.IsLetterOrDigit(c) ? c : '_')).Trim('_');
        var folder = Path.Combine(AppContext.BaseDirectory, "layout-screenshots", viewportName);
        Directory.CreateDirectory(folder);
        await page.ScreenshotAsync(new() { Path = Path.Combine(folder, (name.Length == 0 ? "home" : name) + ".png"), FullPage = true });
    }

    /// <summary>
    /// In-page checks; returns a JSON array of problems. (1) The page scrolls sideways. (2) A visible element runs off
    /// the left or right edge, unless it sits inside something that scrolls or clips sideways (wide tables, calendars).
    /// (3) Two links, buttons or fields overlap. (4) Text is drawn smaller than the minimum font size. (5) Text is cut
    /// off by a box that hides overflow without an ellipsis.
    /// </summary>
    private const string InspectScript = """
        minFont => {
          const problems = [];
          const vw = document.documentElement.clientWidth;
          const describe = el => {
            const cls = typeof el.className === 'string' && el.className.trim() ? '.' + el.className.trim().split(/\s+/).slice(0, 3).join('.') : '';
            const text = (el.textContent || el.value || '').trim().replace(/\s+/g, ' ').slice(0, 40);
            return `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}${cls} "${text}"`;
          };
          const shown = el => {
            const s = getComputedStyle(el);
            if (s.display === 'none' || s.visibility === 'hidden' || s.opacity === '0') return false;
            const r = el.getBoundingClientRect();
            return r.width > 0 && r.height > 0;
          };
          const insideSidewaysClip = el => {
            for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
              if (getComputedStyle(p).overflowX !== 'visible') return true;
            }
            return false;
          };
          const elements = [...document.body.querySelectorAll('*')]
            .filter(el => !(el instanceof SVGElement && el.tagName.toLowerCase() !== 'svg'))
            .filter(el => !el.closest('script, style, noscript, .visually-hidden') && shown(el));

          if (document.documentElement.scrollWidth > vw + 1) {
            problems.push(`The page scrolls sideways (${document.documentElement.scrollWidth}px wide on a ${vw}px screen).`);
          }

          for (const el of elements) {
            const r = el.getBoundingClientRect();
            if ((r.right > vw + 1 || r.left < -1) && !insideSidewaysClip(el)) {
              problems.push(`Runs off the screen: ${describe(el)} (${Math.round(r.left)}–${Math.round(r.right)}px)`);
            }
          }

          const controls = elements.filter(el => el.matches('a[href], button, input:not([type=hidden]), select, textarea'));
          for (let i = 0; i < controls.length; i++) {
            const a = controls[i], ra = a.getBoundingClientRect();
            for (let k = i + 1; k < controls.length; k++) {
              const b = controls[k];
              if (a.contains(b) || b.contains(a)) continue;
              const rb = b.getBoundingClientRect();
              const w = Math.min(ra.right, rb.right) - Math.max(ra.left, rb.left);
              const h = Math.min(ra.bottom, rb.bottom) - Math.max(ra.top, rb.top);
              if (w > 2 && h > 2) problems.push(`Overlapping: ${describe(a)} and ${describe(b)}`);
            }
          }

          for (const el of elements) {
            const ownText = [...el.childNodes].some(n => n.nodeType === Node.TEXT_NODE && n.textContent.trim());
            if (!ownText) continue;
            const s = getComputedStyle(el);
            if (parseFloat(s.fontSize) < minFont) {
              problems.push(`Text too small (${s.fontSize}): ${describe(el)}`);
            }
            const hides = s.overflowX === 'hidden' || s.overflowX === 'clip';
            if (hides && s.textOverflow !== 'ellipsis' && el.scrollWidth > el.clientWidth + 2) {
              problems.push(`Text cut off: ${describe(el)}`);
            }
          }

          return JSON.stringify([...new Set(problems)].slice(0, 25));
        }
        """;
}

public sealed class DesktopLayoutTests(VisualTestApp app) : LayoutTests(app, "desktop", 1366, 900), IClassFixture<VisualTestApp>
{
    [VisualFact]
    public async Task Sidebar_LongPageScrolled_StaysInView()
    {
        await using var context = await NewContextAsync(AppRoles.SuperAdmin);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/Jobs/Create"));

        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        var sidebar = await page.Locator(".app-sidebar").BoundingBoxAsync();

        Assert.True(await page.EvaluateAsync<double>("window.scrollY") > 0, "The page should be long enough to scroll.");
        Assert.NotNull(sidebar);
        Assert.Equal(0, sidebar.Y, 1);
        Assert.Equal(900, sidebar.Height, 1);
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "Sign out" }).CountAsync() == 0, "Sign out is a button.");
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).IsVisibleAsync());
    }

    [VisualFact]
    public async Task SignInLogo_Hovered_LooksTheSame()
    {
        await using var context = await NewContextAsync("");
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/Account/Login"));
        var logo = page.Locator(".app-brand-large");
        const string colors = "el => getComputedStyle(el).color + ' ' + getComputedStyle(el.querySelector('svg')).color";

        var before = await logo.EvaluateAsync<string>(colors);
        await logo.HoverAsync();
        var after = await logo.EvaluateAsync<string>(colors);

        Assert.Equal(before, after);
        Assert.Equal("rgb(15, 23, 42) rgb(37, 99, 235)", after);
    }
}

public sealed class TabletLayoutTests(VisualTestApp app) : LayoutTests(app, "tablet", 820, 1180), IClassFixture<VisualTestApp>;

public sealed class PhoneLayoutTests(VisualTestApp app) : LayoutTests(app, "phone", 390, 844), IClassFixture<VisualTestApp>
{
    [VisualFact]
    public async Task Menu_OpenFromTopBar_ShowsLinksOnScreenAndNavigates()
    {
        await using var context = await NewContextAsync(AppRoles.SuperAdmin);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/"));

        await page.ClickAsync(".app-menu-button");
        var sidebar = page.Locator(".app-sidebar.show");
        await sidebar.WaitForAsync();
        await page.WaitForTimeoutAsync(400);

        var jobsLink = sidebar.GetByRole(AriaRole.Link, new() { Name = "Jobs", Exact = true });
        var box = await sidebar.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.InRange(box.X, -1, 1);
        Assert.True(box.Width <= 390, "The open menu fits on the screen.");
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).IsVisibleAsync());
        Assert.Equal("1 waiting for review", await sidebar.Locator("[data-count=pto]").TextContentAsync());
        Assert.Equal("2 current", await sidebar.Locator("[data-count=conflicts]").TextContentAsync());
        var badge = await sidebar.Locator("[data-count=conflicts]").BoundingBoxAsync();
        Assert.True(badge is not null && badge.X + badge.Width <= box.X + box.Width, "The conflicts badge sits inside the open menu.");

        await jobsLink.ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/Jobs"));
        Assert.False(await page.Locator(".app-sidebar").IsVisibleAsync(), "The menu is closed on the new page.");
    }

    [VisualFact]
    public async Task Menu_CloseButton_HidesMenu()
    {
        await using var context = await NewContextAsync(AppRoles.Technician);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/TechnicianView"));

        await page.ClickAsync(".app-menu-button");
        await page.Locator(".app-sidebar.show").WaitForAsync();
        await page.ClickAsync(".app-sidebar .btn-close");
        await page.Locator(".app-sidebar").WaitForAsync(new() { State = WaitForSelectorState.Hidden });

        Assert.False(await page.Locator(".app-sidebar").IsVisibleAsync());
    }

    /// <summary>
    /// Runs the checklist script for real: ticking Building B saves it and moves it to Completed (in job order) without
    /// reloading the page, the progress updates, it is still there after a reload, and unticking puts it back.
    /// Ends as it started so the other tests see the seeded checklist.
    /// </summary>
    [VisualFact]
    public async Task Checklist_TickAndUntickBuilding_MovesBetweenSectionsAndSaves()
    {
        await using var context = await NewContextAsync(AppRoles.Technician);
        var page = await context.NewPageAsync();
        var problems = new List<string>();
        page.PageError += (_, error) => problems.Add(error);
        await page.GotoAsync(App.Url("/TechnicianView/Job/{jobId}?technicianId={technicianId}"));
        var todo = page.Locator("[data-list=todo] .checklist-name");
        var done = page.Locator("[data-list=done] .checklist-name");
        Assert.Equal(["Building B", "Parking Garage"], await todo.AllTextContentsAsync());
        Assert.Equal(["Building A"], await done.AllTextContentsAsync());
        await page.EvaluateAsync("window.samePage = true");

        await page.GetByLabel("Building B").CheckAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('[data-list=done] li').length === 2");

        Assert.Equal(["Building A", "Building B"], await done.AllTextContentsAsync());
        Assert.Equal(["Parking Garage"], await todo.AllTextContentsAsync());
        Assert.Equal("2 of 3 buildings complete", await page.Locator("[data-progress-text]").TextContentAsync());
        Assert.Equal("2", await page.Locator("[data-count=done]").TextContentAsync());
        Assert.Contains("John Smith", await page.Locator("#building-{buildingB} [data-completed-text]".Replace("{buildingB}", App.Ids["buildingBId"])).TextContentAsync());
        Assert.True(await page.EvaluateAsync<bool>("window.samePage === true"), "The page did not reload.");

        await page.ReloadAsync();
        Assert.Equal(["Building A", "Building B"], await done.AllTextContentsAsync());

        await page.GetByLabel("Building B").UncheckAsync();
        await page.WaitForFunctionAsync("document.querySelectorAll('[data-list=done] li').length === 1");
        Assert.Equal(["Building B", "Parking Garage"], await todo.AllTextContentsAsync());
        await page.ReloadAsync();
        Assert.Equal(["Building B", "Parking Garage"], await todo.AllTextContentsAsync());
        Assert.Empty(problems);
    }

    [VisualFact]
    public async Task Checklist_SaveFails_UndoesTickAndShowsError()
    {
        await using var context = await NewContextAsync(AppRoles.Technician);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/TechnicianView/Job/{jobId}?technicianId={technicianId}"));

        await context.SetOfflineAsync(true);
        await page.GetByLabel("Parking Garage").ClickAsync();
        var error = page.Locator("[data-checklist-error]");
        await error.WaitForAsync();

        Assert.Contains("Couldn't save that change", await error.TextContentAsync());
        Assert.False(await page.GetByLabel("Parking Garage").IsCheckedAsync());
        Assert.Equal(["Building B", "Parking Garage"], await page.Locator("[data-list=todo] .checklist-name").AllTextContentsAsync());
        await context.SetOfflineAsync(false);
    }

    [VisualFact]
    public async Task JobForm_TechnicianDropdownOpen_FitsOnScreen()
    {
        await using var context = await NewContextAsync(AppRoles.SuperAdmin);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.Url("/Jobs/Create?recurringJobId={recurringJobId}"));

        await page.ClickAsync("#technician-picker-toggle");
        var menu = page.Locator(".technician-menu");
        await menu.WaitForAsync();
        var box = await menu.BoundingBoxAsync();

        Assert.NotNull(box);
        Assert.True(box.X >= 0 && box.X + box.Width <= 390, $"The technician list fits across the screen ({box.X}–{box.X + box.Width}).");
    }
}
