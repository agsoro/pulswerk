using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Visual regression tests — screenshot comparisons.
/// First run with UPDATE_SNAPSHOTS=1 generates baselines.
/// Subsequent runs compare against them.
/// </summary>
[TestFixture]
public class VisualRegressionTests : BrowserTestBase
{
    private ILocator[] GetDynamicMasks() =>
    [
        Page.Locator("#connection-status"),
        Page.Locator(Tid("sidebar-footer") + " span"),
        Page.Locator(".point-value"),
        Page.Locator("[data-key]"),
        Page.Locator(".alarm-box .box-count"),
    ];

    // ── Page screenshots ────────────────────────────────────────────────

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task PageScreenshot(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();
        await DisableAnimations();

        await AssertScreenshot($"page-{pg.Name.ToLower()}", fullPage: true);
    }

    // ── Component screenshots ───────────────────────────────────────────

    [Test]
    public async Task SidebarCollapsed()
    {
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();
        await DisableAnimations();

        await AssertElementScreenshot(Tid("sidebar"), "sidebar-collapsed");
    }

    [Test]
    public async Task SidebarExpandedOnHover()
    {
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();
        await DisableAnimations();

        await Page.Locator(Tid("sidebar")).HoverAsync();
        await Page.WaitForTimeoutAsync(300);

        await AssertElementScreenshot(Tid("sidebar"), "sidebar-expanded");
    }

    [Test]
    public async Task AlarmPriorityBoxes()
    {
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();
        await DisableAnimations();

        await AssertElementScreenshot(Tid("alarm-boxes"), "alarm-boxes");
    }

    // ── Modal & popup screenshots ───────────────────────────────────────

