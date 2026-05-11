using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Validates every component in <see cref="UiSpec"/> meets its documented design contract.
///
/// Test layers:
///   1. Structure: required elements exist in DOM
///   2. Visual baseline: CSS properties match expected values
///   3. Hover states: CSS properties change correctly on hover
///   4. Child counts: expected number of child elements
///   5. Modals: hidden by default + trigger opens them
///   6. Design tokens: global color/type system is applied
///   7. Meta: every spec has adequate documentation
/// </summary>
[TestFixture]
public class UiSpecTests : BrowserTestBase
{
    // ── 1. Structure ────────────────────────────────────────────────────

    [TestCaseSource(typeof(UiSpec), nameof(UiSpec.All))]
    public async Task RequiredElementsExist(ComponentSpec spec)
    {
        if (spec.IsConditional)
        {
            Assert.Pass($"[{spec.Name}] is conditionally rendered — skipped");
            return;
        }

        await NavigateToSpec(spec);

        foreach (var testId in spec.RequiredTestIds)
        {
            var el = Page.Locator(Tid(testId));
            await Expect(el).ToBeAttachedAsync(new() { Timeout = 5000 });
        }

        foreach (var selector in spec.RequiredSelectors)
        {
            var count = await Page.Locator(selector).CountAsync();
            Assert.That(count, Is.GreaterThan(0),
                $"[{spec.Name}] selector '{selector}' matched 0 elements");
        }
    }

    // ── 2. Visual baseline ──────────────────────────────────────────────

    [TestCaseSource(typeof(UiSpec), nameof(UiSpec.All))]
    public async Task VisualRulesMatch(ComponentSpec spec)
    {
        if (spec.VisualRules.Length == 0)
        {
            Assert.Pass($"[{spec.Name}] no visual rules defined");
            return;
        }

        await NavigateToSpec(spec);

        var element = spec.RequiredTestIds.Length > 0
            ? Page.Locator(Tid(spec.Id))
            : Page.Locator(spec.RequiredSelectors[0]);

        foreach (var rule in spec.VisualRules)
        {
            var actual = await element.EvaluateAsync<string>(
                "(el, prop) => getComputedStyle(el).getPropertyValue(prop)",
                rule.CssProperty);

            Assert.That(actual.Trim(), Is.EqualTo(rule.ExpectedValue),
                $"[{spec.Name}] CSS '{rule.CssProperty}': expected '{rule.ExpectedValue}' " +
                $"but got '{actual.Trim()}' — {rule.Description}");
        }
    }

    // ── 3. Hover states ─────────────────────────────────────────────────

    [TestCaseSource(nameof(HoverSpecs))]
    public async Task HoverRulesMatch(ComponentSpec spec)
    {
        await NavigateToSpec(spec);
        await DisableAnimations();

        var element = Page.Locator(Tid(spec.Id));
        await element.HoverAsync();
        await Page.WaitForTimeoutAsync(200);

        foreach (var rule in spec.HoverRules)
        {
            var actual = await element.EvaluateAsync<string>(
                "(el, prop) => getComputedStyle(el).getPropertyValue(prop)",
                rule.CssProperty);

            Assert.That(actual.Trim(), Is.EqualTo(rule.ExpectedValue),
                $"[{spec.Name}] hover CSS '{rule.CssProperty}': expected '{rule.ExpectedValue}' " +
                $"but got '{actual.Trim()}' — {rule.Description}");
        }
    }

    // ── 4. Child counts ─────────────────────────────────────────────────

    [TestCaseSource(typeof(UiSpec), nameof(UiSpec.All))]
    public async Task ChildCountsMatch(ComponentSpec spec)
    {
        if (spec.ChildCount is null)
        {
            Assert.Pass($"[{spec.Name}] no child count rule");
            return;
        }

        await NavigateToSpec(spec);

        var rule = spec.ChildCount;
        var parent = Page.Locator(Tid(rule.ParentTestId));
        var children = parent.Locator(rule.ChildSelector);
        var count = await children.CountAsync();

        Assert.That(count, Is.EqualTo(rule.ExpectedCount),
            $"[{spec.Name}] expected {rule.ExpectedCount} '{rule.ChildSelector}' " +
            $"inside [data-testid='{rule.ParentTestId}'], got {count}. {rule.Reason}");
    }

    // ── 5. Modals ───────────────────────────────────────────────────────

    [TestCaseSource(nameof(ModalSpecs))]
    public async Task ModalsAreHiddenByDefault(ComponentSpec spec)
    {
        await NavigateToSpec(spec);

        var modal = Page.Locator(Tid(spec.Id));
        var display = await modal.EvaluateAsync<string>(
            "el => getComputedStyle(el).display");

        Assert.That(display, Is.EqualTo("none"),
            $"[{spec.Name}] modal should be hidden by default but has display: {display}");
    }

    [TestCaseSource(nameof(TriggeredModalSpecs))]
    public async Task ModalTriggersWork(ComponentSpec spec)
    {
        await NavigateToSpec(spec);
        await DisableAnimations();

        var trigger = Page.Locator(spec.TriggerSelector!).First;
        if (await trigger.CountAsync() == 0)
        {
            Assert.Inconclusive($"[{spec.Name}] trigger '{spec.TriggerSelector}' not found");
            return;
        }

        await trigger.ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        var modal = Page.Locator(Tid(spec.Id));
        var display = await modal.EvaluateAsync<string>(
            "el => getComputedStyle(el).display");

        Assert.That(display, Is.Not.EqualTo("none"),
            $"[{spec.Name}] modal should be visible after clicking '{spec.TriggerSelector}' " +
            $"but has display: {display}");
    }

