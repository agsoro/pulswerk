using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// UX audit for post-interaction states — modals, edit modes, dropdowns, hovers.
/// Captures screenshots and runs heuristics on the revealed UI.
/// </summary>
[TestFixture]
public class UxAuditInteractionTests : BrowserTestBase
{
    private static readonly string AuditDir =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "ux-audit");

    private readonly List<InteractionAudit> _report = [];

    [OneTimeSetUp]
    public void CreateAuditDir() => Directory.CreateDirectory(AuditDir);

    [OneTimeTearDown]
    public void WriteInteractionReport()
    {
        Directory.CreateDirectory(AuditDir);
        var json = JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(AuditDir, "interactions.json"), json);

        // Append interaction section to markdown report
        var md = "\n\n# Interaction State Audit\n\n";
        md += $"**Scenarios tested:** {_report.Count}\n";
        md += $"**Total findings:** {_report.Sum(r => r.Findings.Count)}\n\n";

        foreach (var r in _report)
        {
            md += $"## {r.Scenario}\n\n![{r.Scenario}](./{r.Screenshot})\n\n";
            if (r.Findings.Count == 0)
            {
                md += "✅ No issues.\n\n";
            }
            else
            {
                foreach (var f in r.Findings)
                {
                    var icon = f.Severity switch { "critical" => "🔴", "warning" => "🟡", "suggestion" => "🔵", _ => "ℹ️" };
                    md += $"- {icon} **[{f.Category}]** {f.Message}\n  - 💡 {f.Recommendation}\n";
                }
                md += "\n";
            }
        }

        var mdPath = Path.Combine(AuditDir, "report.md");
        if (File.Exists(mdPath))
            File.AppendAllText(mdPath, md);
        else
            File.WriteAllText(mdPath, md);

        var total = _report.Sum(r => r.Findings.Count);
        TestContext.Out.WriteLine($"\n📋 Interaction Audit: {_report.Count} scenarios, {total} findings");
    }

    // ── Interaction scenarios ───────────────────────────────────────────

    [Test]
    public async Task SidebarExpandedState()
    {
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();
        await DisableAnimations();

        await Page.Locator(Tid("sidebar")).HoverAsync();
        await Page.WaitForTimeoutAsync(300);

        var ss = await Screenshot("sidebar-hover");
        var findings = await AuditArea("Sidebar (expanded)", Tid("sidebar"));
        _report.Add(new("Sidebar Hover Expanded", ss, findings));
    }

    [Test]
    public async Task ConnectionsSelectCard()
    {
        await Page.GotoAsync(Url("/plswk/Connections"));
        await WaitForDashboard();
        await DisableAnimations();

        var firstCard = Page.Locator(".conn-card").First;
        if (await firstCard.CountAsync() > 0)
        {
            await firstCard.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        var ss = await Screenshot("connections-selected");
        var findings = await AuditArea("Connections (card selected)", Tid("conn-detail"));
        _report.Add(new("Connections: Card Selected", ss, findings));
    }

    [Test]
    public async Task DashboardsCreateModal()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var btn = Page.Locator(Tid("dash-create-btn"));
        if (await btn.CountAsync() > 0)
        {
            await btn.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        var ss = await Screenshot("dashboards-create-modal");
        var findings = await AuditArea("Dashboards (Create Modal)", Tid("create-dash-modal"));
        _report.Add(new("Dashboards: Create Modal", ss, findings));
    }

    [Test]
    public async Task DashboardsEditModeToolbar()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var firstCard = Page.Locator(".dash-card").First;
        if (await firstCard.CountAsync() > 0)
        {
            await firstCard.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            var editBtn = Page.Locator(Tid("dash-edit-btn"));
            if (await editBtn.IsVisibleAsync())
            {
                await editBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
            }
        }

        var ss = await Screenshot("dashboards-edit-mode");
        var findings = await AuditArea("Dashboards (Edit Mode)", Tid("dash-toolbar"));
        _report.Add(new("Dashboards: Edit Mode Toolbar", ss, findings));
    }

    [Test]
    public async Task DashboardsTimewindowDropdown()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var firstCard = Page.Locator(".dash-card").First;
        if (await firstCard.CountAsync() > 0)
        {
            await firstCard.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            var tw = Page.Locator(Tid("tw-selector"));
            if (await tw.IsVisibleAsync())
            {
                await tw.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
            }
        }

        var ss = await Screenshot("dashboards-timewindow");
        var findings = await AuditArea("Dashboards (Timewindow)", "#twDropdown");
        _report.Add(new("Dashboards: Timewindow Dropdown", ss, findings));
    }

    [Test]
    public async Task DashboardsAddWidgetModal()
    {
        await Page.GotoAsync(Url("/plswk/Dashboards"));
        await WaitForDashboard();
        await DisableAnimations();

        var firstCard = Page.Locator(".dash-card").First;
        if (await firstCard.CountAsync() > 0)
        {
            await firstCard.ClickAsync();
            await Page.WaitForTimeoutAsync(500);

            var editBtn = Page.Locator(Tid("dash-edit-btn"));
            if (await editBtn.IsVisibleAsync())
            {
                await editBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
            }

            var addBtn = Page.Locator(Tid("dash-add-widget-btn"));
            if (await addBtn.IsVisibleAsync())
            {
                await addBtn.ClickAsync();
                await Page.WaitForTimeoutAsync(300);
            }
        }

        var ss = await Screenshot("dashboards-add-widget");
        var findings = await AuditArea("Dashboards (Add Widget)", Tid("add-widget-modal"));
        _report.Add(new("Dashboards: Add Widget Modal", ss, findings));
    }

    [Test]
    public async Task AlarmsAcknowledgeModal()
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

        var ss = await Screenshot("alarms-ack-modal");
        var modal = Page.Locator(Tid("ack-modal"));
        var findings = await modal.IsVisibleAsync()
            ? await AuditArea("Alarms (Ack Modal)", Tid("ack-modal"))
            : [];
        _report.Add(new("Alarms: Acknowledge Modal", ss, findings));
    }

    [Test]
    public async Task AlarmsFilterBySeverity()
    {
        await Page.GotoAsync(Url("/plswk/Alarms?severity=CRITICAL"));
        await WaitForDashboard();
        await DisableAnimations();

        var ss = await Screenshot("alarms-filtered-critical");
        var findings = await AuditArea("Alarms (Filtered: Critical)");
        _report.Add(new("Alarms: Filtered by Critical", ss, findings));
    }

    [Test]
    public async Task AssetsTreeNodeInteraction()
    {
        await Page.GotoAsync(Url("/plswk/Assets"));
        await WaitForDashboard();
        await DisableAnimations();

        var node = Page.Locator(".tree-toggle, .node-toggle, [onclick*='toggle']").First;
        if (await node.CountAsync() > 0)
        {
            await node.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
        }

        var ss = await Screenshot("assets-expanded");
        var findings = await AuditArea("Assets (Node Expanded)");
        _report.Add(new("Assets: Tree Node Expanded", ss, findings));
    }

    [Test]
    public async Task AlarmBoxHoverStates()
    {
        await Page.GotoAsync(Url("/plswk/"));
        await WaitForDashboard();

        await Page.Locator("#box-critical").HoverAsync();
        await Page.WaitForTimeoutAsync(300);

        var ss = await Screenshot("alarm-box-hover");
        var findings = await AuditArea("Dashboard (Alarm Hover)", Tid("alarm-boxes"));
        _report.Add(new("Dashboard: Alarm Box Hover", ss, findings));
    }

    // ── Shared helpers ──────────────────────────────────────────────────

    private async Task<string> Screenshot(string name)
    {
        Directory.CreateDirectory(AuditDir);
        var file = $"interaction-{name}.png";
        await Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(AuditDir, file),
            FullPage = true,
        });
        return file;
    }

    /// <summary>
    /// Runs touch target, contrast, and focus indicator checks
    /// scoped to an optional CSS selector.
    /// </summary>
    private async Task<List<UxFinding>> AuditArea(string context, string? selector = null)
    {
        var findings = new List<UxFinding>();

        // Touch targets
        var touchCount = await Page.EvaluateAsync<int>("""
            (sel) => {
                const root = sel ? document.querySelector(sel) : document;
                if (!root) return 0;
                let c = 0;
                root.querySelectorAll('a,button,[onclick],input,select,textarea').forEach(el => {
                    const r = el.getBoundingClientRect();
                    if (r.width > 0 && r.height > 0 && (r.width < 44 || r.height < 44)) c++;
                });
                return c;
            }
        """, selector);
        if (touchCount > 0)
            findings.Add(new(context, "Touch Targets", "suggestion",
                $"{touchCount} interactive element(s) < 44×44px.", "Increase padding on small interactive elements."));

        // Contrast
        var contrastCount = await Page.EvaluateAsync<int>("""
            (sel) => {
                const root = sel ? document.querySelector(sel) : document;
                if (!root) return 0;
                function lum(r,g,b) { const [rs,gs,bs]=[r,g,b].map(c=>{c/=255;return c<=0.03928?c/12.92:Math.pow((c+0.055)/1.055,2.4);}); return 0.2126*rs+0.7152*gs+0.0722*bs; }
                function parse(c) { const m=c.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/); return m?[+m[1],+m[2],+m[3]]:null; }
                let c=0;
                root.querySelectorAll('p,span,a,h1,h2,h3,h4,td,th,label,div,button').forEach(el => {
                    const s=getComputedStyle(el), t=(el.textContent||'').trim();
                    if(!t||t.length>60||s.display==='none') return;
                    const fg=parse(s.color), bg=parse(s.backgroundColor);
                    if(!fg||!bg) return;
                    const l1=lum(fg[0],fg[1],fg[2]), l2=lum(bg[0],bg[1],bg[2]);
                    const ratio=(Math.max(l1,l2)+0.05)/(Math.min(l1,l2)+0.05);
                    if(ratio<4.5&&ratio>1) c++;
                });
                return c;
            }
        """, selector);
        if (contrastCount > 0)
            findings.Add(new(context, "Color Contrast", "warning",
                $"{contrastCount} element(s) below WCAG AA 4.5:1 ratio.", "Review text/background color pairings."));

        // Focus indicators
        var focusResult = await Page.EvaluateAsync<InteractionFocusCheck>("""
            (sel) => {
                const root = sel ? document.querySelector(sel) : document;
                if (!root) return { total: 0, noOutline: 0 };
                const els=root.querySelectorAll('a,button,input,select,textarea');
                let no=0;
                els.forEach(el => { const s=getComputedStyle(el); if(s.outlineStyle==='none'&&s.boxShadow==='none') no++; });
                return { total: els.length, noOutline: no };
            }
        """, selector);
        if (focusResult.NoOutline > focusResult.Total * 0.5 && focusResult.Total > 2)
            findings.Add(new(context, "Focus Indicators", "warning",
                $"{focusResult.NoOutline}/{focusResult.Total} interactive elements lack focus indicators.",
                "Add :focus-visible ring styles for keyboard accessibility."));

        return findings;
    }
}

internal record InteractionAudit(string Scenario, string Screenshot, List<UxFinding> Findings);

internal class InteractionFocusCheck
{
    public int Total { get; set; }
    public int NoOutline { get; set; }
}
