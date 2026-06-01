import { test, expect } from './fixtures';

test.describe('Telemetry CRUD & Module Gating E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        // Mock telemetry-keys (return proper TelemetryKeyDto objects)
        await page.route('**/api/telemetry-keys', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    { key: "meter-main_energy_import", path: "DEVICE/meter-main / Energy Import", units: "kWh" },
                    { key: "meter-sub_energy_import", path: "DEVICE/meter-sub / Energy Import", units: "kWh" }
                ])
            });
        });

        // Mock telemetry data GET (empty range)
        await page.route('**/api/telemetry/data**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([])
            });
        });
    });

    test('Telemetry CRUD Page UI elements and telemetry key selection', async ({ page }) => {
        await page.goto('/plswk/TelemetryCrud');

        const title = page.locator('[data-testid="page-title"]');
        await expect(title).toBeVisible();
        await expect(title).toHaveText(/Historical Data|Historische Daten/);

        // Select key dropdown
        const keySelect = page.locator('select').first();
        await expect(keySelect).toBeVisible();
        await keySelect.selectOption('meter-main_energy_import');

        // Check section headings
        await expect(page.locator('text=Add Data Point')).toBeVisible();
        await expect(page.locator('text=Bulk CSV Import')).toBeVisible();
    });

    test('Dynamic Sidebar Gating - disables links and redirects pages when disabled', async ({ page }) => {
        // Unroute existing identity mock (from fixture) and replace with restricted version
        await page.unrouteAll({ behavior: 'ignoreErrors' });

        // Override mock user/identity: disable ems and billing
        await page.route('**/api/user/identity', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    username: "operator",
                    email: "operator@pulswerk.lan",
                    name: "Operator User",
                    groups: ["operators"],
                    permissions: {
                        canWriteValue: true,
                        canAckAlarm: true,
                        canEditDashboard: false,
                        canEditFavorites: true,
                        canEditConfig: false,
                        canAccessEms: false,
                        canAccessBilling: false,
                        canAccessWallbox: true,
                        canAccessHistoricalData: true,
                        canAccessAlarms: true,
                        canAccessLogs: false,
                        canAccessHeartbeat: true,
                        canAccessDashboards: true,
                        canAccessAssets: true,
                        canAccessTelemetry: true,
                        canAccessConnections: false
                    },
                    modules: {
                        ems: false,
                        billing: false,
                        wallbox: true,
                        historicalData: true,
                        alarms: true,
                        logs: true,
                        heartbeat: true,
                        dashboards: true,
                        assets: true,
                        telemetry: true,
                        connections: true
                    }
                })
            });
        });

        // Re-register dashboards mock (cleared by unrouteAll)
        await page.route('**/api/dashboards', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([])
            });
        });

        await page.goto('/plswk/');

        // Wait for the sidebar to stabilize after user identity loads.
        // The wallbox link should be visible (enabled in our mock).
        const wallboxLink = page.locator('[data-testid="nav-wallboxes"]');
        await expect(wallboxLink).toBeVisible({ timeout: 10000 });

        // Wait for the Preact app to finish re-rendering after identity loads
        await page.waitForFunction(() => {
            const trajectoryLink = document.querySelector('[data-testid="nav-trajectory"]');
            return !trajectoryLink;
        }, { timeout: 10000 });

        // Verify Trajectory (EMS) and Billing (disabled) links are hidden
        const emsLink = page.locator('[data-testid="nav-trajectory"]');
        await expect(emsLink).toBeHidden();

        const billingLink = page.locator('[data-testid="nav-billing"]');
        await expect(billingLink).toBeHidden();

        // Logs module is enabled but canAccessLogs is false - should be hidden
        const logsLink = page.locator('[data-testid="nav-logs"]');
        await expect(logsLink).toBeHidden();

        // Try navigating to a disabled page, it should fall back to Home
        await page.goto('/plswk/Trajectory');
        const pageTitle = page.locator('[data-testid="page-title"]');
        await expect(pageTitle).toHaveText(/Home/i);
    });
});
