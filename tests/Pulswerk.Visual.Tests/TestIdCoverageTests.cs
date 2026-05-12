using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Verifies that every data-testid in the HTML is registered here,
/// and every registered ID actually exists in the DOM.
/// Bidirectional coverage — adding a testid without a test will fail.
/// </summary>
[TestFixture]
public class TestIdCoverageTests : BrowserTestBase
{
    /// <summary>Test IDs present on every page (from _Layout.cshtml).</summary>
    private static readonly string[] GlobalTestIds =
    [
        "sidebar", "sidebar-brand", "nav-links",
        "nav-dashboard", "nav-dashboards", "nav-assets",
        "nav-connections", "nav-alarms", "nav-logs",
        "sidebar-footer", "page-header", "page-title",
    ];

    /// <summary>Test IDs per page (from individual .cshtml files).</summary>
    private static readonly Dictionary<string, string[]> PageTestIds = new()
    {
        ["/"] = ["alarm-boxes", "favorites-list"],
        ["/Dashboards"] = ["dash-list-mode", "dash-create-btn", "dash-grid"],
        ["/Connections"] = ["conn-layout", "conn-list", "conn-detail", "conn-detail-empty"],
        ["/Alarms"] = ["alarm-filters", "alarm-list", "ack-modal"],
    };

    /// <summary>Test IDs for shared modals (hidden until interaction).</summary>
    private static readonly string[] ModalTestIds =
        ["history-modal", "edit-modal", "props-modal"];

    /// <summary>Dashboard edit-mode IDs (in DOM when a dashboard view is loaded).</summary>
    private static readonly string[] DashEditTestIds =
    [
        "dash-edit-mode", "dash-toolbar", "dash-edit-btn",
        "dash-save-btn", "dash-cancel-btn", "dash-add-widget-btn",
        "tw-selector", "add-widget-modal", "create-dash-modal",
    ];

    /// <summary>All known IDs for orphan detection.</summary>
    private static HashSet<string> AllKnownIds
    {
        get
        {
            var set = new HashSet<string>(GlobalTestIds);
            foreach (var ids in PageTestIds.Values)
                foreach (var id in ids) set.Add(id);
            foreach (var id in ModalTestIds) set.Add(id);
            foreach (var id in DashEditTestIds) set.Add(id);
            return set;
        }
    }

    [Test]
    public async Task AllGlobalTestIdsExistOnEveryPage()
    {
        foreach (var pg in Pages.All)
        {
            await Page.GotoAsync(Url(pg.Path));
            await WaitForDashboard();

            foreach (var id in GlobalTestIds)
            {
                var el = Page.Locator(Tid(id));
                await Expect(el).ToBeAttachedAsync(new()
                {
                    Timeout = 5000
                });
            }
        }
    }

    [Test]
    public async Task PageSpecificTestIdsExist()
    {
        foreach (var (path, ids) in PageTestIds)
        {
            await Page.GotoAsync(Url(path));
            await WaitForDashboard();

            foreach (var id in ids)
            {
                var el = Page.Locator(Tid(id));
                await Expect(el).ToBeAttachedAsync(new()
                {
                    Timeout = 5000
                });
            }
        }
    }

    [Test]
    public async Task ModalTestIdsArePresentInDom()
    {
        // Check on Dashboard overview which includes _AssetModals
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();

        foreach (var id in ModalTestIds)
        {
            var el = Page.Locator(Tid(id));
            await Expect(el).ToBeAttachedAsync(new()
            {
                Timeout = 5000
            });
        }
    }

    [Test]
    public async Task DashboardEditModeTestIdsExist()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();

        foreach (var id in DashEditTestIds)
        {
            var el = Page.Locator(Tid(id));
            await Expect(el).ToBeAttachedAsync(new()
            {
                Timeout = 5000
            });
        }
    }

    [Test]
    public async Task NoOrphanedTestIdsInDom()
    {
        var known = AllKnownIds;

        foreach (var pg in Pages.All)
        {
            await Page.GotoAsync(Url(pg.Path));
            await WaitForDashboard();

            var domIds = await Page.EvaluateAsync<string[]>("""
                Array.from(document.querySelectorAll('[data-testid]'))
                     .map(el => el.getAttribute('data-testid'))
                     .filter(Boolean)
            """);

            var unregistered = domIds.Where(id => !known.Contains(id)).ToArray();
            Assert.That(unregistered, Is.Empty,
                $"[{pg.Name}] found unregistered data-testid(s): {string.Join(", ", unregistered)}. " +
                "Add them to the registry in TestIdCoverageTests.cs.");
        }
    }

    // NUnit Playwright Expect helper
    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
