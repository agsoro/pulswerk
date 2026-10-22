import { test, expect } from './fixtures';

/** Snapshot payload matching Pulswerk.Ems.EnergySystemSnapshot */
const SNAPSHOT = {
    enabled: true,
    gridImportKw: 3.4,
    gridMaxImportKw: 8.0,
    pvPowerKw: 6.2,
    pvGenerationKw: 6.2,
    batteryPowerKw: 1.5,
    batteryChargeKw: 1.5,
    batterySocPct: 72,
    batteryMaxPowerKw: 5.0,
    batteryMaxChargeKw: 5.0,
    batteryMaxDischargeKw: 5.0,
    isBatteryCharging: true,
    totalSurplusAvailableKw: 2.8,
    totalConsumptionKw: 6.8,
    autarkyPct: 64,
    totalBaseLoadKw: 1.2,
    totalOptionalLoadKw: 5.6,
    totalReclaimedPowerKw: 0.4,
    uncontrollableLoadKw: 1.2,
    totalControllableLoadKw: 5.6,
    gridImport24hKwh: 12.4,
    gridExport24hKwh: 3.1,
    pvGeneration24hKwh: 28.6,
    batteryCharged24hKwh: 9.2,
    batteryDischarged24hKwh: 7.8,
    uncontrollable24hKwh: 18.3,
    totalSurplus24hKwh: 14.7,
    totalControllable24hKwh: 22.1,
    totalConsumption24hKwh: 41.9,
    autarky24hPct: 58,
    gridImport30dKwh: 312.4,
    gridExport30dKwh: 88.1,
    pvGeneration30dKwh: 742.6,
    batteryCharged30dKwh: 231.2,
    batteryDischarged30dKwh: 198.8,
    uncontrollable30dKwh: 462.3,
    totalSurplus30dKwh: 371.7,
    totalControllable30dKwh: 552.1,
    totalConsumption30dKwh: 1041.9,
    autarky30dPct: 61,
    gridImport1yKwh: 3812.4,
    gridExport1yKwh: 1088.1,
    pvGeneration1yKwh: 8742.6,
    batteryCharged1yKwh: 2831.2,
    batteryDischarged1yKwh: 2498.8,
    uncontrollable1yKwh: 5462.3,
    totalSurplus1yKwh: 4371.7,
    totalControllable1yKwh: 6552.1,
    totalConsumption1yKwh: 12041.9,
    autarky1yPct: 63,
    consumers: [
        {
            id: 'wallbox-01',
            name: 'Wallbox Carport',
            basePowerKw: 0.0,
            hasOptionalTier: true,
            maxOptionalKw: 11.0,
            minOptionalKw: 1.38,
            standbyOptionalKw: 0.0,
            actualPowerKey: 'wallbox-01_actual_power',
            forcePowerKey: 'wallbox-01_force_power',
            priority: 1,
            actualPowerKw: 4.2,
            isActivelyDemanding: true,
            allocatedOptionalKw: 4.2,
            allocatedPowerKw: 4.2,
            unusedPowerKw: 0.4,
            status: 'Active',
            maxPowerKw: 22.0,
            energy24hKwh: 12.5,
            energy30dKwh: 312.5,
            energy1yKwh: 3125.5
        },
        {
            id: 'hvac-01',
            name: 'Heat Pump',
            basePowerKw: 1.2,
            hasOptionalTier: false,
            maxOptionalKw: 0.0,
            minOptionalKw: 0.0,
            standbyOptionalKw: 0.0,
            actualPowerKey: 'hvac-01_actual_power',
            forcePowerKey: '',
            priority: 2,
            actualPowerKw: 1.2,
            isActivelyDemanding: true,
            allocatedOptionalKw: 0.0,
            allocatedPowerKw: 1.2,
            unusedPowerKw: 0.0,
            status: 'Active',
            maxPowerKw: 3.0,
            energy24hKwh: 18.3,
            energy30dKwh: 458.3,
            energy1yKwh: 4583.3
        }
    ],
    sources: {
        gridMeterKey: 'ems_grid_power',
        gridMaxImportKw: 8.0,
        pvMeterKey: 'ems_pv_power',
        hasBattery: true,
        batteryPowerKey: 'ems_battery_power',
        batterySocKey: 'ems_battery_soc',
        batteryMaxPowerKw: 5.0,
        batteryMaxChargeKw: 5.0,
        batteryMaxDischargeKw: 5.0,
        batteryMinReserveKw: 1.0,
        batteryMinSocPct: 15.0,
        batteryFullSocPct: 98.0,
        invertGridPowerSign: false,
        invertPvPowerSign: false,
        invertBatteryPowerSign: false
    },
    logs: [
        { timestamp: '2026-05-27 12:00:00', message: 'Surplus allocated to wallbox-01.', state: 'Surplus' },
        { timestamp: '2026-05-27 08:00:00', message: 'Base limit active, no surplus available.', state: 'Base' }
    ]
};

