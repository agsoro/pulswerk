using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Wireframe Layout Zone Tests — validates that the live page structure matches
/// the expected layout zones defined in <see cref="PageWireframes"/>.
///
/// These tests are DATA-INDEPENDENT: they check structural layout (position, size,
/// relative ordering) rather than pixel content. This catches:
///   - Missing or collapsed sections
///   - Layout shifts (sidebar gone, header missing)
///   - Panels appearing in wrong order or wrong position
///   - Components that are too small or too large
///
/// All coordinates are validated at 1920×1080 viewport.
/// </summary>
[TestFixture]
public class LayoutZoneTests : BrowserTestBase
{
    // ── Zone position & size validation ─────────────────────────────────

    [TestCaseSource(nameof(AllPageZones))]
    public async Task ZoneMatchesExpectedBounds(string pagePath, string pageName, LayoutZone zone)
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pagePath));
        await WaitForDashboard();

        var el = Page.Locator(zone.Selector).First;
        if (await el.CountAsync() == 0)
        {
            if (zone.IsOptional)
            {
                Assert.Pass($"[{pageName}] Zone '{zone.Label}' is optional and not present — OK");
                return;
            }
            Assert.Fail(
                $"[{pageName}] Zone '{zone.Label}' selector '{zone.Selector}' not found in DOM. " +
                "The wireframe expects this element to exist.");
            return;
        }

        var box = await el.BoundingBoxAsync();
        if (box is null || (box.Width < 1 && box.Height < 1))
        {
            // Skip if optional OR allowed to be empty (MinHeight = 0)
            if (zone.IsOptional || zone.MinHeight is 0)
            {
                Assert.Pass($"[{pageName}] Zone '{zone.Label}' is empty/hidden — OK (MinHeight={zone.MinHeight})");
                return;
            }
            Assert.Fail(
                $"[{pageName}] Zone '{zone.Label}' exists but has 0-size bounding box. " +
                "Element may be hidden via CSS.");
            return;
        }

        var findings = new List<string>();

        // Position checks
        if (zone.ExpectedX.HasValue)
        {
            var drift = Math.Abs(box.X - zone.ExpectedX.Value);
            if (drift > zone.PositionTolerance)
                findings.Add($"X={box.X:F0}px (expected ~{zone.ExpectedX}px ±{zone.PositionTolerance})");
        }
        if (zone.ExpectedY.HasValue)
        {
            var drift = Math.Abs(box.Y - zone.ExpectedY.Value);
            if (drift > zone.PositionTolerance)
                findings.Add($"Y={box.Y:F0}px (expected ~{zone.ExpectedY}px ±{zone.PositionTolerance})");
        }

        // Size checks
        if (zone.MinWidth.HasValue && box.Width < zone.MinWidth.Value)
            findings.Add($"width={box.Width:F0}px < min {zone.MinWidth}px");
        if (zone.MaxWidth.HasValue && box.Width > zone.MaxWidth.Value)
            findings.Add($"width={box.Width:F0}px > max {zone.MaxWidth}px");
        if (zone.MinHeight.HasValue && box.Height < zone.MinHeight.Value)
            findings.Add($"height={box.Height:F0}px < min {zone.MinHeight}px");
        if (zone.MaxHeight.HasValue && box.Height > zone.MaxHeight.Value)
            findings.Add($"height={box.Height:F0}px > max {zone.MaxHeight}px");

        // Full viewport checks
        if (zone.FullHeight && box.Height < 900)
            findings.Add($"height={box.Height:F0}px but expected full height (≥900px at 1080 viewport)");
        if (zone.FullWidth && box.Width < 1700)
            findings.Add($"width={box.Width:F0}px but expected full width (≥1700px at 1920 viewport)");

        Assert.That(findings, Is.Empty,
            $"[{pageName}] Zone '{zone.Label}' layout violations:\n  " +
            string.Join("\n  ", findings));

        TestContext.Out.WriteLine(
            $"✅ [{pageName}] {zone.Label}: {box.X:F0},{box.Y:F0} → {box.Width:F0}×{box.Height:F0}px");
    }

    // ── Relative ordering validation ────────────────────────────────────

    [TestCaseSource(nameof(AllPageZones))]
    public async Task ZoneRelativePositionCorrect(string pagePath, string pageName, LayoutZone zone)
    {
        if (zone.MustBeAbove is null && zone.MustBeRightOf is null)
        {
            Assert.Pass($"[{pageName}] {zone.Label}: no relative constraints");
            return;
        }

        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pagePath));
        await WaitForDashboard();

        var el = Page.Locator(zone.Selector).First;
        if (await el.CountAsync() == 0)
        {
            Assert.Pass($"[{pageName}] {zone.Label}: element not present — skipped");
            return;
        }

        var box = await el.BoundingBoxAsync();
        if (box is null) return;

        // "Must be above" → this element's bottom edge should be ≤ target's top edge
        if (zone.MustBeAbove is not null)
        {
            var target = Page.Locator(zone.MustBeAbove).First;
            if (await target.CountAsync() > 0)
            {
                var targetBox = await target.BoundingBoxAsync();
                if (targetBox is not null)
                {
                    Assert.That(box.Y + box.Height, Is.LessThanOrEqualTo(targetBox.Y + 5),
                        $"[{pageName}] '{zone.Label}' (bottom={box.Y + box.Height:F0}px) " +
                        $"should be ABOVE '{zone.MustBeAbove}' (top={targetBox.Y:F0}px)");
                }
            }
        }

        // "Must be right of" → this element's left edge should be ≥ target's right edge
        if (zone.MustBeRightOf is not null)
        {
            var target = Page.Locator(zone.MustBeRightOf).First;
            if (await target.CountAsync() > 0)
            {
                var targetBox = await target.BoundingBoxAsync();
                if (targetBox is not null)
                {
                    Assert.That(box.X, Is.GreaterThanOrEqualTo(targetBox.X + targetBox.Width - 5),
                        $"[{pageName}] '{zone.Label}' (left={box.X:F0}px) " +
                        $"should be RIGHT OF '{zone.MustBeRightOf}' (right={targetBox.X + targetBox.Width:F0}px)");
                }
            }
        }
    }

    // ── Wireframe completeness ──────────────────────────────────────────
    // Verifies that ALL zones for a page are simultaneously present

    [TestCaseSource(nameof(AllPages))]
    public async Task AllPageZonesPresent(string pagePath, string pageName, LayoutZone[] zones)
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pagePath));
        await WaitForDashboard();

        var missing = new List<string>();
        foreach (var zone in zones)
        {
            var el = Page.Locator(zone.Selector).First;
            if (await el.CountAsync() == 0)
            {
                if (!zone.IsOptional)
                    missing.Add($"'{zone.Label}' ({zone.Selector})");
                continue;
            }
            var box = await el.BoundingBoxAsync();
            if (box is null || (box.Width < 1 && box.Height < 1))
            {
                // Skip optional zones and zones that allow zero height (empty state)
                if (!zone.IsOptional && zone.MinHeight is not (null or 0))
                    missing.Add($"'{zone.Label}' (zero-size)");
            }
        }

        Assert.That(missing, Is.Empty,
            $"[{pageName}] {missing.Count}/{zones.Length} wireframe zones are missing or collapsed:\n  " +
            string.Join("\n  ", missing));
    }

    // ── Test data sources ──────────────────────────────────────────────

    private static IEnumerable<TestCaseData> AllPageZones()
    {
        foreach (var (path, name, zones) in PageWireframes.All)
        {
            foreach (var zone in zones)
            {
                yield return new TestCaseData(path, name, zone)
                    .SetName($"{{m}}({name} — {zone.Label})");
            }
        }
    }

    private static IEnumerable<TestCaseData> AllPages()
    {
        foreach (var (path, name, zones) in PageWireframes.All)
        {
            yield return new TestCaseData(path, name, zones)
                .SetName($"{{m}}({name})");
        }
    }
}
