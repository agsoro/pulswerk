import { test, expect } from './fixtures';

test.describe('EMS Trajectory E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        // Mock /api/trajectory/status
        await page.route('**/api/trajectory/status', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    enabled: true,
                    monthlyTargetKwh: 3000.0,
                    mainMeterKey: "analytics-summary_daily-kwh",
                    targetKwh: 240.50,
                    actualKwh: 232.10,
                    deviationPct: -3.5,
                    isCurtailmentActive: false,
                    controlState: "Normal",
                    logs: [
                        { timestamp: "2026-05-27 12:00:00", message: "Control loop evaluated. actual: 232.1kWh, target: 240.5kWh.", state: "Normal" },
                        { timestamp: "2026-05-27 08:00:00", message: "Peak warning: deviation exceeded 5% threshold.", state: "Warning" }
                    ]
                })
            });
        });

        // Mock /api/trajectory/targets
        await page.route('**/api/trajectory/targets', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify([
                        { telemetryKey: "wallbox-sim-01_max_charge_current", normalValue: 16, warningValue: 10, criticalValue: 6 },
                        { telemetryKey: "hvac-01_setpoint", normalValue: 22, warningValue: 19, criticalValue: 16 }
                    ])
                });
            } else {
                await route.fallback();
            }
        });

        // Mock /api/trajectory/targets/15min
        await page.route('**/api/trajectory/targets/15min', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([])
            });
        });

        // Mock /api/history
        await page.route('**/api/history**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    { ts: Date.now() - 3600000 * 2, value: 5000.0, valueStr: null },
                    { ts: Date.now() - 3600000, value: 5120.0, valueStr: null },
                    { ts: Date.now(), value: 5232.1, valueStr: null }
                ])
            });
        });

        // Mock /api/trajectory/config POST
        await page.route('**/api/trajectory/config', async route => {
            if (route.request().method() === 'POST') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify({ success: true })
                });
            } else {
                await route.fallback();
            }
        });
    });

    test('Trajectory Page Visualizer', async ({ page }) => {
        await page.goto('/plswk/Trajectory');

        const title = page.locator('[data-testid="page-title"]');
        await expect(title).toBeVisible();
        await expect(title).toHaveText(/Trajectory|Verbrauchspfad/);

        // Check if main metrics cards are visible
        await expect(page.locator('text=Control Status')).toBeVisible();
        await expect(page.locator('text=Deviation from Plan')).toBeVisible();
        await expect(page.locator('text=Actual Consumption').first()).toBeVisible();

        // Verify mock target is listed in the curtailment list
        await expect(page.locator('text=wallbox-sim-01_max_charge_current')).toBeVisible();

        // Verify intervention log is shown
        await expect(page.locator('text=Control loop evaluated.')).toBeVisible();
    });

    test('Status cards show correct metric values and styling', async ({ page }) => {
        await page.goto('/plswk/Trajectory');
        await expect(page.locator('text=Control Status')).toBeVisible();

        // Control State: "Normal" with green styling
        const controlCard = page.locator('.glass').filter({ hasText: 'Control Status' });
        await expect(controlCard.locator('.text-2xl')).toHaveText(/Normal/);
        // Normal state should have emerald/green color, not rose/red
        await expect(controlCard.locator('.text-2xl.text-emerald-500')).toBeVisible();

        // Deviation: -3.5% (negative = under target = green)
        const deviationCard = page.locator('.glass').filter({ hasText: 'Deviation from Plan' });
        await expect(deviationCard.locator('.text-2xl')).toContainText('-3.5%');
        // Negative deviation should be emerald (good)
        await expect(deviationCard.locator('.text-2xl.text-emerald-400')).toBeVisible();

        // Actual Consumption: 232.1 kWh
        const actualCard = page.locator('.glass').filter({ hasText: 'Actual Consumption' });
        await expect(actualCard.locator('.text-2xl')).toContainText('232.1 kWh');

        // Target Trajectory: 240.5 kWh
        const targetCard = page.locator('.glass').filter({ hasText: 'Target Trajectory' });
        await expect(targetCard.locator('.text-2xl')).toContainText('240.5 kWh');
    });

    test('Curtailment targets table shows all configured targets', async ({ page }) => {
        await page.goto('/plswk/Trajectory');
        await expect(page.locator('text=wallbox-sim-01_max_charge_current')).toBeVisible();

        // Both targets should be listed
        await expect(page.locator('text=hvac-01_setpoint')).toBeVisible();

        // Verify the setpoint values for the wallbox target
        const wallboxRow = page.locator('tr').filter({ hasText: 'wallbox-sim-01_max_charge_current' });
        await expect(wallboxRow.locator('.text-emerald-400')).toHaveText('16');
        await expect(wallboxRow.locator('.text-amber-400')).toHaveText('10');
        await expect(wallboxRow.locator('.text-rose-500')).toHaveText('6');

        // Verify the setpoint values for the HVAC target
        const hvacRow = page.locator('tr').filter({ hasText: 'hvac-01_setpoint' });
        await expect(hvacRow.locator('.text-emerald-400')).toHaveText('22');
        await expect(hvacRow.locator('.text-amber-400')).toHaveText('19');
        await expect(hvacRow.locator('.text-rose-500')).toHaveText('16');

        // Each row should have a delete button
        const deleteButtons = page.locator('table button:has(i.fa-trash-alt)');
        await expect(deleteButtons).toHaveCount(2);
    });

    test('Add curtailment target form has 4 input fields', async ({ page }) => {
        await page.goto('/plswk/Trajectory');
        await expect(page.locator('text=Control Status')).toBeVisible();

        // Find the add form (has a placeholder with "wallbox-01")
        const keyInput = page.locator('input[placeholder*="wallbox-01"]');
        await expect(keyInput).toBeVisible();

        // There should be 3 number inputs for the setpoints (Normal, Warning, Critical)
        const addForm = page.locator('form').filter({ hasText: 'Writable Register Key' });
        const numberInputs = addForm.locator('input[type="number"]');
        await expect(numberInputs).toHaveCount(3);

        // Verify default values
        await expect(numberInputs.nth(0)).toHaveValue('16');  // Normal
        await expect(numberInputs.nth(1)).toHaveValue('10');  // Warning
        await expect(numberInputs.nth(2)).toHaveValue('6');   // Critical

        // The Add button should be present
        await expect(addForm.locator('button[type="submit"]')).toBeVisible();
    });

    test('Control settings panel shows config form with current values', async ({ page }) => {
        await page.goto('/plswk/Trajectory');
        await expect(page.locator('text=Control Status')).toBeVisible();

        // Find the settings section
        await expect(page.locator('text=Control Settings')).toBeVisible();

        // The enable toggle should be checked (enabled: true in mock)
        const toggle = page.locator('input[type="checkbox"].sr-only');
        await expect(toggle).toBeChecked();

        // Monthly target input should have 3000
        const settingsForm = page.locator('form').filter({ hasText: 'Monthly Limit' });
        const monthlyInput = settingsForm.locator('input[type="number"]');
        await expect(monthlyInput).toHaveValue('3000');

        // Grid meter key should have the mock value
        const meterInput = settingsForm.locator('input[type="text"]');
        await expect(meterInput).toHaveValue('analytics-summary_daily-kwh');

        // Save button should be present
        await expect(settingsForm.locator('button[type="submit"]')).toBeVisible();
    });

    test('Intervention logs show entries with color-coded states', async ({ page }) => {
        await page.goto('/plswk/Trajectory');

        // Wait for logs to render
        await expect(page.locator('text=Intervention Logs')).toBeVisible();

        // Both log entries should be visible
        await expect(page.locator('text=Control loop evaluated.')).toBeVisible();
        await expect(page.locator('text=Peak warning: deviation exceeded 5% threshold.')).toBeVisible();

        // Verify timestamps are displayed
        await expect(page.locator('text=2026-05-27 12:00:00')).toBeVisible();
        await expect(page.locator('text=2026-05-27 08:00:00')).toBeVisible();

        // The "Normal" state should appear with green color
        const logsPanel = page.locator('.glass', { hasText: 'Intervention Logs' });
        const normalBadge = logsPanel.locator('.text-emerald-500').filter({ hasText: 'Normal' });
        await expect(normalBadge).toBeVisible();

        // The "Warning" state should appear with amber color
        const warningBadge = logsPanel.locator('.text-amber-500').filter({ hasText: 'Warning' });
        await expect(warningBadge).toBeVisible();
    });

    test('Chart area and CSV upload button are rendered', async ({ page }) => {
        await page.goto('/plswk/Trajectory');
        await expect(page.locator('text=Control Status')).toBeVisible();

        // The chart section should be visible
        await expect(page.locator('text=Trajectory Curve (Current Month)')).toBeVisible();

        // The CSV upload button should be present
        await expect(page.locator('text=Upload Trajectory Target Profile')).toBeVisible();

        // The file input for CSV is hidden but should exist in the DOM
        const fileInput = page.locator('input[type="file"][accept=".csv"]');
        await expect(fileInput).toBeAttached();

        // The chart container div should be rendered (280px height)
        const chartContainer = page.locator('.h-\\[280px\\]');
        await expect(chartContainer).toBeVisible();
    });
});
