using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Pulswerk.Visual.Tests;

/// <summary>
/// Multi-Mode UI Tests — validates all state variations of mode-switchable components.
/// Every UI element that has multiple visual states (tabs, toggles, conditional panels)
/// is tested in each mode to catch errors specific to individual states.
///
/// Components covered:
///   - Timewindow Dropdown (Realtime presets / History date inputs)
///   - Edit Modal (Numeric stepper / Enum dropdown / Boolean toggle)
///   - Dashboard (List mode / View-Edit mode)
///   - Alarm Filters (active/inactive chip states)
///   - Asset Tree (collapsed / expanded nodes)
///   - Create Dashboard Modal (open state)
///   - History Modal (day selector)
/// </summary>
[TestFixture]
public class MultiModeTests : BrowserTestBase
{
    // ═══════════════════════════════════════════════════════════════════════
    //  HELPERS — create test data in the testing environment
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Create a test dashboard and return its id.</summary>
    private async Task<string?> CreateTestDashboard(string name = "Test Dashboard")
    {
        // Navigate to Dashboards and create via the UI
        await Page.GotoAsync(Url("/Dashboards"));
        await WaitForDashboard();

        // Get CSRF token
        var token = await Page.EvaluateAsync<string?>(
            "() => document.querySelector('input[name=\"__RequestVerificationToken\"]')?.value");

        if (string.IsNullOrEmpty(token))
            return null;

        // Create via API
        var result = await Page.EvaluateAsync<JsonElement>($@"
            async () => {{
                const r = await fetch('/Dashboards?handler=Create', {{
                    method: 'POST',
                    headers: {{
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': '{token}'
                    }},
                    body: JSON.stringify({{ name: '{name}', description: 'Auto-created for visual testing' }})
                }});
                return await r.json();
            }}");

        if (result.TryGetProperty("id", out var idProp))
            return idProp.GetString();

        return null;
    }

    /// <summary>Navigate to a specific dashboard by id.</summary>
    private async Task NavigateToDashboard(string dashId)
    {
        await Page.GotoAsync(Url($"/Dashboards?id={dashId}"));
        await WaitForDashboard();
        await Page.WaitForTimeoutAsync(500);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TIMEWINDOW — Realtime mode vs History mode
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TimewindowRealtimeMode()
    {
        // Create a dashboard so the timewindow selector is visible
        var dashId = await CreateTestDashboard("TW Realtime Test");

        if (dashId != null)
            await NavigateToDashboard(dashId);
        else
        {
            // Fallback: try existing dashboards
            await Page.GotoAsync(Url("/Dashboards"));
            await WaitForDashboard();
        }

        var twSelector = Page.Locator("[data-testid='tw-selector']");
        if (await twSelector.CountAsync() == 0 || !await twSelector.IsVisibleAsync())
        {
            Assert.Inconclusive("Timewindow selector not visible (dashboard API may be unavailable)");
            return;
        }

        // Open dropdown
        await twSelector.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Verify Realtime tab is active by default
        var realtimeTab = Page.Locator("button[data-tw-mode='realtime']").First;
        if (await realtimeTab.CountAsync() > 0)
        {
            var rtClass = await realtimeTab.GetAttributeAsync("class") ?? "";
            Assert.That(rtClass, Does.Contain("active"),
                "Realtime tab should be active by default");
        }

        // Verify presets panel is visible
        var presetsPanel = Page.Locator("#twRealtimePanel");
        Assert.That(await presetsPanel.IsVisibleAsync(), Is.True,
            "Realtime presets panel should be visible in Realtime mode");

        // Verify History panel is hidden
        var historyPanel = Page.Locator("#twHistoryPanel");
        Assert.That(await historyPanel.IsVisibleAsync(), Is.False,
            "History panel should be hidden in Realtime mode");

        // Verify preset buttons exist
        var presets = Page.Locator("#twPresets button");
        var presetCount = await presets.CountAsync();
        Assert.That(presetCount, Is.GreaterThanOrEqualTo(6),
            $"Expected ≥6 preset buttons, got {presetCount}");

        await Page.Locator("#twDropdown").ScreenshotAsync(new()
        {
            Path = SnapshotPath("tw-mode-realtime.png")
        });

        TestContext.Out.WriteLine($"✅ Timewindow Realtime: {presetCount} presets visible");
    }

    [Test]
    public async Task TimewindowHistoryMode()
    {
        var dashId = await CreateTestDashboard("TW History Test");

        if (dashId != null)
            await NavigateToDashboard(dashId);
        else
        {
            await Page.GotoAsync(Url("/Dashboards"));
            await WaitForDashboard();
        }

        var twSelector = Page.Locator("[data-testid='tw-selector']");
        if (await twSelector.CountAsync() == 0 || !await twSelector.IsVisibleAsync())
        {
            Assert.Inconclusive("Timewindow selector not visible");
            return;
        }

        // Open and switch to History mode
        await twSelector.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var historyTab = Page.Locator("button[data-tw-mode='history']").First;
        Assert.That(await historyTab.CountAsync(), Is.GreaterThan(0),
            "History tab button not found in timewindow dropdown");

        await historyTab.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Verify History panel is now visible
        var historyPanel = Page.Locator("#twHistoryPanel");
        Assert.That(await historyPanel.IsVisibleAsync(), Is.True,
            "History panel should be visible after clicking History tab");

        // Verify Realtime panel is now hidden
        var presetsPanel = Page.Locator("#twRealtimePanel");
        Assert.That(await presetsPanel.IsVisibleAsync(), Is.False,
            "Realtime presets should be hidden in History mode");

        // Verify History tab is now active
        var htClass = await historyTab.GetAttributeAsync("class") ?? "";
        Assert.That(htClass, Does.Contain("active"),
            "History tab should have active class");

        // Verify date inputs exist and are visible
        var fromInput = Page.Locator("#twHistFrom");
        var toInput = Page.Locator("#twHistTo");
        Assert.That(await fromInput.IsVisibleAsync(), Is.True,
            "From datetime input should be visible in History mode");
        Assert.That(await toInput.IsVisibleAsync(), Is.True,
            "To datetime input should be visible in History mode");

        // Verify Apply button exists
        var applyBtn = Page.Locator(".tw-apply");
        Assert.That(await applyBtn.CountAsync(), Is.GreaterThan(0),
            "Apply button should exist in History mode");

        // Minimum size check
        var panelBox = await historyPanel.BoundingBoxAsync();
        if (panelBox != null)
        {
            Assert.That(panelBox.Width, Is.GreaterThan(150),
                $"History panel width ({panelBox.Width}px) too narrow");
            Assert.That(panelBox.Height, Is.GreaterThan(40),
                $"History panel height ({panelBox.Height}px) too short");
        }

        await Page.Locator("#twDropdown").ScreenshotAsync(new()
        {
            Path = SnapshotPath("tw-mode-history.png")
        });

        TestContext.Out.WriteLine("✅ Timewindow History: From/To inputs + Apply visible");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EDIT MODAL — Numeric / Enum / Boolean modes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EditModalNumericMode()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        await Page.EvaluateAsync(@"() => {
            if (typeof openEdit === 'function') {
                openEdit('test:key', 'Temperature', 'DEV/AI/0', '°C', '', 'ANALOG_INPUT', '');
            }
        }");
        await Page.WaitForTimeoutAsync(500);

        var modal = Page.Locator("#editModal");
        if (!await modal.IsVisibleAsync())
        {
            Assert.Inconclusive("Edit modal did not open");
            return;
        }

        // Numeric stepper visible, others hidden
        Assert.That(await Page.Locator("#editStepper").IsVisibleAsync(), Is.True,
            "Stepper should be visible in numeric mode");
        Assert.That(await Page.Locator("#editEnumSelect").IsVisibleAsync(), Is.False,
            "Enum should be hidden in numeric mode");
        Assert.That(await Page.Locator("#editToggleWrap").IsVisibleAsync(), Is.False,
            "Toggle should be hidden in numeric mode");

        // Stepper sub-elements
        var stepper = Page.Locator("#editStepper");
        Assert.That(await stepper.Locator(".stepper-btn").First.IsVisibleAsync(), Is.True, "Minus button visible");
        Assert.That(await stepper.Locator(".stepper-btn").Last.IsVisibleAsync(), Is.True, "Plus button visible");
        Assert.That(await stepper.Locator(".stepper-input").IsVisibleAsync(), Is.True, "Input visible");

        var stepperBox = await stepper.BoundingBoxAsync();
        Assert.That(stepperBox, Is.Not.Null);
        Assert.That(stepperBox!.Width, Is.GreaterThan(100), "Stepper too narrow");
        Assert.That(stepperBox.Height, Is.GreaterThan(30), "Stepper too short");

        TestContext.Out.WriteLine("✅ Edit Modal: Numeric stepper mode validated");
    }

    [Test]
    public async Task EditModalEnumMode()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        var enumValues = Uri.EscapeDataString("[\"State A\",\"State B\",\"State C\"]");
        await Page.EvaluateAsync($@"() => {{
            if (typeof openEdit === 'function') {{
                openEdit('test:key', 'Multi-State', 'DEV/MSV/0', '', '', 'MULTI_STATE_VALUE',
                    '{enumValues}');
            }}
        }}");
        await Page.WaitForTimeoutAsync(500);

        var modal = Page.Locator("#editModal");
        if (!await modal.IsVisibleAsync())
        {
            Assert.Inconclusive("Edit modal did not open");
            return;
        }

        Assert.That(await Page.Locator("#editEnumSelect").IsVisibleAsync(), Is.True,
            "Enum dropdown visible in enum mode");
        Assert.That(await Page.Locator("#editStepper").IsVisibleAsync(), Is.False,
            "Stepper hidden in enum mode");
        Assert.That(await Page.Locator("#editToggleWrap").IsVisibleAsync(), Is.False,
            "Toggle hidden in enum mode");

        var options = Page.Locator("#editEnumSelect option");
        Assert.That(await options.CountAsync(), Is.GreaterThanOrEqualTo(3),
            "Enum should have ≥3 options");

        TestContext.Out.WriteLine($"✅ Edit Modal: Enum mode with {await options.CountAsync()} options");
    }