test.describe('EMS E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        await page.route('**/api/ems/status', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify(SNAPSHOT)
            });
        });

        await page.route('**/api/ems/config', async route => {
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

        await page.route('**/api/ems/consumers**', async route => {
            if (route.request().method() === 'POST' || route.request().method() === 'DELETE') {
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

    test('EMS page renders header, configure action and KPI cards', async ({ page }) => {
        await page.goto('/plswk/ems');

        await expect(page.locator('h2', { hasText: 'Energy & Smart Charging Control' })).toBeVisible();

        // Redundant "Active" badge is gone; the shell header already shows "Live"
        await expect(page.getByTestId('ems-header').locator('text=Active')).toHaveCount(0);

        // Configure action lives in the page header on desktop
        await expect(page.getByRole('button', { name: 'Configure System' })).toBeVisible();

        // KPI cards: merged autarky, saldo, battery SoC
        await expect(page.locator('text=Autarky').first()).toBeVisible();
        await expect(page.locator('text=Saldo').first()).toBeVisible();
        await expect(page.locator('text=Battery SoC').first()).toBeVisible();

        // Surplus card is gone
        await expect(page.locator('text=Available Surplus Pool')).toHaveCount(0);

        // Values from the mocked snapshot: current autarky + D/M/Y windows
        await expect(page.locator('text=64%').first()).toBeVisible();
        await expect(page.locator('text=58%').first()).toBeVisible();
        await expect(page.locator('text=61%').first()).toBeVisible();
        await expect(page.locator('text=63%').first()).toBeVisible();
        await expect(page.locator('text=72%').first()).toBeVisible();

        // Saldo = self-generated (PV minus feed-in) minus consumption, in kWh
        await expect(page.locator('text=−16.4').first()).toBeVisible();
        await expect(page.locator('text=−387').first()).toBeVisible();
        await expect(page.locator('text=−4,387').first()).toBeVisible();
    });

    test('Rolling energy footers show 24h / 30d / 1y energy', async ({ page }) => {
        await page.goto('/plswk/ems');

        // Grid card: import + export lines (formatKwh: <100 → 1dp, <1000 → 0dp, else grouped)
        await expect(page.locator('text=12.4').first()).toBeVisible();
        await expect(page.locator('text=312').first()).toBeVisible();
        await expect(page.locator('text=3,812').first()).toBeVisible();

        // PV card
        await expect(page.locator('text=28.6').first()).toBeVisible();
        await expect(page.locator('text=743').first()).toBeVisible();
        await expect(page.locator('text=8,743').first()).toBeVisible();

        // No window toggle controls remain
        await expect(page.locator('text=24h Energy (Rolling)')).toHaveCount(0);
    });

    test('Sources & Storage column shows grid, PV and battery cards', async ({ page }) => {
        await page.goto('/plswk/ems');

        await expect(page.locator('text=Sources & Storage (Supply)')).toBeVisible();

        // Grid connection card
        await expect(page.locator('h3', { hasText: 'Grid Connection' })).toBeVisible();
        await expect(page.locator('text=ems_grid_power').first()).toBeVisible();
        await expect(page.locator('text=Max 8.0 kW')).toBeVisible();
        await expect(page.locator('text=Grid Import').first()).toBeVisible();

        // PV generation card
        await expect(page.locator('h3', { hasText: 'Solar PV Generation' })).toBeVisible();
        await expect(page.locator('text=ems_pv_power').first()).toBeVisible();

        // Battery storage card
        await expect(page.locator('h3', { hasText: 'Battery Storage' })).toBeVisible();
        await expect(page.locator('text=ems_battery_power').first()).toBeVisible();
        await expect(page.locator('text=Charging').first()).toBeVisible();
    });

    test('Consumers & Sinks column lists controllable and uncontrollable loads', async ({ page }) => {
        await page.goto('/plswk/ems');

        await expect(page.locator('text=Consumers & Sinks (Demand)')).toBeVisible();

        // Controllable consumer card
        await expect(page.locator('h3', { hasText: 'Wallbox Carport' })).toBeVisible();
        await expect(page.locator('text=wallbox-01_actual_power').first()).toBeVisible();
        await expect(page.locator('text=P1')).toBeVisible();
        await expect(page.locator('text=Allocated Setpoint').first()).toBeVisible();
        await expect(page.locator('text=Actual Power').first()).toBeVisible();

        // Uncontrollable base load card
        await expect(page.locator('text=Uncontrollable Base Loads')).toBeVisible();
        await expect(page.locator('text=Heat Pump').first()).toBeVisible();
    });

    test('Add Consumer button opens the consumer dialog', async ({ page }) => {
        await page.goto('/plswk/ems');

        await page.locator('button', { hasText: 'Add Consumer' }).first().click();

        const dialog = page.locator('form').filter({ hasText: 'Consumer Name' });
        await expect(dialog).toBeVisible();
        await expect(dialog.locator('text=Tier 1: Uncontrollable Base (kW)')).toBeVisible();
        await expect(dialog.locator('text=Tier 2: Optional Controllable (kW)')).toBeVisible();
        await expect(dialog.locator('button[type="submit"]')).toBeVisible();
    });

    test('Configure System dialog shows current source configuration', async ({ page }) => {
        await page.goto('/plswk/ems');

        await page.locator('button', { hasText: 'Configure System' }).first().click();

        const dialog = page.locator('form').filter({ hasText: 'Grid Import Limit' });
        await expect(dialog).toBeVisible();

        // Grid limit prefilled from snapshot
        const gridLimit = dialog.locator('input[type="number"]').first();
        await expect(gridLimit).toHaveValue('8');

        // Battery toggle reflects hasBattery: true
        await expect(dialog.locator('#hasBattery')).toBeChecked();

        // Telemetry key mappings prefilled
        await expect(dialog.locator('input[type="text"]').first()).toHaveValue('ems_grid_power');

        await expect(dialog.locator('button[type="submit"]')).toBeVisible();
    });

    test('Intervention log table renders entries with state badges', async ({ page }) => {
        await page.goto('/plswk/ems');

        await expect(page.locator('text=Control Decisions & Event Log')).toBeVisible();

        await expect(page.locator('text=Surplus allocated to wallbox-01.')).toBeVisible();
        await expect(page.locator('text=Base limit active, no surplus available.')).toBeVisible();

        await expect(page.locator('text=2026-05-27 12:00:00')).toBeVisible();
        await expect(page.locator('text=2026-05-27 08:00:00')).toBeVisible();

        // "Surplus" state gets the cyan badge styling
        const surplusBadge = page.getByRole('table').locator('span.text-cyan-400', { hasText: 'Surplus' });
        await expect(surplusBadge).toBeVisible();
    });

    test('Empty intervention log shows the placeholder message', async ({ page }) => {
        await page.route('**/api/ems/status', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({ ...SNAPSHOT, logs: [] })
            });
        });

        await page.goto('/plswk/ems');

        await expect(page.locator('text=No intervention events logged.')).toBeVisible();
    });
});
