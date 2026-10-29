import { test, expect } from './fixtures';

const CUSTOMERS = [
    { id: 'cust_mueller', name: 'Müller GmbH', address: 'Hauptstr. 1, 12345 Stadt', email: 'billing@mueller.example', note: null },
    { id: 'cust_fischer', name: 'Fischer AG', address: 'Nebenweg 9, 54321 Dorf', email: 'invoice@fischer.example', note: null }
];

const RFIDS = [
    { idTag: '04A1B2C3', userName: 'Alice Miller', customerId: 'cust_mueller', customerName: 'Müller GmbH' },
    { idTag: '08D4E5F6', userName: 'Bob Fischer', customerId: null, customerName: null }
];

const TENANTS = [
    { id: 'tenant-101', name: 'Apartment 101', meterKey: 'meter-101_active-energy', customerId: 'cust_mueller', customerName: 'Müller GmbH' },
    { id: 'tenant-202', name: 'Office 202', meterKey: 'meter-202_active-energy', customerId: null, customerName: null }
];

const INVOICES = [
    {
        invoiceId: 'EV-2026-05-cust_mueller',
        source: 'ev',
        type: 'EV Charging',
        customerId: 'cust_mueller',
        customerName: 'Müller GmbH',
        customerAddress: 'Hauptstr. 1, 12345 Stadt',
        customerEmail: 'billing@mueller.example',
        details: 'Customer: Müller GmbH',
        transactionCount: 2,
        totalKwh: 80.5,
        ratePerKwh: 0.35,
        baseFee: 12.5,
        energyCost: 28.18,
        totalCost: 40.68,
        replacementCount: 0,
        billingPeriod: '2026-05',
        lineItems: [
            { date: '2026-05-03 09:15', description: 'Alice Miller · CP-1 / #1', idTag: '04A1B2C3', quantity: 40.5, unit: 'kWh', unitPrice: 0.35, amount: 14.18 },
            { date: '2026-05-17 18:42', description: 'Alice Miller · CP-1 / #2', idTag: '04A1B2C3', quantity: 40.0, unit: 'kWh', unitPrice: 0.35, amount: 14.0 }
        ]
    },
    {
        invoiceId: 'METER-2026-05-cust_mueller',
        source: 'meter',
        type: 'Tenant Meter',
        customerId: 'cust_mueller',
        customerName: 'Müller GmbH',
        customerAddress: 'Hauptstr. 1, 12345 Stadt',
        customerEmail: 'billing@mueller.example',
        details: 'meter-101_active-energy',
        transactionCount: 1,
        totalKwh: 320.0,
        ratePerKwh: 0.35,
        baseFee: 12.5,
        energyCost: 112.0,
        totalCost: 124.5,
        replacementCount: 1,
        billingPeriod: '2026-05',
        lineItems: [
            { date: '2026-05', description: 'Apartment 101 · meter-101_active-energy', idTag: 'tenant-101', quantity: 320.0, unit: 'kWh', unitPrice: 0.35, amount: 112.0 }
        ]
    },
    {
        invoiceId: 'EV-2026-05-unassigned',
        source: 'ev',
        type: 'EV Charging',
        customerId: null,
        customerName: 'Unassigned',
        customerAddress: null,
        customerEmail: null,
        details: 'No customer assigned',
        transactionCount: 1,
        totalKwh: 10.0,
        ratePerKwh: 0.35,
        baseFee: 12.5,
        energyCost: 3.5,
        totalCost: 16.0,
        replacementCount: 0,
        billingPeriod: '2026-05',
        lineItems: [
            { date: '2026-05-21 07:05', description: 'Bob Fischer · CP-2 / #1', idTag: '08D4E5F6', quantity: 10.0, unit: 'kWh', unitPrice: 0.35, amount: 3.5 }
        ]
    }
];

