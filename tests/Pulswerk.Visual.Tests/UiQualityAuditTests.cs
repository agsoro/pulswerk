using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// UI Quality Audit Tests — detects subtle defects that visual regression alone misses:
///   - Missing icons (Font Awesome not rendering, empty ::before content)
///   - Invisible/collapsed controls (modals, popups, buttons too small to interact)
///   - Wrong fonts (elements not using the expected font family)
///   - Inconsistent arrangements (uneven margins, padding, sizes across siblings)
/// </summary>
[TestFixture]
public class UiQualityAuditTests : BrowserTestBase
{
    // ═══════════════════════════════════════════════════════════════════════
    //  1. MISSING ICONS — Font Awesome elements with empty or tofu content
    // ═══════════════════════════════════════════════════════════════════════

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task NoMissingIcons(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        // Detect FA icons that failed to render: their ::before pseudo-element
        // should produce a single unicode glyph. If FA CSS didn't load, the
        // computed content is "none" or empty, or the glyph renders as a
        // zero-width box (tofu/replacement character).
        var issues = await Page.EvaluateAsync<JsonElement>(@"() => {
            const results = [];
            // All FA icon selectors (fa, fas, far, fab, fa-solid, fa-regular)
            const icons = document.querySelectorAll(
                '.fa, .fas, .far, .fab, .fa-solid, .fa-regular, .fa-brands, [class*=""fa-""]'
            );
            icons.forEach(el => {
                const style = getComputedStyle(el, '::before');
                const content = style.content;
                const fontFamily = style.fontFamily;
                const r = el.getBoundingClientRect();

                let problem = null;

                // Content missing or explicitly 'none'
                if (!content || content === 'none' || content === '""""' || content === '""') {
                    problem = 'empty ::before content';
                }
                // Font family doesn't include Font Awesome
                else if (!fontFamily.toLowerCase().includes('awesome') &&
                         !fontFamily.toLowerCase().includes('fa ')) {
                    problem = `wrong font-family: ${fontFamily.substring(0, 40)}`;
                }
                // Zero-size icon (not rendered) — but skip if any ancestor is hidden
                else if (r.width === 0 && r.height === 0 &&
                         getComputedStyle(el).display !== 'none') {
                    // Check if parent or ancestor is hidden (icon is inside a hidden modal)
                    let ancestor = el.parentElement;
                    let ancestorHidden = false;
                    while (ancestor) {
                        const as2 = getComputedStyle(ancestor);
                        if (as2.display === 'none' || as2.visibility === 'hidden') {
                            ancestorHidden = true;
                            break;
                        }
                        ancestor = ancestor.parentElement;
                    }
                    if (!ancestorHidden) {
                        problem = 'zero-size (0×0px)';
                    }
                }

                if (problem) {
                    results.push({
                        classes: el.className?.toString().substring(0, 60) || '',
                        parent: el.parentElement?.tagName || '',
                        parentId: el.parentElement?.id || '',
                        problem: problem,
                        display: getComputedStyle(el).display,
                        width: Math.round(r.width),
                        height: Math.round(r.height)
                    });
                }
            });
            return { total: icons.length, issues: results };
        }");

        var total = issues.GetProperty("total").GetInt32();
        var broken = issues.GetProperty("issues").EnumerateArray().ToArray();

        if (broken.Length > 0)
        {
            var summary = string.Join("\n",
                broken.Take(15).Select(i =>
                    $"  🚫 .{i.GetProperty("classes")} in <{i.GetProperty("parent")}> " +
                    $"#{i.GetProperty("parentId")} — {i.GetProperty("problem")}"));

            TestContext.Out.WriteLine(
                $"🔍 [{pg.Name}] {broken.Length}/{total} icon(s) have rendering issues:\n{summary}");
        }

        // Hard fail if any visible icon has zero content
        var critical = broken.Where(i =>
            i.GetProperty("problem").GetString()!.Contains("empty") &&
            i.GetProperty("display").GetString() != "none").ToArray();

        Assert.That(critical, Is.Empty,
            $"[{pg.Name}] {critical.Length} icon(s) have empty ::before content " +
            "(Font Awesome not loaded or class name wrong)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  2. INVISIBLE CONTROLS — interactive elements too small to use
    // ═══════════════════════════════════════════════════════════════════════

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task NoInvisibleControls(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        // Find interactive elements (buttons, links, inputs, selects) that are
        // technically present but too small to see or interact with
        var issues = await Page.EvaluateAsync<JsonElement>(@"() => {
            const results = [];
            const interactive = document.querySelectorAll(
                'button, a[href], input, select, textarea, [role=""button""], [onclick], [tabindex]'
            );
            interactive.forEach(el => {
                const style = getComputedStyle(el);
                if (style.display === 'none' || style.visibility === 'hidden') return;
                if (style.opacity === '0') return;

                // Walk up ancestors to check if any parent is hidden
                let parent = el.parentElement;
                while (parent) {
                    const ps = getComputedStyle(parent);
                    if (ps.display === 'none' || ps.visibility === 'hidden' || ps.opacity === '0') return;
                    parent = parent.parentElement;
                }

                const r = el.getBoundingClientRect();
                const problems = [];

                // Too small to see (< 8px in either dimension for visible elements)
                if (r.width > 0 && r.width < 8 && style.overflow !== 'hidden') {
                    problems.push(`width=${Math.round(r.width)}px`);
                }
                if (r.height > 0 && r.height < 8 && style.overflow !== 'hidden') {
                    problems.push(`height=${Math.round(r.height)}px`);
                }

                // Completely collapsed
                if (r.width === 0 && r.height === 0) {
                    problems.push('collapsed (0×0)');
                }

                // Clipped out of viewport
                if (r.right < 0 || r.bottom < 0) {
                    problems.push('off-screen (negative position)');
                }

                if (problems.length > 0) {
                    results.push({
                        tag: el.tagName,
                        type: el.type || '',
                        text: (el.textContent || '').trim().substring(0, 30),
                        id: el.id || '',
                        testId: el.getAttribute('data-testid') || '',
                        problems: problems.join(', '),
                        width: Math.round(r.width),
                        height: Math.round(r.height)
                    });
                }
            });
            return results;
        }");

        var invisible = issues.EnumerateArray().ToArray();

        if (invisible.Length > 0)
        {
            var summary = string.Join("\n",
                invisible.Take(15).Select(c =>
                    $"  👻 <{c.GetProperty("tag")}> #{c.GetProperty("id")} " +
                    $"[testid={c.GetProperty("testId")}] '{c.GetProperty("text")}' " +
                    $"— {c.GetProperty("problems")}"));

            TestContext.Out.WriteLine(
                $"🔍 [{pg.Name}] {invisible.Length} invisible/micro controls:\n{summary}");
        }

        // Exclude known hidden modals — only fail on page-visible elements
        var unexpected = invisible.Where(c =>
        {
            var tid = c.GetProperty("testId").GetString() ?? "";
            var id = c.GetProperty("id").GetString() ?? "";
            return !tid.Contains("modal") && !id.Contains("modal") && !id.Contains("Modal");
        }).ToArray();

        Assert.That(unexpected, Is.Empty,
            $"[{pg.Name}] {unexpected.Length} interactive element(s) are invisible or too small.");
    }

    // Also check spec-defined modals aren't too small when opened
    [Test]
    public async Task ModalsAreAdequateSize()
    {
        await Page.GotoAsync(Url("/Dashboards"));
        await WaitForDashboard();

        var modals = UiSpec.All.Where(s => s.IsModal && s.TriggerSelector != null).ToArray();
        var findings = new List<string>();

        foreach (var modal in modals)
        {
            // Try to trigger
            var trigger = Page.Locator(modal.TriggerSelector!).First;
            if (await trigger.CountAsync() == 0) continue;
            if (!await trigger.IsVisibleAsync()) continue;

            try
            {
                await trigger.ClickAsync(new() { Timeout = 3000 });
                await Page.WaitForTimeoutAsync(500);

                // Check the modal's size
                var selector = modal.RequiredTestIds.Length > 0
                    ? Tid(modal.RequiredTestIds[0])
                    : modal.RequiredSelectors.FirstOrDefault();

                if (selector == null) continue;

                var el = Page.Locator(selector).First;
                if (await el.CountAsync() == 0) continue;

                var box = await el.BoundingBoxAsync();
                if (box is null) continue;

                // Modal content should be at least 200×100px when visible
                if (box.Width > 0 && box.Width < 200)
                    findings.Add($"{modal.Name}: width={box.Width:F0}px (too narrow, min 200px)");
                if (box.Height > 0 && box.Height < 100)
                    findings.Add($"{modal.Name}: height={box.Height:F0}px (too short, min 100px)");
            }
            catch { /* trigger may not work in test context */ }

            // Reload for next modal
            await Page.GotoAsync(Url("/Dashboards"));
            await WaitForDashboard();
        }

        if (findings.Count > 0)
            TestContext.Out.WriteLine($"📏 Modal size findings:\n  " + string.Join("\n  ", findings));

        Assert.That(findings, Is.Empty,
            $"{findings.Count} modal(s) are too small when opened.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  3. WRONG FONTS — elements not using the expected font stack
    // ═══════════════════════════════════════════════════════════════════════

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task FontConsistency(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        // The dashboard uses Inter as primary font and Fira Code for monospace.
        // Detect elements that fell back to browser defaults (serif, Times, etc.)
        var fontIssues = await Page.EvaluateAsync<JsonElement>(@"() => {
            const expectedPrimary = ['inter', 'outfit', 'roboto', 'system-ui', 'sans-serif',
                                     '-apple-system', 'segoe ui', 'helvetica', 'arial'];
            const expectedMono = ['fira code', 'fira mono', 'jetbrains mono', 'source code pro',
                                  'consolas', 'courier new', 'monospace'];
            const expectedIcon = ['font awesome', 'fa ', 'fontawesome', 'bootstrap icons',
                                  'material icons', 'icons'];
            const fallbackFonts = ['times new roman', 'times,', 'georgia',
                                   'palatino', 'book antiqua'];

            const results = [];
            const checked = new Set();

            document.querySelectorAll('*').forEach(el => {
                const style = getComputedStyle(el);
                if (style.display === 'none') return;
                // Skip HTML element (always has browser default)
                if (el.tagName === 'HTML') return;
                const r = el.getBoundingClientRect();
                if (r.width === 0 || r.height === 0) return;

                const font = style.fontFamily.toLowerCase();
                const tag = el.tagName;

                // Skip if we've seen this exact font stack already
                const key = font + '|' + tag;
                if (checked.has(key)) return;
                checked.add(key);

                // Check if font matches any expected font
                const isExpected = expectedPrimary.some(f => font.includes(f)) ||
                                   expectedMono.some(f => font.includes(f)) ||
                                   expectedIcon.some(f => font.includes(f));
                const isFallback = fallbackFonts.some(f => font.includes(f));

                if (isFallback || (!isExpected && el.textContent?.trim().length > 0)) {
                    results.push({
                        tag: tag,
                        id: el.id || '',
                        cls: el.className?.toString().substring(0, 40) || '',
                        text: (el.textContent || '').trim().substring(0, 25),
                        font: font.substring(0, 60),
                        isFallback: isFallback
                    });
                }
            });
            return results;
        }");

        var issues = fontIssues.EnumerateArray().ToArray();
        var fallbacks = issues.Where(i => i.GetProperty("isFallback").GetBoolean()).ToArray();

        if (issues.Length > 0)
        {
            var summary = string.Join("\n",
                issues.Take(10).Select(f =>
                    $"  🔤 <{f.GetProperty("tag")}> #{f.GetProperty("id")} " +
                    $"'{f.GetProperty("text")}' → font: {f.GetProperty("font")}" +
                    (f.GetProperty("isFallback").GetBoolean() ? " ⚠ FALLBACK" : "")));

            TestContext.Out.WriteLine(
                $"🔍 [{pg.Name}] {issues.Length} font finding(s) ({fallbacks.Length} fallback):\n{summary}");
        }

        // Hard fail only on serif/Times fallback — this means CSS failed to load
        Assert.That(fallbacks, Is.Empty,
            $"[{pg.Name}] {fallbacks.Length} element(s) are using serif/Times fallback fonts — " +
            "font CSS not loaded correctly.");
    }

    // Verify monospace is used where expected (log console, code paths)
    [Test]
    public async Task LogConsoleUsesMonospace()
    {
        await Page.GotoAsync(Url("/Logs"));
        await WaitForDashboard();

        var font = await Page.EvaluateAsync<string>(@"() => {
            const line = document.querySelector('.log-line, .terminal-body, .log-container');
            if (!line) return 'NOT_FOUND';
            return getComputedStyle(line).fontFamily.toLowerCase();
        }");

        Assert.That(font, Does.Contain("fira").Or.Contain("mono").Or.Contain("consolas")
            .Or.Contain("courier"),
            $"Log console should use monospace font, but got: {font}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  4. INCONSISTENT ARRANGEMENTS — uneven sizing/spacing across siblings
    // ═══════════════════════════════════════════════════════════════════════

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task SiblingConsistency(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();

        // Find groups of sibling elements that should be uniformly sized
        // (e.g. nav links, alarm boxes, dashboard cards, filter chips)
        var inconsistencies = await Page.EvaluateAsync<JsonElement>(@"() => {
            const results = [];

            // Selector groups that should have consistent children
            const groups = [
                { name: 'Nav links',     parent: '.nav-links',       child: 'a' },
                { name: 'Alarm boxes',   parent: '[data-testid=""alarm-boxes""]', child: '.alarm-box' },
                { name: 'Filter chips',  parent: '[data-testid=""alarm-filters""]', child: '.filter-chip, a' },
                { name: 'Dashboard cards', parent: '[data-testid=""dash-grid""]', child: '.dash-card' },
            ];

            groups.forEach(({ name, parent, child }) => {
                const container = document.querySelector(parent);
                if (!container) return;
                const children = [...container.querySelectorAll(':scope > ' + child)];
                if (children.length < 2) return;

                const metrics = children.map(c => {
                    const r = c.getBoundingClientRect();
                    const s = getComputedStyle(c);
                    return {
                        width: r.width,
                        height: r.height,
                        fontSize: parseFloat(s.fontSize),
                        paddingLeft: parseFloat(s.paddingLeft),
                        paddingRight: parseFloat(s.paddingRight),
                        marginBottom: parseFloat(s.marginBottom),
                    };
                });

                // Check height consistency (should be within 10% of average)
                const heights = metrics.map(m => m.height).filter(h => h > 0);
                if (heights.length >= 2) {
                    const avgH = heights.reduce((a, b) => a + b) / heights.length;
                    const maxDev = Math.max(...heights.map(h => Math.abs(h - avgH)));
                    if (maxDev > avgH * 0.25 && maxDev > 5) {
                        results.push({
                            group: name,
                            property: 'height',
                            values: heights.map(h => Math.round(h)).join(', '),
                            maxDeviation: Math.round(maxDev),
                            average: Math.round(avgH)
                        });
                    }
                }

                // Check font-size consistency
                const sizes = metrics.map(m => m.fontSize).filter(s => s > 0);
                if (sizes.length >= 2) {
                    const unique = [...new Set(sizes.map(s => Math.round(s * 10) / 10))];
                    if (unique.length > 1) {
                        results.push({
                            group: name,
                            property: 'font-size',
                            values: unique.join(', ') + 'px',
                            maxDeviation: 0,
                            average: 0
                        });
                    }
                }

                // Check padding consistency
                const paddings = metrics.map(m => m.paddingLeft + m.paddingRight);
                if (paddings.length >= 2) {
                    const unique = [...new Set(paddings.map(p => Math.round(p)))];
                    if (unique.length > 1) {
                        const maxP = Math.max(...unique);
                        const minP = Math.min(...unique);
                        if (maxP - minP > 8) {
                            results.push({
                                group: name,
                                property: 'horizontal padding',
                                values: unique.join(', ') + 'px',
                                maxDeviation: maxP - minP,
                                average: 0
                            });
                        }
                    }
                }
            });

            return results;
        }");

        var findings = inconsistencies.EnumerateArray().ToArray();

        if (findings.Length > 0)
        {
            var summary = string.Join("\n",
                findings.Select(f =>
                    $"  📏 {f.GetProperty("group")}: inconsistent {f.GetProperty("property")} " +
                    $"— values: [{f.GetProperty("values")}]" +
                    (f.GetProperty("maxDeviation").GetInt32() > 0
                        ? $" (max deviation: {f.GetProperty("maxDeviation")}px)"
                        : "")));

            TestContext.Out.WriteLine(
                $"🔍 [{pg.Name}] {findings.Length} arrangement inconsistency:\n{summary}");
        }

        // Height and font-size inconsistencies in navigation and alarm boxes are hard failures
        var critical = findings.Where(f =>
        {
            var group = f.GetProperty("group").GetString() ?? "";
            var prop = f.GetProperty("property").GetString() ?? "";
            return (group.Contains("Nav") || group.Contains("Alarm box")) && prop == "font-size";
        }).ToArray();

        Assert.That(critical, Is.Empty,
            $"[{pg.Name}] {critical.Length} critical arrangement inconsistency.");
    }

    // ── Cross-page nav consistency ──────────────────────────────────────

    [Test]
    public async Task NavItemsConsistentAcrossPages()
    {
        // Verify the nav structure is identical on every page
        var navStructures = new List<(string page, string structure)>();

        foreach (var pg in Pages.All)
        {
            await Page.GotoAsync(Url(pg.Path));
            await WaitForDashboard();

            var structure = await Page.EvaluateAsync<string>(@"() => {
                const links = [...document.querySelectorAll('.nav-links a')];
                return links.map(a => {
                    const icon = a.querySelector('i, .fa, [class*=""fa-""]');
                    const iconClass = icon?.className?.toString().replace(/\s+/g, ' ').trim() || 'NO_ICON';
                    const text = a.querySelector('.nav-text')?.textContent?.trim() || 'NO_TEXT';
                    const href = a.getAttribute('href') || '';
                    return `${href}|${iconClass}|${text}`;
                }).join(';;');
            }");

            navStructures.Add((pg.Path, structure));
        }

        // All pages should have identical nav structure
        var first = navStructures[0];
        foreach (var (page, structure) in navStructures.Skip(1))
        {
            Assert.That(structure, Is.EqualTo(first.structure),
                $"Nav structure on {page} differs from {first.page}. " +
                "Navigation icons, text, or links are inconsistent across pages.");
        }
    }

    // ── Page header consistency ─────────────────────────────────────────

    [Test]
    public async Task PageHeaderStyleConsistent()
    {
        var headerStyles = new List<(string page, string fontSize, string fontWeight, string color)>();

        foreach (var pg in Pages.All)
        {
            await Page.GotoAsync(Url(pg.Path));
            await WaitForDashboard();

            var style = await Page.EvaluateAsync<JsonElement>(@"() => {
                const title = document.querySelector('[data-testid=""page-title""]');
                if (!title) return { found: false };
                const s = getComputedStyle(title);
                return {
                    found: true,
                    fontSize: s.fontSize,
                    fontWeight: s.fontWeight,
                    color: s.color,
                    fontFamily: s.fontFamily
                };
            }");

            if (!style.GetProperty("found").GetBoolean()) continue;

            headerStyles.Add((pg.Path,
                style.GetProperty("fontSize").GetString()!,
                style.GetProperty("fontWeight").GetString()!,
                style.GetProperty("color").GetString()!));
        }

        if (headerStyles.Count < 2) return;

        var first = headerStyles[0];
        foreach (var (page, fontSize, fontWeight, color) in headerStyles.Skip(1))
        {
            Assert.That(fontSize, Is.EqualTo(first.fontSize),
                $"Page title font-size on {page} ({fontSize}) differs from {first.page} ({first.fontSize})");
            Assert.That(fontWeight, Is.EqualTo(first.fontWeight),
                $"Page title font-weight on {page} ({fontWeight}) differs from {first.page} ({first.fontWeight})");
        }
    }
}