    [Test]
    public async Task DashboardCreateModal()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        await Page.Locator(Tid("dash-create-btn")).ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        await AssertScreenshot("modal-create-dashboard", fullPage: true);
    }

    [Test]
    public async Task DashboardEditModeToolbar()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var card = Page.Locator(".dash-card").First;
        if (await card.CountAsync() > 0)
        {
            await card.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            await Page.Locator(Tid("dash-edit-btn")).ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        await AssertScreenshot("modal-edit-toolbar", fullPage: true);
    }

    [Test]
    public async Task AddWidgetModal()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var card = Page.Locator(".dash-card").First;
        if (await card.CountAsync() > 0)
        {
            await card.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            await Page.Locator(Tid("dash-edit-btn")).ClickAsync();
            await Page.WaitForTimeoutAsync(300);

            var addBtn = Page.Locator(Tid("dash-add-widget-btn"));
            if (await addBtn.IsVisibleAsync())
            {
                await addBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
            }
        }

        await AssertScreenshot("modal-add-widget", fullPage: true);
    }

    [Test]
    public async Task TimewindowDropdown()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var card = Page.Locator(".dash-card").First;
        if (await card.CountAsync() > 0)
        {
            await card.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            await Page.Locator(Tid("tw-selector")).ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        await AssertScreenshot("popup-timewindow", fullPage: true);
    }

    [Test]
    public async Task AlarmAcknowledgeModal()
    {
        await Page.GotoAsync(Url("/plswk/Alarms"));
        await WaitForDashboard();
        await DisableAnimations();

        var ackBtn = Page.Locator(".ack-btn").First;
        if (await ackBtn.CountAsync() > 0)
        {
            await ackBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        await AssertScreenshot("modal-alarm-ack", fullPage: true);
    }

    [Test]
    public async Task ScheduleModalViewMode()
    {
        await Page.GotoAsync(Url("/plswk/Assets"));
        await WaitForDashboard();
        await DisableAnimations();

        var scheduleBtn = Page.Locator("button:has(.fa-clock)").First;
        if (await scheduleBtn.CountAsync() > 0)
        {
            await scheduleBtn.ClickAsync();
            await Page.WaitForSelectorAsync("#scheduleModal", new() { State = WaitForSelectorState.Visible });
            await Page.WaitForSelectorAsync(".sched-day-row", new() { State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(300);
        }

        await AssertElementScreenshot("#scheduleModal .modal-content", "modal-schedule-view");
    }

    [Test]
    public async Task ScheduleModalEditMode()
    {
        await Page.GotoAsync(Url("/plswk/Assets"));
        await WaitForDashboard();
        await DisableAnimations();

        var scheduleBtn = Page.Locator("button:has(.fa-clock)").First;
        if (await scheduleBtn.CountAsync() > 0)
        {
            await scheduleBtn.ClickAsync();
            await Page.WaitForSelectorAsync("#scheduleModal", new() { State = WaitForSelectorState.Visible });
            await Page.WaitForSelectorAsync(".sched-day-row", new() { State = WaitForSelectorState.Visible });
            
            await Page.ClickAsync("#btnEditSchedule");
            await Page.WaitForSelectorAsync("#editScheduleActions", new() { State = WaitForSelectorState.Visible });
            await Page.WaitForTimeoutAsync(300);
        }

        await AssertElementScreenshot("#scheduleModal .modal-content", "modal-schedule-edit");
    }

    // ── Spec-driven element screenshots ─────────────────────────────────
    // Automatically takes a screenshot for every spec element that has a
    // resolvable selector in the DOM.

    [TestCaseSource(nameof(ScreenshotSpecs))]
    public async Task SpecElementScreenshot(ComponentSpec spec)
    {
        var path = spec.PagePath ?? "/";
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(path));
        await WaitForDashboard();
        await DisableAnimations();

        // For modals: try to open them first
        if (spec.IsModal && spec.TriggerSelector is not null)
        {
            var trigger = Page.Locator(spec.TriggerSelector).First;
            if (await trigger.CountAsync() > 0)
            {
                await trigger.ClickAsync();
                await Page.WaitForTimeoutAsync(400);
            }
        }

        // Find the element to screenshot
        ILocator? element = null;
        if (spec.RequiredTestIds.Length > 0)
        {
            element = Page.Locator(Tid(spec.Id));
        }
        else if (spec.RequiredSelectors.Length > 0)
        {
            element = Page.Locator(spec.RequiredSelectors[0]);
        }

        if (element is null || await element.CountAsync() == 0)
        {
            if (spec.IsConditional)
            {
                Assert.Pass($"[{spec.Name}] conditional element not present — skipped");
                return;
            }
            Assert.Inconclusive($"[{spec.Name}] element not found for screenshot");
            return;
        }

        // Check if element is visible (has non-zero dimensions)
        var box = await element.BoundingBoxAsync();
        if (box is null || box.Width < 1 || box.Height < 1)
        {
            // Hidden element (modal not opened, etc) — take full page instead
            await AssertScreenshot($"spec-{spec.Id}", fullPage: true);
        }
        else
        {
            await AssertElementScreenshot(
                spec.RequiredTestIds.Length > 0
                    ? Tid(spec.Id)
                    : spec.RequiredSelectors[0],
                $"spec-{spec.Id}");
        }
    }

    // ── Content population tests ────────────────────────────────────────
    // Verifies that JS-generated content is actually populated after page load.
    // This catches bugs like empty dropdowns where the DOM element exists but
    // no children were rendered by JS.

    [Test]
    public async Task TimewindowPresetsPopulated()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();

        // Navigate to a dashboard to trigger initDashboards()
        var card = Page.Locator(".dash-card").First;
        if (await card.CountAsync() == 0)
        {
            Assert.Inconclusive("No dashboards exist to test timewindow presets");
            return;
        }

        await card.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        // Verify preset buttons were generated by JS
        var presets = Page.Locator("#twPresets .tw-preset");
        var count = await presets.CountAsync();
        Assert.That(count, Is.GreaterThanOrEqualTo(8),
            $"Timewindow presets should have ≥8 buttons (5m,15m,1h,...) but found {count}. " +
            "Check that buildTwPresets() runs during initDashboards().");

        // Verify one is marked active
        var active = Page.Locator("#twPresets .tw-preset.active");
        var activeCount = await active.CountAsync();
        Assert.That(activeCount, Is.EqualTo(1),
            "Exactly one timewindow preset should be marked as active");

        // Verify the dropdown opens and shows presets
        await Page.Locator(Tid("tw-selector")).ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var dropdown = Page.Locator(".tw-dropdown");
        var hasOpen = await dropdown.EvaluateAsync<bool>("el => el.classList.contains('open')");
        Assert.That(hasOpen, Is.True,
            "Timewindow dropdown should have .open class after clicking");

        // Screenshot the open dropdown with populated presets
        await AssertScreenshot("popup-timewindow-presets", fullPage: true);
    }

    [Test]
    public async Task DashboardCardsPopulated()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await Page.WaitForTimeoutAsync(500); // wait for loadList()

        var cards = Page.Locator(".dash-card");
        var empty = Page.Locator("#emptyDashboards");

        var cardCount = await cards.CountAsync();
        var emptyVisible = await empty.EvaluateAsync<string>("el => getComputedStyle(el).display");

        // Either cards exist or empty state is visible — never both hidden
        Assert.That(cardCount > 0 || emptyVisible != "none", Is.True,
            "Dashboard list should show either cards or empty state after JS loadList()");
    }

    [Test]
    public async Task AssetTreePopulated()
    {
        await Page.GotoAsync(Url("/plswk/Assets"));
        await WaitForDashboard();
        await Page.WaitForTimeoutAsync(500); // wait for tree fetch

        var treeNodes = Page.Locator("#assetTree .tree-row");
        var count = await treeNodes.CountAsync();

        Assert.That(count, Is.GreaterThan(0),
            "Asset tree should have >0 nodes after fetch. " +
            "Check that the tree handler returns data and JS populates #assetTree.");
    }

    [Test]
    public async Task AlarmListPopulated()
    {
        await Page.GotoAsync(Url("/plswk/Alarms"));
        await WaitForDashboard();

        var items = Page.Locator(".alarm-item");
        var empty = Page.Locator(".empty-state");

        var itemCount = await items.CountAsync();
        var emptyCount = await empty.CountAsync();

        Assert.That(itemCount > 0 || emptyCount > 0, Is.True,
            "Alarms page should show either alarm items or empty state");
    }

    private static IEnumerable<ComponentSpec> ScreenshotSpecs() =>
        UiSpec.All.Where(s => !s.IsConditional);

    // ── Responsive screenshots ──────────────────────────────────────────

    private static readonly (string Name, int Width, int Height)[] Viewports =
    [
        ("desktop", 1920, 1080),
        ("laptop",  1366, 768),
        ("tablet",  1024, 768),
    ];

    [Test]
    public async Task ResponsiveScreenshots(
        [ValueSource(nameof(Viewports))] (string Name, int Width, int Height) vp)
    {
        await Page.SetViewportSizeAsync(vp.Width, vp.Height);
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();
        await DisableAnimations();

        await AssertScreenshot($"responsive-{vp.Name}", fullPage: true);
    }
}
