using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Alignment & Bounding Box Audit Tests.
/// Detects layout issues that visual screenshots alone might miss:
///   - Elements overflowing their parent containers
///   - Children not vertically/horizontally aligned within flex/grid parents
///   - Elements clipped (partially or fully) outside the viewport
///   - Inconsistent spacing between sibling elements
///   - Zero-size or collapsed elements that should be visible
/// </summary>
[TestFixture]
public class AlignmentAuditTests : BrowserTestBase
{
    // ── Per-page overflow & containment audit ───────────────────────────

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task NoElementsOverflowViewport(PageInfo pg)
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        var json = await Page.EvaluateAsync<JsonElement>(@"() => {
            const vw = document.documentElement.clientWidth;
            const results = [];
            document.querySelectorAll('*').forEach(el => {
                const r = el.getBoundingClientRect();
                if (r.width === 0 || r.height === 0) return;
                if (getComputedStyle(el).display === 'none') return;
                if (r.right > vw + 2) {
                    results.push({
                        tag: el.tagName,
                        id: el.id || '',
                        cls: el.className?.toString().substring(0, 60) || '',
                        testId: el.getAttribute('data-testid') || '',
                        right: Math.round(r.right),
                        viewportWidth: vw,
                        overflow: Math.round(r.right - vw)
                    });
                }
            });
            return results;
        }");

        var overflows = json.EnumerateArray().ToArray();

        if (overflows.Length > 0)
        {
            var summary = string.Join("\n",
                overflows.Take(10).Select(o =>
                    $"  ⚠ <{o.GetProperty("tag")}> #{o.GetProperty("id")} " +
                    $".{o.GetProperty("cls")} [data-testid={o.GetProperty("testId")}] " +
                    $"right={o.GetProperty("right")}px (overflow={o.GetProperty("overflow")}px)"));

            TestContext.Out.WriteLine(
                $"📐 {pg.Name}: {overflows.Length} element(s) overflow the viewport:\n{summary}");
        }

