using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// E2E functional tests — navigation, theme, structure, and accessibility assertions.
/// Uses data-testid selectors for stable element targeting.
/// </summary>
[TestFixture]
public class E2eTests : BrowserTestBase
{
    // ── Navigation ──────────────────────────────────────────────────────

    [Test]
    public async Task AllPagesLoadWithCorrectHeading()
    {
        foreach (var pg in Pages.All)
        {
            await Page.GotoAsync(Url(pg.Path));
            await WaitForDashboard();
            await Expect(Page.Locator(Tid("page-title"))).ToHaveTextAsync(pg.Heading);
        }
    }

    [Test]
    public async Task SidebarHas6NavigationLinks()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        await Expect(Page.Locator($"{Tid("nav-links")} a")).ToHaveCountAsync(6);
    }

    [Test]
    public async Task ActiveNavLinkMatchesCurrentPage()
    {
        var navMap = new Dictionary<string, string>
        {
            ["/"] = "nav-dashboard",
            ["/Dashboards"] = "nav-dashboards",
            ["/Assets"] = "nav-assets",
            ["/Connections"] = "nav-connections",
            ["/Alarms"] = "nav-alarms",
            ["/Logs"] = "nav-logs",
        };

        foreach (var (path, navId) in navMap)
        {
            await Page.GotoAsync(Url(path));
            await WaitForDashboard();
            await Expect(Page.Locator(Tid(navId))).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        }
    }

    [Test]
    public async Task AlarmBoxLinksNavigateToAlarmsPage()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        await Page.ClickAsync("#box-critical");
        await Page.WaitForURLAsync("**/Alarms**");
        await WaitForDashboard();
        await Expect(Page.Locator(Tid("page-title"))).ToContainTextAsync("Alarm");
    }

    // ── Theme & Styling ─────────────────────────────────────────────────

    [Test]
    public async Task DarkBackgroundIsApplied()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        var bg = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).backgroundColor");
        Assert.That(bg, Is.EqualTo("rgb(15, 23, 42)"));
    }

    [Test]
    public async Task InterFontIsLoaded()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        var font = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).fontFamily");
        Assert.That(font, Does.Contain("Inter"));
    }

    [Test]
    public async Task GlassmorphismBlurIsApplied()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        var glass = Page.Locator(".glass");
        if (await glass.CountAsync() > 0)
        {
            var filter = await glass.First.EvaluateAsync<string>("""
                el => getComputedStyle(el).backdropFilter
                   || getComputedStyle(el).webkitBackdropFilter
            """);
            Assert.That(filter, Does.Contain("blur"));
        }
    }

    // ── Dashboard Structure ─────────────────────────────────────────────

    [Test]
    public async Task AlarmBoxesExistWith5SeverityLevels()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        await Expect(Page.Locator($"{Tid("alarm-boxes")} .alarm-box")).ToHaveCountAsync(5);

        foreach (var id in new[] { "critical", "major", "minor", "warning", "total" })
        {
            await Expect(Page.Locator($"#box-{id}")).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task FavoritesSectionExists()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        await Expect(Page.Locator(Tid("favorites-list"))).ToBeAttachedAsync();
    }

    // ── Accessibility ───────────────────────────────────────────────────

    [Test]
    public async Task ImagesHaveAltTextOrAreDecorative()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        var images = Page.Locator("img");
        var count = await images.CountAsync();

        for (int i = 0; i < count; i++)
        {
            var alt = await images.Nth(i).GetAttributeAsync("alt");
            var role = await images.Nth(i).GetAttributeAsync("role");
            Assert.That(alt is not null || role == "presentation", Is.True,
                $"Image #{i} missing alt text and is not decorative.");
        }
    }

    [Test]
    public async Task SidebarLinksAreNotInvisible()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        var color = await Page.Locator(Tid("nav-dashboard"))
            .EvaluateAsync<string>("el => getComputedStyle(el).color");

        Assert.That(color, Is.Not.EqualTo("rgba(0, 0, 0, 0)"));
        Assert.That(color, Is.Not.EqualTo("rgb(15, 23, 42)"));
    }

    // NUnit Playwright Expect helper
    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