test.describe('Billing E2E Tests', () => {

    test.beforeEach(async ({ page }) => {
        await page.route('**/api/billing/customers**', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(CUSTOMERS) });
            } else {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            }
        });

        await page.route('**/api/billing/rfid**', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(RFIDS) });
            } else {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            }
        });

        await page.route('**/api/billing/tenants**', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(TENANTS) });
            } else {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            }
        });

        await page.route('**/api/billing/tariffs', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ratePerKwh: 0.35, baseMonthlyFee: 12.50 }) });
            } else {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            }
        });

        await page.route('**/api/billing/invoice**', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify({ invoices: INVOICES, ratePerKwh: 0.35, baseMonthlyFee: 12.50 })
            });
        });

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
            } else {
                await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ success: true }) });
            }
        });
    });

    test('Billing page tabs and per-customer invoice grouping', async ({ page }) => {
        await page.goto('/plswk/billing');

        const title = page.locator('[data-testid="page-title"]');
        await expect(title).toBeVisible();
        await expect(title).toHaveText(/Billing|Abrechnung/);

        // Per-customer summary cards
        await expect(page.locator('text=Summary per Customer')).toBeVisible();
        await expect(page.locator('text=Müller GmbH').first()).toBeVisible();
        await expect(page.locator('text=Unassigned').first()).toBeVisible();

        // Grand total across all customers: 40.68 + 124.50 + 16.00 = 181.18
        await expect(page.locator('text=181.18 €')).toBeVisible();

        // Both invoice documents for Müller GmbH are listed
        await expect(page.locator('text=EV-2026-05-cust_mueller')).toBeVisible();
        await expect(page.locator('text=METER-2026-05-cust_mueller')).toBeVisible();
        await expect(page.locator('text=EV-2026-05-unassigned')).toBeVisible();

        // Source badges
        await expect(page.locator('text=EV Charging').first()).toBeVisible();
        await expect(page.locator('text=Tenant Meter').first()).toBeVisible();

        // Switch to RFID tab
        await page.locator('button:has-text("RFID Cards")').click();
        await expect(page.locator('text=04A1B2C3')).toBeVisible();
        await expect(page.locator('text=Alice Miller')).toBeVisible();
        await expect(page.locator('text=08D4E5F6')).toBeVisible();
        await expect(page.locator('text=Bob Fischer')).toBeVisible();

        // Switch to Tenants tab
        await page.locator('button:has-text("Metered Tenants")').click();
        await expect(page.locator('text=Apartment 101')).toBeVisible();
        await expect(page.locator('text=meter-101_active-energy')).toBeVisible();
        await expect(page.locator('text=Office 202')).toBeVisible();
        await expect(page.locator('text=meter-202_active-energy')).toBeVisible();
    });

    test('Customers tab lists customers and opens a create dialog', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("Customers")').click();

        await expect(page.locator('text=cust_mueller')).toBeVisible();
        await expect(page.locator('text=cust_fischer')).toBeVisible();
        await expect(page.locator('text=billing@mueller.example')).toBeVisible();

        // One delete button per customer
        await expect(page.locator('button:has(i.fa-trash-alt)')).toHaveCount(2);

        // Create form lives in a popup
        await expect(page.locator('input[placeholder*="cust_mueller"]')).toHaveCount(0);
        await page.locator('button:has-text("New Customer")').click();

        const dialog = page.locator('.fixed.inset-0');
        await expect(dialog).toBeVisible();
        await expect(dialog.locator('input[placeholder*="cust_mueller"]')).toBeVisible();
        await expect(dialog.locator('input[placeholder*="Müller GmbH"]')).toBeVisible();
        await expect(dialog.locator('input[placeholder*="billing@example.com"]')).toBeVisible();
        await expect(dialog.locator('button[type="submit"]')).toBeVisible();

        // Cancel closes the popup
        await dialog.locator('button:has-text("Cancel")').click();
        await expect(dialog).toBeHidden();
    });

    test('Tariff pricing tab displays current tariffs with form fields', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("Tariff")').click();

        const rateInput = page.locator('input[type="number"][step="0.001"]');
        await expect(rateInput).toBeVisible();
        await expect(rateInput).toHaveValue('0.35');

        const baseFeeInput = page.locator('input[type="number"][step="0.01"]');
        await expect(baseFeeInput).toBeVisible();
        await expect(baseFeeInput).toHaveValue('12.5');

        await expect(page.locator('text=€').first()).toBeVisible();
        await expect(page.locator('button[type="submit"]')).toBeVisible();
    });

    test('Invoice line items expand inline and printable document opens', async ({ page }) => {
        await page.goto('/plswk/billing');

        await expect(page.locator('text=EV-2026-05-cust_mueller')).toBeVisible();

        // Expand the EV invoice line items
        const evRow = page.locator('tr', { has: page.locator('text=EV-2026-05-cust_mueller') });
        await evRow.locator('button:has(i.fa-list-ul)').click();

        await expect(page.locator('text=Line Items')).toBeVisible();
        await expect(page.locator('text=2026-05-03 09:15')).toBeVisible();
        await expect(page.locator('text=40.500 kWh')).toBeVisible();
        await expect(page.locator('text=14.18 €')).toBeVisible();

        // Collapse again
        await evRow.locator('button:has(i.fa-chevron-up)').click();
        await expect(page.locator('text=2026-05-03 09:15')).toBeHidden();

        // Open the printable invoice document
        await evRow.locator('button:has(i.fa-file-invoice)').click();

        const modal = page.locator('.fixed.inset-0');
        await expect(modal).toBeVisible();
        await expect(modal.locator('text=PULSWERK ENERGY BILLING')).toBeVisible();
        await expect(modal.locator('strong:has-text("Müller GmbH")')).toBeVisible();
        await expect(modal.locator('text=Hauptstr. 1, 12345 Stadt')).toBeVisible();
        await expect(modal.locator('text=EV-2026-05-cust_mueller')).toBeVisible();
        await expect(modal.locator('text=40.500 kWh')).toBeVisible();
        await expect(modal.locator('text=40.68 €')).toBeVisible();

        await expect(page.locator('button:has-text("Print")')).toBeVisible();
        const closeBtn = page.locator('button:has-text("Close")');
        await closeBtn.click();
        await expect(modal).toBeHidden();
    });

    test('RFID tab opens a registration dialog and shows customer assignment', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("RFID Cards")').click();

        // Assigned customer is shown as a badge, unassigned card is flagged
        const rfidTable = page.locator('table.lv-table');
        await expect(rfidTable.locator('span:has-text("Müller GmbH")')).toBeVisible();
        await expect(rfidTable.locator('text=Unassigned')).toBeVisible();
        await expect(page.locator('button:has(i.fa-trash-alt)')).toHaveCount(2);

        // Registration form lives in a popup
        await expect(page.locator('input[placeholder*="04A1B2C3"]')).toHaveCount(0);
        await page.locator('button:has-text("New RFID Card")').click();

        const dialog = page.locator('.fixed.inset-0');
        await expect(dialog).toBeVisible();
        await expect(dialog.locator('input[placeholder*="04A1B2C3"]')).toBeVisible();
        await expect(dialog.locator('input[placeholder*="John Doe"]')).toBeVisible();

        const formSelect = dialog.locator('select');
        await expect(formSelect).toBeVisible();
        await expect(formSelect.locator('option:has-text("Müller GmbH")')).toHaveCount(1);
        await expect(dialog.locator('button[type="submit"]')).toBeVisible();

        await dialog.locator('button:has-text("Cancel")').click();
        await expect(dialog).toBeHidden();
    });

    test('Existing RFID card can be edited and assigned to a customer', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("RFID Cards")').click();

        const bobRow = page.locator('tr', { has: page.locator('text=08D4E5F6') });
        await bobRow.locator('button:has(i.fa-edit)').click();

        // Inline editor exposes name input and customer dropdown
        const nameInput = bobRow.locator('input[type="text"]');
        await expect(nameInput).toHaveValue('Bob Fischer');

        const customerSelect = bobRow.locator('select');
        await expect(customerSelect).toBeVisible();
        await customerSelect.selectOption('cust_fischer');

        const postRequest = page.waitForRequest(req =>
            req.url().includes('/api/billing/rfid') && req.method() === 'POST');
        await bobRow.locator('button:has(i.fa-check)').click();

        const body = JSON.parse((await postRequest).postData() || '{}');
        expect(body.idTag).toBe('08D4E5F6');
        expect(body.userName).toBe('Bob Fischer');
        expect(body.customerId).toBe('cust_fischer');
    });

    test('Tenants tab opens an add-tenant dialog with customer assignment', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("Metered Tenants")').click();

        await expect(page.locator('button:has(i.fa-trash-alt)')).toHaveCount(2);

        await expect(page.locator('input[placeholder*="tenant_101"]')).toHaveCount(0);
        await page.locator('button:has-text("New Metered Tenant")').click();

        const dialog = page.locator('.fixed.inset-0');
        await expect(dialog).toBeVisible();
        await expect(dialog.locator('input[placeholder*="tenant_101"]')).toBeVisible();
        await expect(dialog.locator('input[placeholder*="Apartment"]')).toBeVisible();
        await expect(dialog.locator('input[placeholder*="meter"]')).toBeVisible();

        const formSelect = dialog.locator('select');
        await expect(formSelect).toBeVisible();
        await expect(formSelect.locator('option:has-text("Müller GmbH")')).toHaveCount(1);
        await expect(dialog.locator('button[type="submit"]')).toBeVisible();

        await dialog.locator('button:has-text("Cancel")').click();
        await expect(dialog).toBeHidden();
    });

    test('Invoice list shows replacement badge for meters replaced during period', async ({ page }) => {
        await page.goto('/plswk/billing');

        await expect(page.locator('text=METER-2026-05-cust_mueller')).toBeVisible();

        const evRow = page.locator('tr', { has: page.locator('text=EV-2026-05-cust_mueller') });
        await expect(evRow.locator('text=replaced')).toHaveCount(0);

        const meterRow = page.locator('tr', { has: page.locator('text=METER-2026-05-cust_mueller') });
        await expect(meterRow.locator('text=replaced')).toBeVisible();
        await expect(meterRow.locator('i.fa-exchange-alt')).toBeVisible();
    });

    test('Tenant meter replacement panel expands and lists recorded events', async ({ page }) => {
        await page.goto('/plswk/billing');

        await page.locator('button:has-text("Metered Tenants")').click();
        await expect(page.locator('text=Apartment 101')).toBeVisible();

        const replacementsBtn = page.locator('tr', { has: page.locator('text=Apartment 101') })
            .locator('button:has(i.fa-exchange-alt)');
        await replacementsBtn.click();

        await expect(page.locator('text=Meter Replacement Events')).toBeVisible();
        await expect(page.locator('text=1 recorded')).toBeVisible();

        await expect(page.locator('text=98751.50')).toBeVisible();
        await expect(page.locator('text=0.00').first()).toBeVisible();
        await expect(page.locator('text=Meter serial').first()).toBeVisible();

        await expect(page.locator('input[type="datetime-local"]')).toBeVisible();
        await expect(page.locator('button:has-text("Record")')).toBeVisible();

        await replacementsBtn.click();
        await expect(page.locator('text=Meter Replacement Events')).toBeHidden();
    });
});