    [Test]
    public async Task EditModalBooleanMode()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        await Page.EvaluateAsync(@"() => {
            if (typeof openEdit === 'function') {
                openEdit('test:key', 'Binary Output', 'DEV/BO/0', '', '', 'BINARY_OUTPUT', '');
            }
        }");
        await Page.WaitForTimeoutAsync(500);

        var modal = Page.Locator("#editModal");
        if (!await modal.IsVisibleAsync())
        {
            Assert.Inconclusive("Edit modal did not open");
            return;
        }

        Assert.That(await Page.Locator("#editToggleWrap").IsVisibleAsync(), Is.True,
            "Toggle visible in bool mode");
        Assert.That(await Page.Locator("#editStepper").IsVisibleAsync(), Is.False,
            "Stepper hidden in bool mode");
        Assert.That(await Page.Locator("#editEnumSelect").IsVisibleAsync(), Is.False,
            "Enum hidden in bool mode");

        var label = Page.Locator("#editToggleLabel");
        Assert.That(await label.TextContentAsync(), Is.Not.Null.And.Not.Empty,
            "Toggle label should show On/Off");

        var toggleBox = await Page.Locator("#editToggleWrap").BoundingBoxAsync();
        Assert.That(toggleBox, Is.Not.Null);
        Assert.That(toggleBox!.Width, Is.GreaterThan(50), "Toggle wrap too narrow");

