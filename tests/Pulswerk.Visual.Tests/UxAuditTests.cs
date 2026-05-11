using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// UX Audit — Automated heuristic analysis of every dashboard page.
/// Captures screenshots and generates a structured markdown report.
/// </summary>
[TestFixture]
public class UxAuditTests : BrowserTestBase
{
    private static readonly string AuditDir =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "ux-audit");

    private readonly List<PageAuditResult> _allAudits = [];

    [OneTimeSetUp]
    public void CreateAuditDir() => Directory.CreateDirectory(AuditDir);

    [OneTimeTearDown]
    public void WriteReport()
    {
        Directory.CreateDirectory(AuditDir);

        // JSON report
        var json = JsonSerializer.Serialize(_allAudits, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(AuditDir, "report.json"), json);

        // Markdown report
        var md = GenerateMarkdownReport(_allAudits);
        File.WriteAllText(Path.Combine(AuditDir, "report.md"), md);

        var total = _allAudits.Sum(a => a.Findings.Count);
        var warnings = _allAudits.Sum(a => a.Findings.Count(f => f.Severity == "warning"));
        TestContext.Out.WriteLine($"\n📋 UX Audit: {_allAudits.Count} pages, {total} findings ({warnings} warnings)");
    }

    // ── Per-page audits ─────────────────────────────────────────────────

    [TestCaseSource(typeof(Pages), nameof(Pages.All))]
    public async Task AuditPage(PageInfo pg)
    {
        await Page.GotoAsync(Url(pg.Path));
        await WaitForDashboard();
        await DisableAnimations();

        // Screenshot
        var screenshotName = $"{pg.Name.ToLower()}.png";
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(AuditDir, screenshotName),
            FullPage = true,
        });

        // Run heuristics
        var findings = new List<UxFinding>();
        findings.AddRange(await AuditTouchTargets(pg.Name));
        findings.AddRange(await AuditColorContrast(pg.Name));
        findings.AddRange(await AuditTextReadability(pg.Name));
        findings.AddRange(await AuditSpacingConsistency(pg.Name));
        findings.AddRange(await AuditEmptyStates(pg.Name));
        findings.AddRange(await AuditLoadingStates(pg.Name));
        findings.AddRange(await AuditFocusIndicators(pg.Name));
        findings.AddRange(await AuditOverflow(pg.Name));

        // Metrics
        var metrics = await Page.EvaluateAsync<Dictionary<string, object?>>("""
            (() => ({
                totalElements: document.querySelectorAll('*').length,
                interactiveElements: document.querySelectorAll('a,button,[onclick],input,select,textarea').length,
                images: document.querySelectorAll('img').length,
                forms: document.querySelectorAll('form').length,
                tables: document.querySelectorAll('table').length,
            }))()
        """);

        _allAudits.Add(new(pg.Name, pg.Path, screenshotName, findings, metrics));

        // Log inline
        if (findings.Count > 0)
        {
            TestContext.Out.WriteLine($"  📄 {pg.Name}: {findings.Count} finding(s)");
            foreach (var f in findings)
            {
                var icon = f.Severity switch { "critical" => "🔴", "warning" => "🟡", "suggestion" => "🔵", _ => "ℹ️" };
                TestContext.Out.WriteLine($"     {icon} [{f.Category}] {f.Message}");
            }
        }
    }

    // ── Responsive audit ────────────────────────────────────────────────

    [TestCase("tablet", 1024, 768)]
    [TestCase("laptop", 1366, 768)]
    public async Task AuditResponsive(string name, int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(Url("/"));
        await WaitForDashboard();
        await DisableAnimations();

        var screenshotName = $"responsive-{name}.png";
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(AuditDir, screenshotName),
            FullPage = true,
        });

        var findings = new List<UxFinding>();
        findings.AddRange(await AuditTouchTargets($"Responsive({name})"));
        findings.AddRange(await AuditOverflow($"Responsive({name})"));
        findings.AddRange(await AuditTextReadability($"Responsive({name})"));

        _allAudits.Add(new($"Responsive({name})", "/", screenshotName, findings,
            new() { ["viewportWidth"] = width, ["viewportHeight"] = height }));
    }

    // ── Heuristic checks ────────────────────────────────────────────────

    private async Task<List<UxFinding>> AuditTouchTargets(string pageName)
    {
        var issues = await Page.EvaluateAsync<TouchTarget[]>("""
            (() => {
                const els = document.querySelectorAll('a,button,[onclick],input,select,textarea');
                const issues = [];
                els.forEach(el => {
                    const r = el.getBoundingClientRect();
                    if (r.width > 0 && r.height > 0 && (r.width < 44 || r.height < 44))
                        issues.push({ tag: el.tagName.toLowerCase(), text: (el.textContent||'').trim().slice(0,40), width: Math.round(r.width), height: Math.round(r.height) });
                });
                return issues;
            })()
        """);

        if (issues.Length == 0) return [];
        return [new(pageName, "Touch Targets", "suggestion",
            $"{issues.Length} interactive element(s) are smaller than the 44×44px minimum recommended by WCAG 2.5.8.",
            $"Worst offenders: {string.Join(", ", issues.Take(3).Select(t => $"<{t.Tag}> \"{t.Text}\" ({t.Width}×{t.Height}px)"))}")];
    }

    private async Task<List<UxFinding>> AuditColorContrast(string pageName)
    {
        var issues = await Page.EvaluateAsync<ContrastIssue[]>("""
            (() => {
                function lum(r,g,b) { const [rs,gs,bs]=[r,g,b].map(c=>{c/=255;return c<=0.03928?c/12.92:Math.pow((c+0.055)/1.055,2.4);}); return 0.2126*rs+0.7152*gs+0.0722*bs; }
                function parse(c) { const m=c.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/); return m?[+m[1],+m[2],+m[3]]:null; }
                const issues = [];
                document.querySelectorAll('p,span,a,h1,h2,h3,h4,h5,h6,td,th,label,div,li').forEach(el => {
                    const s=getComputedStyle(el), t=(el.textContent||'').trim();
                    if(!t||t.length>80||s.display==='none'||s.visibility==='hidden') return;
                    const fg=parse(s.color), bg=parse(s.backgroundColor);
                    if(!fg||!bg) return;
                    const l1=lum(fg[0],fg[1],fg[2]), l2=lum(bg[0],bg[1],bg[2]);
                    const ratio=(Math.max(l1,l2)+0.05)/(Math.min(l1,l2)+0.05);
                    if(ratio<4.5&&ratio>1) issues.push({text:t.slice(0,30),ratio:Math.round(ratio*100)/100,fg:s.color,bg:s.backgroundColor});
                });
                const seen=new Set();
                return issues.filter(i=>{const k=`${Math.round(i.ratio)}-${i.fg}`;if(seen.has(k))return false;seen.add(k);return true;}).slice(0,5);
            })()
        """);

        if (issues.Length == 0) return [];
        return [new(pageName, "Color Contrast", "warning",
            $"{issues.Length} text element(s) may not meet WCAG AA contrast ratio (4.5:1).",
            string.Join("\n", issues.Select(i => $"\"{i.Text}\" — ratio {i.Ratio}:1 (fg: {i.Fg}, bg: {i.Bg})")))];
    }

    private async Task<List<UxFinding>> AuditTextReadability(string pageName)
    {
        var count = await Page.EvaluateAsync<int>("""
            (() => {
                let c=0;
                document.querySelectorAll('*').forEach(el => {
                    const s=getComputedStyle(el), t=(el.textContent||'').trim();
                    if(!t||s.display==='none') return;
                    const size=parseFloat(s.fontSize);
                    if(size>0&&size<11&&el.children.length===0) c++;
                });
                return c;
            })()
        """);

        if (count == 0) return [];
        return [new(pageName, "Text Readability", "suggestion",
            $"{count} element(s) use font size below 11px, which may be hard to read.",
            "Increase the minimum body font size to at least 12px.")];
    }

    private async Task<List<UxFinding>> AuditSpacingConsistency(string pageName)
    {
        var uniquePaddings = await Page.EvaluateAsync<int>("""
            (() => {
                const paddings=new Set();
                document.querySelectorAll('.glass,.alarm-box,.conn-card,.point-item,.alarm-item').forEach(el => {
                    paddings.add(getComputedStyle(el).padding);
                });
                return paddings.size;
            })()
        """);

        if (uniquePaddings <= 5) return [];
        return [new(pageName, "Spacing Consistency", "info",
            $"{uniquePaddings} different padding values found across card elements.",
            "Consider establishing a spacing scale and using CSS custom properties.")];
    }

    private async Task<List<UxFinding>> AuditEmptyStates(string pageName)
    {
        var count = await Page.EvaluateAsync<int>("""
            document.querySelectorAll('[id*="empty"],[id*="Empty"],.empty-state,.no-selection,.no-favorites').length
        """);

        if (count > 0) return [];
        return [new(pageName, "Empty States", "suggestion",
            "No empty-state handler detected on this page.",
            "Add a friendly empty-state message with illustration and call-to-action.")];
    }

    private async Task<List<UxFinding>> AuditLoadingStates(string pageName)
    {
        var result = await Page.EvaluateAsync<LoadingCheck>("""
            (() => ({
                hasLoader: document.querySelectorAll('.loading,.spinner,[class*="skeleton"],[class*="loading"],[class*="spinner"],.fa-spinner').length > 0,
                hasDynamic: document.querySelectorAll('[onclick*="fetch"],[onclick*="load"]').length > 0,
            }))()
        """);

        if (!result.HasDynamic || result.HasLoader) return [];
        return [new(pageName, "Loading States", "suggestion",
            "Page has dynamically loaded content but no visible loading indicator.",
            "Add skeleton loaders or a spinner while async data is being fetched.")];
    }

    private async Task<List<UxFinding>> AuditFocusIndicators(string pageName)
    {
        var result = await Page.EvaluateAsync<FocusCheck>("""
            (() => {
                const els=document.querySelectorAll('a,button,input,select,textarea');
                let no=0;
                els.forEach(el => { const s=getComputedStyle(el); if(s.outlineStyle==='none'&&s.boxShadow==='none') no++; });
                return { total: els.length, noOutline: no };
            })()
        """);

        if (result.NoOutline <= result.Total * 0.5 || result.Total <= 3) return [];
        return [new(pageName, "Focus Indicators", "warning",
            $"{result.NoOutline}/{result.Total} interactive elements have no visible focus indicator.",
            "Add :focus-visible styles with a visible ring for keyboard navigation.")];
    }

    private async Task<List<UxFinding>> AuditOverflow(string pageName)
    {
        var count = await Page.EvaluateAsync<int>("""
            (() => {
                const w=document.documentElement.clientWidth;
                let c=0;
                document.querySelectorAll('*').forEach(el => { if(el.getBoundingClientRect().right>w+2) c++; });
                return c;
            })()
        """);

        if (count == 0) return [];
        return [new(pageName, "Horizontal Overflow", "warning",
            $"{count} element(s) extend beyond the viewport width.",
            "Check for fixed-width elements or missing overflow handling.")];
    }

    // ── Report generation ───────────────────────────────────────────────

    private static string GenerateMarkdownReport(List<PageAuditResult> audits)
    {
        var allFindings = audits.SelectMany(a => a.Findings).ToList();
        var criticals = allFindings.Count(f => f.Severity == "critical");
        var warnings = allFindings.Count(f => f.Severity == "warning");
        var suggestions = allFindings.Count(f => f.Severity == "suggestion");
        var infos = allFindings.Count(f => f.Severity == "info");

        var md = $"""
            # Pulswerk Dashboard — UX Audit Report

            **Generated:** {DateTime.UtcNow:O}
            **Pages audited:** {audits.Count}
            **Total findings:** {allFindings.Count} (🔴 {criticals} critical, 🟡 {warnings} warnings, 🔵 {suggestions} suggestions, ℹ️ {infos} info)

            ---

            ## Summary by Page

            | Page | Findings | Elements | Interactive |
            |------|----------|----------|-------------|

            """;

        foreach (var audit in audits)
        {
            var els = audit.Metrics.GetValueOrDefault("totalElements", "-");
            var inter = audit.Metrics.GetValueOrDefault("interactiveElements", "-");
            md += $"| {audit.Page} | {audit.Findings.Count} | {els} | {inter} |\n";
        }
        md += "\n";

        // Findings by category
        var categories = allFindings.Select(f => f.Category).Distinct();
        md += "## Findings by Category\n\n";
        foreach (var cat in categories)
        {
            md += $"### {cat}\n\n";
            foreach (var f in allFindings.Where(f => f.Category == cat))
            {
                var icon = f.Severity switch { "critical" => "🔴", "warning" => "🟡", "suggestion" => "🔵", _ => "ℹ️" };
                md += $"{icon} **{f.Page}**: {f.Message}\n> {f.Recommendation.Replace("\n", "\n> ")}\n\n";
            }
        }

        // Per-page detail
        md += "## Page Details\n\n";
        foreach (var audit in audits)
        {
            md += $"### {audit.Page} (`{audit.Url}`)\n\n";
            md += $"![{audit.Page}](./{audit.Screenshot})\n\n";
            if (audit.Findings.Count == 0)
            {
                md += "✅ No issues found.\n\n";
            }
            else
            {
                foreach (var f in audit.Findings)
                {
                    var icon = f.Severity switch { "critical" => "🔴", "warning" => "🟡", "suggestion" => "🔵", _ => "ℹ️" };
                    md += $"- {icon} **[{f.Category}]** {f.Message}\n  - 💡 {f.Recommendation.Replace("\n", "\n  - ")}\n";
                }
                md += "\n";
            }
        }

        return md;
    }
}

// ── Models ──────────────────────────────────────────────────────────────────

public record UxFinding(string Page, string Category, string Severity, string Message, string Recommendation);
public record PageAuditResult(string Page, string Url, string Screenshot, List<UxFinding> Findings, Dictionary<string, object?> Metrics);

// JS interop DTOs — must have parameterless constructors for Playwright deserialization
file class TouchTarget
{
    public string Tag { get; set; } = "";
    public string Text { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}

file class ContrastIssue
{
    public string Text { get; set; } = "";
    public double Ratio { get; set; }
    public string Fg { get; set; } = "";
    public string Bg { get; set; } = "";
}

file class LoadingCheck
{
    public bool HasLoader { get; set; }
    public bool HasDynamic { get; set; }
}

file class FocusCheck
{
    public int Total { get; set; }
    public int NoOutline { get; set; }
}
