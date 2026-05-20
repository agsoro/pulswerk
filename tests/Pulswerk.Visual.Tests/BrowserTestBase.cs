using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Base test class that connects to Browserless via CDP.
/// Each test gets a fresh page from the remote headless browser.
/// </summary>
public abstract class BrowserTestBase
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    protected IPage Page { get; private set; } = null!;
    protected string EffectiveDashboardUrl { get; private set; } = "http://localhost:5000";

    /// <summary>Connection URL for the Browserless container (CDP endpoint).</summary>
    private static string BrowserlessUrl =>
        Environment.GetEnvironmentVariable("BROWSERLESS_URL") ?? "ws://localhost:3000";

    /// <summary>Auth token for Browserless.</summary>
    private static string BrowserlessToken =>
        Environment.GetEnvironmentVariable("BROWSERLESS_TOKEN") ?? "pulswerk-visual-tests";

    /// <summary>Dashboard base URL template.</summary>
    private static string DashboardUrlBase =>
        Environment.GetEnvironmentVariable("DASHBOARD_URL") ?? "http://localhost:5000";

    [SetUp]
    public async Task SetUpBrowser()
    {
        _playwright = await Playwright.CreateAsync();
        var wsEndpoint = $"{BrowserlessUrl}/chromium?token={BrowserlessToken}";

        try
        {
            _browser = await _playwright.Chromium.ConnectOverCDPAsync(wsEndpoint);
            EffectiveDashboardUrl = "http://pulswerk:5000";
            TestContext.Out.WriteLine("✅ Connected to remote Browserless. Using dashboard: " + EffectiveDashboardUrl);
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"⚠️ Remote Browserless failed: {ex.Message}. Falling back to local browser.");
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            EffectiveDashboardUrl = "http://localhost:5000";
        }

        var context = _browser.Contexts.Count > 0
            ? _browser.Contexts[0]
            : await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            });

        Page = await context.NewPageAsync();
        Page.SetDefaultTimeout(60000);
        Page.SetDefaultNavigationTimeout(60000);
    }

    [TearDown]
    public async Task TearDownBrowser()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    // ── Navigation helpers ──────────────────────────────────────────────

    /// <summary>Full URL for a dashboard path.</summary>
    protected string Url(string path) => $"{EffectiveDashboardUrl}{path}";

    /// <summary>Selector shorthand for data-testid.</summary>
    protected static string Tid(string id) => $"[data-testid=\"{id}\"]";

    /// <summary>Wait for the dashboard shell to be fully loaded.</summary>
    protected async Task WaitForDashboard()
    {
        await Page.WaitForSelectorAsync(Tid("sidebar-brand"),
            new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Page.WaitForSelectorAsync(Tid("page-title"),
            new() { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 30_000 });
    }

    /// <summary>Inject CSS that kills all animations and transitions.</summary>
    protected async Task DisableAnimations()
    {
        await Page.AddStyleTagAsync(new()
        {
            Content = """
                *, *::before, *::after {
                    animation-duration: 0s !important;
                    animation-delay: 0s !important;
                    transition-duration: 0s !important;
                    transition-delay: 0s !important;
                    caret-color: transparent !important;
                }
                """
        });
        await Page.WaitForTimeoutAsync(100);
    }

    // ── Screenshot helpers ──────────────────────────────────────────────

    protected static string SnapshotDir =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "snapshots");

    protected static bool UpdateSnapshots =>
        Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

    protected async Task AssertScreenshot(string name, bool fullPage = false)
    {
        Directory.CreateDirectory(SnapshotDir);
        var actual = await Page.ScreenshotAsync(new()
        {
            FullPage = fullPage,
            Animations = ScreenshotAnimations.Disabled,
        });

        var baselinePath = Path.Combine(SnapshotDir, $"{name}.png");
        if (UpdateSnapshots || !File.Exists(baselinePath))
        {
            await File.WriteAllBytesAsync(baselinePath, actual);
            TestContext.Out.WriteLine($"📸 Baseline saved: {name}.png");
            return;
        }

        var baseline = await File.ReadAllBytesAsync(baselinePath);
        if (!baseline.SequenceEqual(actual))
        {
            var diffPath = Path.Combine(SnapshotDir, $"{name}-actual.png");
            await File.WriteAllBytesAsync(diffPath, actual);
            Assert.Fail($"Screenshot '{name}' differs from baseline. " +
                        $"Actual saved to {diffPath}. " +
                        "Run with UPDATE_SNAPSHOTS=1 to update baselines.");
        }
    }

    protected async Task AssertElementScreenshot(string selector, string name)
    {
        Directory.CreateDirectory(SnapshotDir);
        var el = Page.Locator(selector);
        var actual = await el.ScreenshotAsync(new()
        {
            Animations = ScreenshotAnimations.Disabled,
        });

        var baselinePath = Path.Combine(SnapshotDir, $"{name}.png");
        if (UpdateSnapshots || !File.Exists(baselinePath))
        {
            await File.WriteAllBytesAsync(baselinePath, actual);
            TestContext.Out.WriteLine($"📸 Baseline saved: {name}.png");
            return;
        }

        var baseline = await File.ReadAllBytesAsync(baselinePath);
        if (!baseline.SequenceEqual(actual))
        {
            var diffPath = Path.Combine(SnapshotDir, $"{name}-actual.png");
            await File.WriteAllBytesAsync(diffPath, actual);
            Assert.Fail($"Screenshot '{name}' differs from baseline. " +
                        $"Actual saved to {diffPath}. " +
                        "Run with UPDATE_SNAPSHOTS=1 to update baselines.");
        }
    }
}

/// <summary>Navigable pages with their expected headings.</summary>
public record PageInfo(string Name, string Path, string Heading);

public static class Pages
{
    public static readonly PageInfo[] All =
    [
        new("Dashboard",   "/plswk/",            "Dashboard"),
        new("Dashboards",  "/plswk/Dashboards",  "Dashboards"),
        new("Assets",      "/plswk/Assets",      "Assets"),
        new("Inventory",   "/plswk/AssetsList",  "Asset Inventory"),
        new("Connections", "/plswk/Connections",  "Connections"),
        new("Alarms",      "/plswk/Alarms",      "Active Alarms"),
        new("Logs",        "/plswk/Logs",         "System Logs"),
        new("Heartbeat",   "/plswk/Heartbeat",    "System Heartbeat"),
    ];
}