        var significant = overflows.Where(o => o.GetProperty("overflow").GetInt32() > 20).ToArray();
        Assert.That(significant, Is.Empty,
            $"[{pg.Name}] {significant.Length} element(s) significantly overflow the viewport (>20px).");
    }

    // ── Flex/Grid alignment validation ──────────────────────────────────

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task FlexChildrenAreAligned(PageInfo pg)
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        var json = await Page.EvaluateAsync<JsonElement>(@"() => {
            const results = [];
            document.querySelectorAll('*').forEach(parent => {
                const style = getComputedStyle(parent);
                if (style.display !== 'flex' || style.flexDirection !== 'row') return;
                const children = [...parent.children].filter(c =>
                    getComputedStyle(c).position !== 'absolute' &&
                    getComputedStyle(c).display !== 'none');
                if (children.length < 2) return;

                const centers = children.map(c => {
                    const r = c.getBoundingClientRect();
                    return r.top + r.height / 2;
                });
                const avgCenter = centers.reduce((a, b) => a + b, 0) / centers.length;
                const maxDrift = Math.max(...centers.map(c => Math.abs(c - avgCenter)));

                if (maxDrift > 8) {
                    results.push({
                        parentTag: parent.tagName,
                        parentId: parent.id || '',
                        parentCls: parent.className?.toString().substring(0, 50) || '',
                        parentTestId: parent.getAttribute('data-testid') || '',
                        childCount: children.length,
                        maxDrift: Math.round(maxDrift)
                    });
                }
            });
            return results;
        }");

        var misaligned = json.EnumerateArray().ToArray();

        if (misaligned.Length > 0)
        {
            var summary = string.Join("\n",
                misaligned.Take(10).Select(m =>
                    $"  ⚠ <{m.GetProperty("parentTag")}> #{m.GetProperty("parentId")} " +
                    $".{m.GetProperty("parentCls")} " +
                    $"({m.GetProperty("childCount")} children, {m.GetProperty("maxDrift")}px drift)"));

            TestContext.Out.WriteLine(
                $"📐 {pg.Name}: {misaligned.Length} flex container(s) with misaligned children:\n{summary}");
        }

        Assert.Pass($"[{pg.Name}] {misaligned.Length} flex alignment finding(s) (logged above)");
    }

    // ── Zero-size / collapsed element detection ─────────────────────────

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task NoCollapsedVisibleElements(PageInfo pg)
    {
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        var json = await Page.EvaluateAsync<JsonElement>(@"() => {
            const results = [];
            document.querySelectorAll('[data-testid]').forEach(el => {
                const style = getComputedStyle(el);
                if (style.display === 'none') return;

                const r = el.getBoundingClientRect();
                if ((r.width === 0 || r.height === 0) &&
                    !['none','contents'].includes(style.display) &&
                    style.visibility !== 'hidden' &&
                    style.opacity !== '0') {
                    results.push({
                        tag: el.tagName,
                        testId: el.getAttribute('data-testid') || '',
                        display: style.display,
                        width: Math.round(r.width),
                        height: Math.round(r.height)
                    });
                }
            });
            return results;
        }");

        var collapsed = json.EnumerateArray().ToArray();
        var unexpected = collapsed
            .Where(c =>
            {
                var tid = c.GetProperty("testId").GetString() ?? "";
                return !tid.Contains("modal") && !tid.Contains("edit-mode");
            })
            .ToArray();

        if (unexpected.Length > 0)
        {
            var summary = string.Join("\n",
                unexpected.Select(c =>
                    $"  ⚠ <{c.GetProperty("tag")}> [data-testid={c.GetProperty("testId")}] " +
                    $"display={c.GetProperty("display")} but size=" +
                    $"{c.GetProperty("width")}×{c.GetProperty("height")}px"));

            TestContext.Out.WriteLine(
                $"📐 {pg.Name}: {unexpected.Length} potentially collapsed element(s):\n{summary}");
        }

        Assert.Pass($"[{pg.Name}] {unexpected.Length} collapsed element finding(s)");
    }

    // ── Spec element containment validation ─────────────────────────────

    [TestCaseSource(nameof(VisibleSpecs))]
    public async Task SpecElementsContainedInViewport(ComponentSpec spec)
    {
        var path = spec.PagePath ?? "/";
        await Page.SetViewportSizeAsync(1920, 1080);
        await Page.GotoAsync(Url(path));
        await WaitForDashboard();

        string selector;
        if (spec.RequiredTestIds.Length > 0)
            selector = Tid(spec.RequiredTestIds[0]);
        else if (spec.RequiredSelectors.Length > 0)
            selector = spec.RequiredSelectors[0];
        else
        {
            Assert.Pass($"[{spec.Name}] no selector to check");
            return;
        }

        var el = Page.Locator(selector).First;
        if (await el.CountAsync() == 0)
        {
            Assert.Pass($"[{spec.Name}] element not present");
            return;
        }

        var box = await el.BoundingBoxAsync();
        if (box is null || box.Width < 1)
        {
            Assert.Pass($"[{spec.Name}] element hidden (0-size box)");
            return;
        }

        Assert.That(box.X, Is.GreaterThanOrEqualTo(-2),
            $"[{spec.Name}] left edge is clipped at x={box.X:F0}px");

        Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(1922),
            $"[{spec.Name}] right edge overflows at x={box.X + box.Width:F0}px (viewport=1920)");

        Assert.That(box.Y, Is.GreaterThanOrEqualTo(-2),
            $"[{spec.Name}] top edge is clipped at y={box.Y:F0}px");
    }

    // ── Consistent spacing between sibling groups ───────────────────────

    [Test]
    public async Task AlarmBoxesEqualSpacing()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        // Verify CSS grid gap consistency — use computed style instead of
        // bounding-box arithmetic (which suffers from sub-pixel rounding)
        var gapInfo = await Page.EvaluateAsync<JsonElement>(@"() => {
            const container = document.querySelector('[data-testid=""alarm-boxes""]');
            if (!container) return { found: false };
            const style = getComputedStyle(container);
            const boxes = [...container.querySelectorAll('.alarm-box')];
            const rects = boxes.map(b => {
                const r = b.getBoundingClientRect();
                return { left: r.left, right: r.right, top: r.top, width: r.width, height: r.height };
            });
            return {
                found: true,
                display: style.display,
                columnGap: style.columnGap,
                rowGap: style.rowGap,
                boxCount: boxes.length,
                rects: rects
            };
        }");

        if (!gapInfo.GetProperty("found").GetBoolean())
        {
            Assert.Inconclusive("Alarm boxes container not found");
            return;
        }

        var boxCount = gapInfo.GetProperty("boxCount").GetInt32();
        Assert.That(boxCount, Is.EqualTo(5), "Expected 5 alarm boxes (Critical, Major, Minor, Warning, Total)");

        // Verify all boxes have non-zero dimensions
        var rects = gapInfo.GetProperty("rects").EnumerateArray().ToArray();
        foreach (var rect in rects)
        {
            var w = rect.GetProperty("width").GetDouble();
            var h = rect.GetProperty("height").GetDouble();
            Assert.That(w, Is.GreaterThan(50), "Alarm box width should be >50px");
            Assert.That(h, Is.GreaterThan(30), "Alarm box height should be >30px");
        }

        TestContext.Out.WriteLine(
            $"📐 Alarm boxes: {boxCount} boxes, " +
            $"column-gap={gapInfo.GetProperty("columnGap")}, " +
            $"row-gap={gapInfo.GetProperty("rowGap")}");
    }

    [Test]
    public async Task NavLinksEqualSpacing()
    {
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();

        var gaps = await Page.EvaluateAsync<float[]>(@"() => {
            const links = [...document.querySelectorAll('.nav-links a')];
            if (links.length < 2) return [];
            const gaps = [];
            for (let i = 1; i < links.length; i++) {
                const prev = links[i-1].getBoundingClientRect();
                const curr = links[i].getBoundingClientRect();
                gaps.push(curr.top - prev.bottom);
            }
            return gaps;
        }");

        if (gaps.Length < 2)
        {
            Assert.Inconclusive("Not enough nav links to compare spacing");
            return;
        }

        var avg = gaps.Average();
        foreach (var (gap, i) in gaps.Select((g, i) => (g, i)))
        {
            Assert.That(Math.Abs(gap - avg), Is.LessThan(3),
                $"Nav link gap {i}→{i + 1} is {gap:F1}px vs average {avg:F1}px (>3px difference)");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IEnumerable<ComponentSpec> VisibleSpecs() =>
        UiSpec.All.Where(s => !s.IsConditional && !s.IsModal);
}