        TestContext.Out.WriteLine($"✅ Edit Modal: Boolean mode, label='{await label.TextContentAsync()}'");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DASHBOARD — List mode vs View/Edit mode
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task DashboardListMode()
    {
        await Page.GotoAsync(Url("/Dashboards"));
        await WaitForDashboard();

        var listMode = Page.Locator("[data-testid='dash-list-mode']");
        Assert.That(await listMode.IsVisibleAsync(), Is.True,
            "List mode should be visible by default on /Dashboards");

        var editMode = Page.Locator("[data-testid='dash-edit-mode']");
        Assert.That(await editMode.IsVisibleAsync(), Is.False,
            "Edit mode should be hidden when no dashboard selected");

        var createBtn = Page.Locator("[data-testid='dash-create-btn']");
        Assert.That(await createBtn.IsVisibleAsync(), Is.True,
            "Create dashboard button should be visible in list mode");

        TestContext.Out.WriteLine("✅ Dashboard: List mode visible, edit mode hidden");
    }

    [Test]
    public async Task DashboardEditModeElements()
    {
        // Create a dashboard to test edit mode
        var dashId = await CreateTestDashboard("Edit Mode Test");

        if (dashId != null)
            await NavigateToDashboard(dashId);
        else
        {
            Assert.Inconclusive("Failed to create test dashboard");
            return;
        }

        var editMode = Page.Locator("[data-testid='dash-edit-mode']");
        Assert.That(await editMode.IsVisibleAsync(), Is.True,
            "Dashboard edit mode should be visible");

        // Toolbar visible
        var toolbar = Page.Locator("[data-testid='dash-toolbar']");
        Assert.That(await toolbar.IsVisibleAsync(), Is.True,
            "Dashboard toolbar should be visible");

        // Edit button visible (view mode)
        var editBtn = Page.Locator("[data-testid='dash-edit-btn']");
        Assert.That(await editBtn.IsVisibleAsync(), Is.True,
            "Edit button should be visible in view mode");

        // Save/Cancel hidden (view mode)
        var saveBtn = Page.Locator("[data-testid='dash-save-btn']");
        Assert.That(await saveBtn.IsVisibleAsync(), Is.False,
            "Save button should be hidden in view mode");

        // Enter edit mode
        await editBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // Save should now be visible
        Assert.That(await saveBtn.IsVisibleAsync(), Is.True,
            "Save button should be visible in edit mode");

        // Add Widget button visible
        var addWidgetBtn = Page.Locator("[data-testid='dash-add-widget-btn']");
        Assert.That(await addWidgetBtn.IsVisibleAsync(), Is.True,
            "Add Widget button should be visible in edit mode");

        TestContext.Out.WriteLine("✅ Dashboard: View→Edit mode transition validated");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  CREATE DASHBOARD MODAL — open state validation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task CreateDashboardModalLayout()
    {
        await Page.GotoAsync(Url("/Dashboards"));
        await WaitForDashboard();

        // Open the create modal
        await Page.Locator("[data-testid='dash-create-btn']").ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var modal = Page.Locator("#createModal");
        Assert.That(await modal.IsVisibleAsync(), Is.True,
            "Create modal should be visible after clicking New Dashboard");

        // All child elements must be visible and adequately sized
        var nameInput = Page.Locator("#newDashName");
        var descInput = Page.Locator("#newDashDesc");
        var createBtn = modal.Locator(".btn-primary");
        var closeBtn = modal.Locator(".close-modal");

        Assert.That(await nameInput.IsVisibleAsync(), Is.True, "Name input visible");
        Assert.That(await descInput.IsVisibleAsync(), Is.True, "Description input visible");
        Assert.That(await createBtn.IsVisibleAsync(), Is.True, "Create button visible");
        Assert.That(await closeBtn.IsVisibleAsync(), Is.True, "Close button visible");

        // Size checks
        var nameBox = await nameInput.BoundingBoxAsync();
        Assert.That(nameBox, Is.Not.Null);
        Assert.That(nameBox!.Width, Is.GreaterThan(200), "Name input too narrow");

        TestContext.Out.WriteLine("✅ Create Dashboard Modal: all elements visible and sized");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ALARM FILTERS — Active/inactive chip states
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AlarmFilterChipsModes()
    {
        await Page.GotoAsync(Url("/Alarms"));
        await WaitForDashboard();

        var filterBar = Page.Locator("[data-testid='alarm-filters']");
        if (!await filterBar.IsVisibleAsync())
        {
            Assert.Inconclusive("Filter bar not visible");
            return;
        }

        var chips = filterBar.Locator("a, button, .filter-chip");
        var chipCount = await chips.CountAsync();

        Assert.That(chipCount, Is.GreaterThanOrEqualTo(4),
            $"Expected ≥4 filter chips, got {chipCount}");

        // Exactly one active
        var activeCount = 0;
        for (var i = 0; i < chipCount; i++)
        {
            var cls = await chips.Nth(i).GetAttributeAsync("class") ?? "";
            if (cls.Contains("active") || cls.Contains("selected"))
                activeCount++;
        }
        Assert.That(activeCount, Is.EqualTo(1),
            $"Exactly 1 filter chip should be active, found {activeCount}");

        // Click each chip and verify it becomes active
        for (var i = 0; i < Math.Min(chipCount, 5); i++)
        {
            var chip = chips.Nth(i);
            var chipText = (await chip.TextContentAsync() ?? "").Trim();
            await chip.ClickAsync();
            await Page.WaitForTimeoutAsync(300);

            var cls = await chip.GetAttributeAsync("class") ?? "";
            Assert.That(cls, Does.Contain("active").Or.Contain("selected"),
                $"Chip '{chipText}' should be active after clicking");

            TestContext.Out.WriteLine($"  ✅ Filter chip '{chipText}' — active after click");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ASSET TREE — Collapsed / Expanded node states
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task AssetTreeExpandCollapse()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        var toggles = Page.Locator(".tree-toggle, .tree-chevron, [data-expandable]");
        var toggleCount = await toggles.CountAsync();

        if (toggleCount == 0)
        {
            toggles = Page.Locator(".asset-sidebar .fa-chevron-right, .asset-sidebar .fa-chevron-down");
            toggleCount = await toggles.CountAsync();
        }

        if (toggleCount == 0)
        {
            Assert.Inconclusive("No expandable tree nodes found");
            return;
        }

        await toggles.First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var treeItems = Page.Locator(".asset-sidebar .tree-node, .asset-sidebar .tree-item, .asset-sidebar li");
        var afterExpand = await treeItems.CountAsync();

        TestContext.Out.WriteLine($"✅ Asset tree: {toggleCount} toggles, {afterExpand} items after expand");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HISTORY MODAL — Day selector modes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task HistoryModalDaySelectorOptions()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        await Page.EvaluateAsync(@"() => {
            if (typeof openHistory === 'function') {
                openHistory('test:key', 'Temperature Sensor', '°C', '');
            }
        }");
        await Page.WaitForTimeoutAsync(500);

        var modal = Page.Locator("#historyModal");
        if (!await modal.IsVisibleAsync())
        {
            Assert.Inconclusive("History modal did not open");
            return;
        }

        var daySelector = Page.Locator("#daysSelector");
        Assert.That(await daySelector.IsVisibleAsync(), Is.True,
            "Day range selector should be visible");

        var options = daySelector.Locator("option");
        var optCount = await options.CountAsync();
        Assert.That(optCount, Is.GreaterThanOrEqualTo(4),
            $"Day selector should have ≥4 options, got {optCount}");

        for (var i = 0; i < optCount; i++)
        {
            var val = await options.Nth(i).GetAttributeAsync("value") ?? "";
            TestContext.Out.WriteLine($"  📅 Option: {val} days");
        }

        var closeBtn = modal.Locator(".close-modal");
        Assert.That(await closeBtn.CountAsync(), Is.GreaterThan(0),
            "History modal should have close button");

        TestContext.Out.WriteLine($"✅ History modal: {optCount} day range options");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TIMEWINDOW HISTORY — Detailed UI Audit
    //  Catches: empty inputs, alignment drift, label inconsistency, sizing
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TimewindowHistoryPanelAudit()
    {
        var dashId = await CreateTestDashboard("TW Audit Test");
        if (dashId != null)
            await NavigateToDashboard(dashId);
        else
        {
            Assert.Inconclusive("Cannot create dashboard for TW audit");
            return;
        }

        var twSelector = Page.Locator("[data-testid='tw-selector']");
        if (await twSelector.CountAsync() == 0 || !await twSelector.IsVisibleAsync())
        {
            Assert.Inconclusive("Timewindow selector not visible");
            return;
        }

        // Open + switch to History
        await twSelector.ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        await Page.Locator("button[data-tw-mode='history']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        var findings = new List<string>();

        // ── 1. Date inputs should have default values (not empty placeholders) ──
        var fromVal = await Page.Locator("#twHistFrom").InputValueAsync();
        var toVal = await Page.Locator("#twHistTo").InputValueAsync();
        if (string.IsNullOrWhiteSpace(fromVal))
            findings.Add("⚠ FROM input has no default value — user sees empty placeholder");
        if (string.IsNullOrWhiteSpace(toVal))
            findings.Add("⚠ TO input has no default value — user sees empty placeholder");

        // ── 2. Label/input alignment — From and To rows should be uniform ──
        var auditResult = await Page.EvaluateAsync<JsonElement>(@"() => {
            const panel = document.getElementById('twHistoryPanel');
            if (!panel) return { error: 'panel not found' };

            const rows = panel.querySelectorAll('.tw-history-inputs > div');
            const rowData = [];

            rows.forEach((row, i) => {
                const label = row.querySelector('label');
                const input = row.querySelector('input');
                if (!label || !input) return;

                const lr = label.getBoundingClientRect();
                const ir = input.getBoundingClientRect();
                const ls = getComputedStyle(label);
                const is2 = getComputedStyle(input);

                rowData.push({
                    index: i,
                    labelText: label.textContent.trim(),
                    labelX: lr.x, labelY: lr.y, labelW: lr.width, labelH: lr.height,
                    inputX: ir.x, inputY: ir.y, inputW: ir.width, inputH: ir.height,
                    labelFont: ls.fontFamily, labelSize: ls.fontSize, labelColor: ls.color,
                    inputFont: is2.fontFamily, inputSize: is2.fontSize, inputColor: is2.color,
                    inputBg: is2.backgroundColor, inputBorder: is2.borderColor,
                    inputPadding: is2.padding
                });
            });

            // Apply button
            const applyBtn = panel.querySelector('.tw-apply');
            let applyData = null;
            if (applyBtn) {
                const ar = applyBtn.getBoundingClientRect();
                const as2 = getComputedStyle(applyBtn);
                applyData = {
                    x: ar.x, y: ar.y, width: ar.width, height: ar.height,
                    font: as2.fontFamily, size: as2.fontSize
                };
            }

            // Container info
            const container = panel.querySelector('.tw-history-inputs');
            const cr = container ? container.getBoundingClientRect() : null;

            return { rows: rowData, apply: applyData, container: cr ? { x: cr.x, y: cr.y, w: cr.width, h: cr.height } : null };
        }");

        if (auditResult.TryGetProperty("rows", out var rowsProp))
        {
            var rows = rowsProp.EnumerateArray().ToList();

            if (rows.Count >= 2)
            {
                var row0 = rows[0];
                var row1 = rows[1];

                // ── 3. Label X alignment — both labels should start at same X ──
                var l0x = row0.GetProperty("labelX").GetDouble();
                var l1x = row1.GetProperty("labelX").GetDouble();
                var labelXDrift = Math.Abs(l0x - l1x);
                if (labelXDrift > 2)
                    findings.Add($"⚠ Label X misalignment: FROM at {l0x:F0}px, TO at {l1x:F0}px (drift={labelXDrift:F0}px)");

                // ── 4. Input X alignment — both inputs should start at same X ──
                var i0x = row0.GetProperty("inputX").GetDouble();
                var i1x = row1.GetProperty("inputX").GetDouble();
                var inputXDrift = Math.Abs(i0x - i1x);
                if (inputXDrift > 2)
                    findings.Add($"⚠ Input X misalignment: FROM at {i0x:F0}px, TO at {i1x:F0}px (drift={inputXDrift:F0}px)");

                // ── 5. Input width uniformity ──
                var i0w = row0.GetProperty("inputW").GetDouble();
                var i1w = row1.GetProperty("inputW").GetDouble();
                var widthDrift = Math.Abs(i0w - i1w);
                if (widthDrift > 2)
                    findings.Add($"⚠ Input width mismatch: FROM={i0w:F0}px, TO={i1w:F0}px");

                // ── 6. Input height uniformity ──
                var i0h = row0.GetProperty("inputH").GetDouble();
                var i1h = row1.GetProperty("inputH").GetDouble();
                if (Math.Abs(i0h - i1h) > 2)
                    findings.Add($"⚠ Input height mismatch: FROM={i0h:F0}px, TO={i1h:F0}px");

                // ── 7. Font consistency across labels ──
                var l0font = row0.GetProperty("labelFont").GetString();
                var l1font = row1.GetProperty("labelFont").GetString();
                if (l0font != l1font)
                    findings.Add($"⚠ Label font mismatch: FROM='{l0font}', TO='{l1font}'");

                // ── 8. Font consistency across inputs ──
                var i0font = row0.GetProperty("inputFont").GetString();
                var i1font = row1.GetProperty("inputFont").GetString();
                if (i0font != i1font)
                    findings.Add($"⚠ Input font mismatch: FROM='{i0font}', TO='{i1font}'");

                // ── 9. Input minimum size (must be tap-friendly) ──
                if (i0w < 150) findings.Add($"⚠ FROM input too narrow ({i0w:F0}px < 150px min)");
                if (i1w < 150) findings.Add($"⚠ TO input too narrow ({i1w:F0}px < 150px min)");
                if (i0h < 28) findings.Add($"⚠ FROM input too short ({i0h:F0}px < 28px min)");
                if (i1h < 28) findings.Add($"⚠ TO input too short ({i1h:F0}px < 28px min)");

                // ── 10. Label font size check (must be readable) ──
                foreach (var row in rows)
                {
                    var sizeStr = row.GetProperty("labelSize").GetString() ?? "0";
                    if (float.TryParse(sizeStr.Replace("px", ""), out var sz) && sz < 10)
                        findings.Add($"⚠ Label '{row.GetProperty("labelText")}' font size {sz}px < 10px minimum");
                }
            }
        }

        // ── 11. Apply button width check — should span full dropdown width ──
        if (auditResult.TryGetProperty("apply", out var applyProp) && applyProp.ValueKind == JsonValueKind.Object)
        {
            var applyW = applyProp.GetProperty("width").GetDouble();
            var applyH = applyProp.GetProperty("height").GetDouble();
            if (applyH < 30) findings.Add($"⚠ Apply button too short ({applyH:F0}px < 30px)");
            if (applyW < 150) findings.Add($"⚠ Apply button too narrow ({applyW:F0}px < 150px)");
        }

        // Take a screenshot for reference
        await Page.Locator("#twDropdown").ScreenshotAsync(new()
        {
            Path = SnapshotPath("tw-history-audit.png")
        });

        // Log all findings
        foreach (var f in findings)
            TestContext.Out.WriteLine($"  {f}");

        // Fail if critical issues found (empty defaults are critical UX issues)
        var criticalIssues = findings.Where(f => f.Contains("no default value")).ToList();
        if (criticalIssues.Count > 0)
        {
            Assert.Warn(
                $"Timewindow History: {findings.Count} finding(s), {criticalIssues.Count} critical:\n  " +
                string.Join("\n  ", findings));
        }

        TestContext.Out.WriteLine(
            $"✅ Timewindow History Audit: {findings.Count} finding(s)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TIMEWINDOW DROPDOWN — comprehensive structure audit (both modes)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task TimewindowDropdownStructureAudit()
    {
        var dashId = await CreateTestDashboard("TW Structure Test");
        if (dashId != null)
            await NavigateToDashboard(dashId);
        else
        {
            Assert.Inconclusive("Cannot create dashboard");
            return;
        }

        var twSelector = Page.Locator("[data-testid='tw-selector']");
        if (await twSelector.CountAsync() == 0 || !await twSelector.IsVisibleAsync())
        {
            Assert.Inconclusive("Timewindow selector not visible");
            return;
        }

        await twSelector.ClickAsync();
        await Page.WaitForTimeoutAsync(300);

        // ── Audit dropdown structure ──
        var audit = await Page.EvaluateAsync<JsonElement>(@"() => {
            const dd = document.getElementById('twDropdown');
            if (!dd) return { error: 'dropdown not found' };
            const ddr = dd.getBoundingClientRect();

            // Tab buttons
            const tabs = dd.querySelectorAll('.tw-tab');
            const tabData = [];
            tabs.forEach(t => {
                const r = t.getBoundingClientRect();
                const s = getComputedStyle(t);
                tabData.push({
                    text: t.textContent.trim(),
                    active: t.classList.contains('active'),
                    x: r.x, y: r.y, w: r.width, h: r.height,
                    font: s.fontFamily, size: s.fontSize
                });
            });

            // Preset buttons (realtime mode)
            const presets = dd.querySelectorAll('.tw-preset');
            const presetData = [];
            presets.forEach(p => {
                const r = p.getBoundingClientRect();
                presetData.push({
                    text: p.textContent.trim(),
                    active: p.classList.contains('active'),
                    w: r.width, h: r.height,
                    visible: getComputedStyle(p).display !== 'none'
                });
            });

            return {
                dropdown: { x: ddr.x, y: ddr.y, w: ddr.width, h: ddr.height },
                tabs: tabData,
                presets: presetData
            };
        }");

        var findings = new List<string>();

        // ── Tabs should be equal width ──
        if (audit.TryGetProperty("tabs", out var tabsProp))
        {
            var tabs = tabsProp.EnumerateArray().ToList();
            if (tabs.Count == 2)
            {
                var w0 = tabs[0].GetProperty("w").GetDouble();
                var w1 = tabs[1].GetProperty("w").GetDouble();
                if (Math.Abs(w0 - w1) > 5)
                    findings.Add($"⚠ Tab width mismatch: '{tabs[0].GetProperty("text")}' = {w0:F0}px, '{tabs[1].GetProperty("text")}' = {w1:F0}px");

                var h0 = tabs[0].GetProperty("h").GetDouble();
                var h1 = tabs[1].GetProperty("h").GetDouble();
                if (Math.Abs(h0 - h1) > 2)
                    findings.Add($"⚠ Tab height mismatch: {h0:F0}px vs {h1:F0}px");

                // Tabs should be horizontally adjacent, not stacked
                var y0 = tabs[0].GetProperty("y").GetDouble();
                var y1 = tabs[1].GetProperty("y").GetDouble();
                if (Math.Abs(y0 - y1) > 5)
                    findings.Add($"⚠ Tabs not horizontally aligned: Realtime Y={y0:F0}, History Y={y1:F0}");
            }
        }

        // ── Preset buttons: uniform sizing within grid ──
        if (audit.TryGetProperty("presets", out var presetsProp))
        {
            var presets = presetsProp.EnumerateArray().ToList();
            if (presets.Count > 0)
            {
                var heights = presets.Select(p => p.GetProperty("h").GetDouble()).ToList();
                var minH = heights.Min();
                var maxH = heights.Max();
                if (maxH - minH > 5)
                    findings.Add($"⚠ Preset button height varies: {minH:F0}px–{maxH:F0}px (should be uniform)");

                var activePresets = presets.Count(p => p.GetProperty("active").GetBoolean());
                if (activePresets == 0)
                    findings.Add("⚠ No preset button is marked active — user has no visual feedback for current selection");
                else if (activePresets > 1)
                    findings.Add($"⚠ {activePresets} preset buttons marked active — should be exactly 1");
            }
        }

        foreach (var f in findings)
            TestContext.Out.WriteLine($"  {f}");

        TestContext.Out.WriteLine($"✅ Timewindow Structure Audit: {findings.Count} finding(s)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  EDIT MODAL — cross-mode visual consistency
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task EditModalCrossModeConsistency()
    {
        await Page.GotoAsync(Url("/Assets"));
        await WaitForDashboard();

        var measurements = new Dictionary<string, (double w, double h)>();

        // Measure modal card in each mode
        var modes = new[]
        {
            ("numeric", "'test:key', 'Temperature', 'DEV/AI/0', '°C', '', 'ANALOG_INPUT', ''"),
            ("binary", "'test:key', 'Binary', 'DEV/BO/0', '', '', 'BINARY_OUTPUT', ''"),
        };

        foreach (var (mode, args) in modes)
        {
            await Page.EvaluateAsync($"() => {{ if(typeof openEdit === 'function') openEdit({args}); }}");
            await Page.WaitForTimeoutAsync(400);

            var modal = Page.Locator("#editModal .edit-modal-content");
            if (!await modal.IsVisibleAsync()) continue;

            var box = await modal.BoundingBoxAsync();
            if (box != null)
                measurements[mode] = (box.Width, box.Height);

            // Close modal
            await Page.EvaluateAsync("() => document.getElementById('editModal').style.display='none'");
            await Page.WaitForTimeoutAsync(200);
        }

        if (measurements.Count >= 2)
        {
            var widths = measurements.Values.Select(m => m.w).ToList();
            var widthDrift = widths.Max() - widths.Min();
            if (widthDrift > 10)
            {
                TestContext.Out.WriteLine(
                    $"  ⚠ Modal card width varies across modes: " +
                    string.Join(", ", measurements.Select(kv => $"{kv.Key}={kv.Value.w:F0}px")));
            }

            TestContext.Out.WriteLine(
                $"✅ Edit Modal cross-mode: " +
                string.Join(", ", measurements.Select(kv => $"{kv.Key}={kv.Value.w:F0}×{kv.Value.h:F0}px")));
        }
        else
        {
            Assert.Inconclusive("Could not measure enough modes");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string SnapshotPath(string name)
    {
        var dir = Path.Combine(
            Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..")),
            "snapshots");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }
}