    // ── 6. Design tokens ────────────────────────────────────────────────

    [Test]
    public async Task GlobalDesignTokensApplied()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        // Background color
        var bg = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).backgroundColor");
        Assert.That(bg, Is.EqualTo(UiSpec.DesignTokens.BgColor),
            "Body background must match --bg-color token");

        // Font family
        var font = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).fontFamily");
        Assert.That(font, Does.Contain(UiSpec.DesignTokens.FontFamily),
            "Body font must include Inter");

        // Text color
        var color = await Page.EvaluateAsync<string>(
            "getComputedStyle(document.body).color");
        Assert.That(color, Is.EqualTo(UiSpec.DesignTokens.TextPrimary),
            "Body text color must match --text-primary token");
    }

    [Test]
    public async Task CssVariablesMatchTokens()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        // CSS variables return raw authored values (hex), not computed rgb
        var vars = new Dictionary<string, string>
        {
            ["--bg-color"] = "#0f172a",
            ["--card-bg"] = "#1e293b",
            ["--accent-primary"] = "#38bdf8",
            ["--border-color"] = "#334155",
            ["--sidebar-width"] = "72px",
        };

        foreach (var (varName, expected) in vars)
        {
            var actual = await Page.EvaluateAsync<string>(
                "(name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim()",
                varName);
            Assert.That(actual, Is.EqualTo(expected),
                $"CSS variable {varName} expected '{expected}' but got '{actual}'");
        }
    }

    [Test]
    public async Task SeverityColorsMatchTokens()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        var checks = new[]
        {
            ("#box-critical .box-count", "color", UiSpec.DesignTokens.Critical),
            ("#box-major .box-count",    "color", UiSpec.DesignTokens.Major),
            ("#box-minor .box-count",    "color", UiSpec.DesignTokens.Minor),
        };

        foreach (var (selector, prop, expected) in checks)
        {
            var el = Page.Locator(selector);
            if (await el.CountAsync() == 0) continue;

            var actual = await el.EvaluateAsync<string>(
                "(el, prop) => getComputedStyle(el).getPropertyValue(prop)", prop);
            Assert.That(actual.Trim(), Is.EqualTo(expected),
                $"Severity color for {selector} expected '{expected}' but got '{actual.Trim()}'");
        }
    }

    // ── 7. Meta-tests ───────────────────────────────────────────────────

    [Test]
    public void EverySpecHasPurposeDescription()
    {
        foreach (var spec in UiSpec.All)
        {
            Assert.That(spec.Purpose, Is.Not.Null.And.Not.Empty,
                $"[{spec.Name}] missing Purpose");
            Assert.That(spec.Purpose.Length, Is.GreaterThan(50),
                $"[{spec.Name}] Purpose too short — describe the design goal thoroughly");
        }
    }

    [Test]
    public void EverySpecHasBehaviorDescription()
    {
        foreach (var spec in UiSpec.All)
        {
            Assert.That(spec.Behavior, Is.Not.Null.And.Not.Empty,
                $"[{spec.Name}] missing Behavior description");
            Assert.That(spec.Behavior!.Length, Is.GreaterThan(30),
                $"[{spec.Name}] Behavior too short — describe all interaction states");
        }
    }

    [Test]
    public void EverySpecHasAtLeastOneRequiredElement()
    {
        foreach (var spec in UiSpec.All)
        {
            Assert.That(spec.RequiredTestIds.Length + spec.RequiredSelectors.Length, Is.GreaterThan(0),
                $"[{spec.Name}] needs at least one RequiredTestId or RequiredSelector");
        }
    }

    [Test]
    public void EveryModalHasHiddenByDefaultRule()
    {
        foreach (var spec in UiSpec.All.Where(s => s.IsModal))
        {
            var hasHiddenRule = spec.VisualRules
                .Any(r => r.CssProperty == "display" && r.ExpectedValue == "none");
            Assert.That(hasHiddenRule, Is.True,
                $"[{spec.Name}] is modal but missing VisualRule for display:none");
        }
    }

    [Test]
    public void DesignTokenDocumentationComplete()
    {
        // Verify all token constants are non-empty
        var fields = typeof(UiSpec.DesignTokens).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        foreach (var field in fields)
        {
            var value = field.GetValue(null) as string;
            Assert.That(value, Is.Not.Null.And.Not.Empty,
                $"DesignToken.{field.Name} must have a value");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task NavigateToSpec(ComponentSpec spec)
    {
        // Ensure desktop viewport so responsive breakpoints don't skew visual rules
        await Page.SetViewportSizeAsync(1920, 1080);
        var path = spec.PagePath ?? "/";
        await Page.GotoAsync(Url(path));
        await WaitForDashboard();
    }

    private static IEnumerable<ComponentSpec> ModalSpecs() =>
        UiSpec.All.Where(s => s.IsModal);

    private static IEnumerable<ComponentSpec> TriggeredModalSpecs() =>
        UiSpec.All.Where(s => s.IsModal && s.TriggerSelector is not null);

    private static IEnumerable<ComponentSpec> HoverSpecs() =>
        UiSpec.All.Where(s => s.HoverRules.Length > 0);

    private static ILocatorAssertions Expect(ILocator locator) =>
        Assertions.Expect(locator);
}
