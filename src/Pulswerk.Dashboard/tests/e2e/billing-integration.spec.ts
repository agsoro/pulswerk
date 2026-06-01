import { test, expect } from './fixtures';

test.describe('Billing E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        // Mock /api/billing/rfid
        await page.route('**/api/billing/rfid', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify([
                        { idTag: "04A1B2C3", userName: "Alice Miller" },
                        { idTag: "08D4E5F6", userName: "Bob Fischer" }
                    ])
                });
            } else {
                await route.fallback();
            }
        });

        // Mock /api/billing/tenants
        await page.route('**/api/billing/tenants', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify([
                        { id: "tenant-101", name: "Apartment 101", meterKey: "meter-101_active-energy" },
                        { id: "tenant-202", name: "Office 202", meterKey: "meter-202_active-energy" }
                    ])
                });
            } else {
                await route.fallback();
            }
        });

        // Mock /api/billing/tariffs
        await page.route('**/api/billing/tariffs', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify({ ratePerKwh: 0.35, baseMonthlyFee: 12.50 })
                });
            } else {
                await route.fallback();
            }
        });

        // Mock /api/billing/invoice
        await page.route('**/api/billing/invoice**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({
                    invoices: [
                        {
                            type: "EV Charging",
                            idTag: "04A1B2C3",
                            userName: "Alice Miller",
                            details: "RFID: 04A1B2C3",
                            transactionCount: 4,
                            totalKwh: 80.5,
                            ratePerKwh: 0.35,
                            baseFee: 12.50,
                            energyCost: 28.18,
                            totalCost: 40.68,
                            replacementCount: 0,
                            billingPeriod: "2026-05"
                        },
                        {
                            type: "Tenant Meter",
                            idTag: "",
                            userName: "Apartment 101",
                            details: "meter-101_active-energy",
                            transactionCount: 0,
                            totalKwh: 320.0,
                            ratePerKwh: 0.35,
                            baseFee: 12.50,
                            energyCost: 112.00,
                            totalCost: 124.50,
                            replacementCount: 1,
                            billingPeriod: "2026-05"
                        }
                    ],
                    ratePerKwh: 0.35,
                    baseMonthlyFee: 12.50
                })
            });
        });

        // Mock /api/billing/meter-replacements
        await page.route('**/api/billing/meter-replacements**', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify([
                        {
                            id: 1,
                            tenantId: 'tenant-101',
                            replacedAt: new Date('2026-05-15T12:00:00Z').getTime(),
                            replacedAtIso: '2026-05-15T12:00:00+00:00',
                            oldFinalKwh: 98751.5,
                            newStartKwh: 0.0,
                            note: 'Meter serial #OLD → #NEW'
                        }
                    ])
                });
            } else if (route.request().method() === 'POST') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            } else if (route.request().method() === 'DELETE') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            } else {
                await route.fallback();
            }
        });
    });

    test('Billing Page Tabs and Invoices', async ({ page }) => {
        await page.goto('/plswk/Billing');

        const title = page.locator('[data-testid="page-title"]');
        await expect(title).toBeVisible();
        await expect(title).toHaveText(/Billing|Abrechnung/);

        // Check both invoice rows are displayed
        await expect(page.locator('text=Alice Miller')).toBeVisible();
        await expect(page.locator('text=EV Charging')).toBeVisible();
        await expect(page.locator('text=40.68 €')).toBeVisible();
        await expect(page.locator('text=Apartment 101')).toBeVisible();
        await expect(page.locator('text=124.50 €')).toBeVisible();

        // Switch to RFID tab
        const rfidTab = page.locator('button:has-text("RFID Cards")');
        await rfidTab.click();

        // Verify both mock RFIDs are displayed
        await expect(page.locator('text=04A1B2C3')).toBeVisible();
        await expect(page.locator('text=Alice Miller')).toBeVisible();
        await expect(page.locator('text=08D4E5F6')).toBeVisible();
        await expect(page.locator('text=Bob Fischer')).toBeVisible();

        // Switch to Tenants tab
        const tenantsTab = page.locator('button:has-text("Metered Tenants")');
        await tenantsTab.click();

        // Verify both mock Tenants are displayed
        await expect(page.locator('text=Apartment 101')).toBeVisible();
        await expect(page.locator('text=meter-101_active-energy')).toBeVisible();
        await expect(page.locator('text=Office 202')).toBeVisible();
        await expect(page.locator('text=meter-202_active-energy')).toBeVisible();
    });

    test('Tariff Pricing tab displays current tariffs with form fields', async ({ page }) => {
        await page.goto('/plswk/Billing');

        // Switch to Tariff tab
        const tariffTab = page.locator('button:has-text("Tariff")');
        await tariffTab.click();

        // Verify tariff form inputs are pre-populated with mock values
        const rateInput = page.locator('input[type="number"][step="0.001"]');
        await expect(rateInput).toBeVisible();
        await expect(rateInput).toHaveValue('0.35');

        const baseFeeInput = page.locator('input[type="number"][step="0.01"]');
        await expect(baseFeeInput).toBeVisible();
        await expect(baseFeeInput).toHaveValue('12.5');

        // Verify the € symbols and submit button are present
        await expect(page.locator('text=€').first()).toBeVisible();
        const submitBtn = page.locator('button[type="submit"]');
        await expect(submitBtn).toBeVisible();
    });

    test('Invoice detail modal opens and shows line items', async ({ page }) => {
        await page.goto('/plswk/Billing');

        // Wait for invoices to load
        await expect(page.locator('text=Alice Miller')).toBeVisible();

        // Click the "Details" button on the first invoice row
        const detailBtn = page.locator('button:has-text("Details")').first();
        await detailBtn.click();

        // Verify the modal overlay is visible
        const modal = page.locator('.fixed.inset-0');
        await expect(modal).toBeVisible();

        // Verify invoice header content
        await expect(modal.locator('text=PULSWERK ENERGY BILLING')).toBeVisible();

        // Verify customer name in modal
        await expect(modal.locator('strong:has-text("Alice Miller")')).toBeVisible();

        // Verify line items: energy consumption row
        await expect(modal.locator('text=80.50 kWh')).toBeVisible();
        await expect(modal.locator('text=28.18 €')).toBeVisible();

        // Verify base fee row
        await expect(modal.locator('text=12.50 €').first()).toBeVisible();

        // Verify grand total
        await expect(modal.locator('text=40.68 €')).toBeVisible();

        // Verify print and close buttons
        await expect(page.locator('button:has-text("Print")')).toBeVisible();
        const closeBtn = page.locator('button:has-text("Close")');
        await expect(closeBtn).toBeVisible();

        // Close the modal
        await closeBtn.click();
        await expect(modal).toBeHidden();
    });

    test('Invoice list shows both EV Charging and Tenant Meter types', async ({ page }) => {
        await page.goto('/plswk/Billing');

        // Wait for both invoice rows
        await expect(page.locator('text=Alice Miller')).toBeVisible();
        await expect(page.locator('text=Apartment 101')).toBeVisible();

        // Verify type badges render differently for each type
        const evBadge = page.locator('text=EV Charging');
        const tenantBadge = page.locator('text=Tenant Meter');
        await expect(evBadge).toBeVisible();
        await expect(tenantBadge).toBeVisible();

        // Verify the tariff summary bar at the top shows mock rate
        await expect(page.locator('text=0.35 €/kWh')).toBeVisible();

        // Verify accounts count indicator
        await expect(page.locator('text=2 accounts found')).toBeVisible();
    });

    test('RFID tab shows registration form with required fields', async ({ page }) => {
        await page.goto('/plswk/Billing');

        const rfidTab = page.locator('button:has-text("RFID Cards")');
        await rfidTab.click();

        // Verify the "Register" form is visible
        await expect(page.locator('input[placeholder*="04A1B2C3"]')).toBeVisible();
        await expect(page.locator('input[placeholder*="John Doe"]')).toBeVisible();

        // Verify the "Add" / "Register" submit button is present
        const addBtn = page.locator('button[type="submit"]');
        await expect(addBtn).toBeVisible();

        // Verify delete buttons exist for each RFID card
        const deleteButtons = page.locator('button:has(i.fa-trash-alt)');
        await expect(deleteButtons).toHaveCount(2);
    });

    test('Tenants tab shows add tenant form with 3 required fields', async ({ page }) => {
        await page.goto('/plswk/Billing');

        const tenantsTab = page.locator('button:has-text("Metered Tenants")');
        await tenantsTab.click();

        // Verify the 3 form fields for adding a tenant
        await expect(page.locator('input[placeholder*="tenant_101"]')).toBeVisible();
        await expect(page.locator('input[placeholder*="Apartment"]')).toBeVisible();
        await expect(page.locator('input[placeholder*="meter"]')).toBeVisible();

        // Verify the submit button
        const addBtn = page.locator('button[type="submit"]');
        await expect(addBtn).toBeVisible();

        // Verify delete buttons exist for each tenant
        const deleteButtons = page.locator('button:has(i.fa-trash-alt)');
        await expect(deleteButtons).toHaveCount(2);
    });

    test('Invoice list shows replacement badge for meters replaced during period', async ({ page }) => {
        await page.goto('/plswk/Billing');

        // Wait for invoices to load
        await expect(page.locator('text=Alice Miller')).toBeVisible();
        await expect(page.locator('text=Apartment 101')).toBeVisible();

        // Alice (EV Charging, replacementCount=0) should NOT have a replacement badge
        const evRow = page.locator('tr', { has: page.locator('text=Alice Miller') });
        await expect(evRow.locator('text=replaced')).toHaveCount(0);

        // Apartment 101 (Tenant Meter, replacementCount=1) SHOULD have replacement badge
        const tenantRow = page.locator('tr', { has: page.locator('text=Apartment 101') });
        await expect(tenantRow.locator('text=replaced')).toBeVisible();
        await expect(tenantRow.locator('i.fa-exchange-alt')).toBeVisible();
    });

    test('Tenant meter replacement panel expands and lists recorded events', async ({ page }) => {
        await page.goto('/plswk/Billing');

        // Navigate to Tenants tab
        const tenantsTab = page.locator('button:has-text("Metered Tenants")');
        await tenantsTab.click();

        await expect(page.locator('text=Apartment 101')).toBeVisible();

        // Click the replacements toggle button (exchange-alt icon) for Apartment 101
        const replacementsBtn = page.locator('tr', { has: page.locator('text=Apartment 101') })
            .locator('button:has(i.fa-exchange-alt)');
        await replacementsBtn.click();

        // Panel should expand and show the header
        await expect(page.locator('text=Meter Replacement Events')).toBeVisible();
        await expect(page.locator('text=1 recorded')).toBeVisible();

        // The mocked replacement event should be shown
        await expect(page.locator('text=98751.50')).toBeVisible();
        await expect(page.locator('text=0.00').first()).toBeVisible();
        await expect(page.locator('text=Meter serial').first()).toBeVisible();

        // The record form should be visible with a datetime-local input
        await expect(page.locator('input[type="datetime-local"]')).toBeVisible();
        await expect(page.locator('button:has-text("Record")')).toBeVisible();

        // Clicking the toggle again collapses the panel
        await replacementsBtn.click();
        await expect(page.locator('text=Meter Replacement Events')).toBeHidden();
    });
});

