import { test, expect } from './fixtures';

test.describe('OCPP Wallboxes E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        // Mock /api/wallboxes with multiple wallboxes in different states
        await page.route('**/api/wallboxes', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    {
                        id: "wallbox-sim-01",
                        name: "Garage Wallbox 1",
                        connected: true,
                        status: "Available",
                        power: 0.0,
                        energyImport: 125.4,
                        current: 0.0,
                        voltage: 230.0,
                        activeUser: "None"
                    },
                    {
                        id: "wallbox-sim-02",
                        name: "Parking Charger 2",
                        connected: true,
                        status: "Charging",
                        power: 7.4,
                        energyImport: 412.8,
                        current: 32.0,
                        voltage: 231.0,
                        activeUser: "Alice Miller"
                    },
                    {
                        id: "wallbox-sim-03",
                        name: "Outdoor Charger 3",
                        connected: false,
                        status: "Unavailable",
                        power: 0.0,
                        energyImport: 50.0,
                        current: 0.0,
                        voltage: 0.0,
                        activeUser: "None"
                    }
                ])
            });
        });

        // Mock /api/billing/rfid
        await page.route('**/api/billing/rfid', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify([
                    { idTag: "04A1B2C3", userName: "Alice Miller" },
                    { idTag: "08D4E5F6", userName: "Bob Fischer" }
                ])
            });
        });

        // Mock /api/wallboxes/force-power
        let mockForcePower = 0.0;
        await page.route('**/api/wallboxes/force-power', async route => {
            if (route.request().method() === 'POST') {
                const body = route.request().postDataJSON();
                mockForcePower = body?.forcePower ?? 0;
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify({ success: true, forcePower: mockForcePower, validitySeconds: 0 })
                });
            } else {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify({ forcePower: mockForcePower, validitySeconds: 0 })
                });
            }
        });
    });

    test('Wallboxes Page UI and Commands', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        
        // Wait for page header / title
        const title = page.locator('[data-testid="page-title"]');
        await expect(title).toBeVisible();
        await expect(title).toHaveText(/Wallboxes|Ladestationen/);

        // Verify summary cards
        await expect(page.locator('[data-testid="force-power-card"]')).toBeVisible();
        await expect(page.locator('text=Active Charging')).toBeVisible();

        // Verify wallbox card
        const cardTitle = page.locator('text=Garage Wallbox 1');
        await expect(cardTitle).toBeVisible();

        const statusText = page.getByText('Available', { exact: true });
        await expect(statusText).toBeVisible();

        // Click Remote Start Charge
        const startBtn = page.locator('button:has-text("Start Charge")');
        await expect(startBtn.first()).toBeVisible();
        await startBtn.first().click();

        // Modal should appear
        const modalHeader = page.locator('text=Authorize Remote Charging');
        await expect(modalHeader).toBeVisible();

        // Dropdown selection (should select the mocked RFID)
        const select = page.locator('select').first();
        await expect(select).toBeVisible();
        await select.selectOption({ label: 'Alice Miller (04A1B2C3)' });

        // Close modal
        const closeBtn = page.locator('button:has-text("Cancel")').first();
        await closeBtn.click();
        await expect(modalHeader).toBeHidden();
    });

    test('Summary cards compute correct aggregate values', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        await expect(page.locator('[data-testid="page-title"]')).toBeVisible();

        // Wait for cards to render
        const fpCard = page.locator('[data-testid="force-power-card"]');
        await expect(fpCard).toBeVisible();
        await expect(page.locator('[data-testid="force-power-value"]')).toHaveText(/Unrestricted|Unbegrenzt/);

        // Online = 2 (wallbox-sim-01 and 02 are connected)
        const onlineCard = page.locator('.glass').filter({ hasText: 'Online' });
        await expect(onlineCard.locator('.text-2xl')).toHaveText('2');

        // Active Charging = 1 (only wallbox-sim-02 is Charging)
        const chargingCard = page.locator('.glass').filter({ hasText: 'Active Charging' });
        await expect(chargingCard.locator('.text-2xl')).toHaveText('1');

        // Total Power = 7.4 kW (only wallbox-sim-02 has power)
        const powerCard = page.locator('.glass').filter({ hasText: 'Total Power' });
        await expect(powerCard.locator('.text-2xl')).toContainText('7.4');
    });

    test('Force power limit can be edited and persisted', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        const fpCard = page.locator('[data-testid="force-power-card"]');
        await expect(fpCard).toBeVisible();

        // Click to enter edit mode
        await fpCard.click();

        // Enter a limit of 11.0 kW
        const input = fpCard.locator('input[type="number"]');
        await expect(input).toBeVisible();
        await input.fill('11');

        // Save
        const saveBtn = fpCard.locator('button[title*="Save"], button[title*="Speichern"]').first();
        await saveBtn.click();

        // Confirm new value is displayed
        await expect(page.locator('[data-testid="force-power-value"]')).toHaveText('11.0 kW');
    });

    test('Wallbox cards show correct per-unit data and status indicators', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        await expect(page.locator('text=Garage Wallbox 1')).toBeVisible();

        // Verify the "Available" wallbox card
        const availableCard = page.locator('.bg-slate-800').filter({ hasText: 'Garage Wallbox 1' });
        await expect(availableCard.locator('text=Available')).toBeVisible();
        await expect(availableCard.locator('text=0.00 kW')).toBeVisible();
        await expect(availableCard.locator('text=125.4 kWh')).toBeVisible();
        // Start Charge button should be present (not Stop)
        await expect(availableCard.locator('button:has-text("Start Charge")')).toBeVisible();
        await expect(availableCard.locator('button:has-text("Stop")')).toBeHidden();

        // Verify the "Charging" wallbox card
        const chargingCard = page.locator('.bg-slate-800').filter({ hasText: 'Parking Charger 2' });
        await expect(chargingCard.locator('text=Charging')).toBeVisible();
        await expect(chargingCard.locator('text=7.40 kW')).toBeVisible();
        await expect(chargingCard.locator('text=Alice Miller')).toBeVisible();
        await expect(chargingCard.locator('text=412.8 kWh')).toBeVisible();
        // Stop Charge button should be present (not Start)
        await expect(chargingCard.locator('button:has-text("Stop")')).toBeVisible();
        await expect(chargingCard.locator('button:has-text("Start")')).toBeHidden();
    });

    test('Disconnected wallbox disables action buttons', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        await expect(page.locator('text=Outdoor Charger 3')).toBeVisible();

        const offlineCard = page.locator('.bg-slate-800').filter({ hasText: 'Outdoor Charger 3' });
        
        // Verify offline badge
        await expect(offlineCard.locator('text=Offline')).toBeVisible();

        // Action buttons should be disabled
        const buttons = offlineCard.locator('.px-6.py-4.border-t button');
        const count = await buttons.count();
        for (let i = 0; i < count; i++) {
            await expect(buttons.nth(i)).toBeDisabled();
        }
    });

    test('Start Charge modal shows RFID dropdown with registered cards', async ({ page }) => {
        await page.goto('/plswk/Wallboxes');
        await expect(page.locator('text=Garage Wallbox 1')).toBeVisible();

        // Click Start Charge on the available wallbox
        const availableCard = page.locator('.bg-slate-800').filter({ hasText: 'Garage Wallbox 1' });
        await availableCard.locator('button:has-text("Start Charge")').click();

        // Modal visible
        await expect(page.locator('text=Authorize Remote Charging')).toBeVisible();

        // Verify RFID dropdown has both registered cards
        const select = page.locator('select').first();
        const options = select.locator('option');
        // 3 options: "Custom card" + 2 registered cards
        await expect(options).toHaveCount(3);

        // Select "Custom card" option to reveal the custom RFID input
        await select.selectOption('');
        const customInput = page.locator('input[placeholder*="A1B2C3D4"]');
        await expect(customInput).toBeVisible();

        // Authorize button should be disabled when no RFID selected and custom is empty
        const authBtn = page.locator('button:has-text("Authorize")');
        await expect(authBtn).toBeDisabled();

        // Type a custom RFID
        await customInput.fill('CUSTOM123');
        await expect(authBtn).toBeEnabled();
    });
});
