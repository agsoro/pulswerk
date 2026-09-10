import { test, expect, Page } from './fixtures';

/**
 * Mobile Responsiveness and Touch UX Test Suite
 * 
 * Verifies mobile layout, navigation, touch targets, and responsive sheets
 * across Pulswerk on mobile viewports (e.g., 390x844).
 */

const MOBILE_VIEWPORT = { width: 390, height: 844 };

// Mock wallbox list
const MOCK_WALLBOXES = [
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
    power: 11.0,
    energyImport: 340.2,
    current: 16.0,
    voltage: 230.0,
    activeUser: "Bob Fischer"
  }
];

// Mock RFID cards
const MOCK_RFIDS = [
  { idTag: "04A1B2C3", userName: "Alice Miller" },
  { idTag: "08D4E5F6", userName: "Bob Fischer" }
];

// Mock Alarms
const MOCK_ALARMS = {
  alarms: [
    {
      alarmId: "alarm-001",
      type: "High Temperature Warning",
      severity: "CRITICAL",
      originator: "Inverter-01",
      status: "ACTIVE_UNACK",
      telemetryKey: "devices/inv1/temp",
      message: "Inverter internal temp reached 85°C",
      time: new Date().toISOString()
    },
    {
      alarmId: "alarm-002",
      type: "Grid Voltage Dip",
      severity: "WARNING",
      originator: "SmartMeter-01",
      status: "ACTIVE_UNACK",
      telemetryKey: "devices/grid/u1",
      message: "Phase L1 voltage dipped below 207V",
      time: new Date().toISOString()
    }
  ],
  countCritical: 1,
  countMajor: 0,
  countMinor: 0,
  countWarning: 1,
  countMaintenance: 0,
  countAcked: 0
};

test.describe('Mobile View & Touch Navigation Audits', () => {

  test.use({ viewport: MOBILE_VIEWPORT });

  test.beforeEach(async ({ page }) => {
    // Mock standard endpoints
    await page.route('**/api/wallboxes', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_WALLBOXES)
      });
    });

    await page.route('**/api/billing/rfid', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_RFIDS)
      });
    });

    await page.route('**/plswk/api/alarms*', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(MOCK_ALARMS)
      });
    });

    await page.route('**/api/dashboards', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { id: "dash-1", name: "Main SCADA", widgets: [] }
        ])
      });
    });
  });

  test('Mobile shell hides desktop sidebar and presents mobile header & bottom nav', async ({ page }) => {
    await page.goto('/plswk/');
    await page.waitForLoadState('domcontentloaded');

    // Desktop sidebar should NOT be visible on mobile viewport
    const sidebar = page.locator('[data-testid="sidebar"]');
    await expect(sidebar).toBeHidden();

    // Mobile header should be visible with Live indicator
    const mobileHeader = page.locator('[data-testid="mobile-header"]');
    await expect(mobileHeader).toBeVisible();
    await expect(mobileHeader).toContainText('Live');

    // Bottom navigation bar should be visible with primary icons
    const bottomNav = page.locator('[data-testid="bottom-nav"]');
    await expect(bottomNav).toBeVisible();
    await expect(bottomNav.locator('a, button')).toHaveCount(5); // Home, Wallboxes, Dashboards, Alarms, More
  });

  test('Mobile "More" drawer opens, closes, and navigates properly', async ({ page }) => {
    await page.goto('/plswk/');
    await page.waitForLoadState('domcontentloaded');

    const drawer = page.locator('[data-testid="mobile-drawer"]');
    const backdrop = page.locator('[data-testid="mobile-drawer-overlay"]');

    // Drawer should initially be hidden or translated off-screen
    await expect(backdrop).toBeHidden();

    // Click "More" button in bottom nav
    const moreBtn = page.locator('[data-testid="bottom-nav-more"]');
    await expect(moreBtn).toBeVisible();
    await moreBtn.click();

    // Backdrop and Drawer should now be visible
    await expect(backdrop).toBeVisible();
    await expect(drawer).toBeVisible();

    // Check that drawer contains Trajectory link
    const trajectoryLink = drawer.locator('a[href="/plswk/Trajectory"]');
    await expect(trajectoryLink).toBeVisible();

    // Clicking Trajectory navigates to Trajectory page and closes drawer
    await trajectoryLink.click();
    await page.waitForURL('**/plswk/Trajectory');
    await expect(backdrop).toBeHidden();
  });

  test('Wallbox mobile dashboard displays 2x2 glance cards and touch-optimized start sheet', async ({ page }) => {
    await page.goto('/plswk/Wallboxes');
    await page.waitForLoadState('domcontentloaded');

    // Check that Wallbox page rendered
    await expect(page.locator('h1')).toContainText('Wallboxes');

    // Check that Wallboxes are listed
    await expect(page.locator('text=Garage Wallbox 1')).toBeVisible();

    // Check Start Charge button has at least 44px min-height for touch ergonomcs
    const startChargeBtn = page.locator('button:has-text("Start Charge")').first();
    await expect(startChargeBtn).toBeVisible();
    const btnBox = await startChargeBtn.boundingBox();
    expect(btnBox).not.toBeNull();
    if (btnBox) {
      expect(btnBox.height).toBeGreaterThanOrEqual(40); // 40-44px touch target
    }

    // Open Start Charge modal
    await startChargeBtn.click();

    // Modal should be visible with bottom sheet styling on mobile
    const modal = page.locator('[data-testid="start-charge-modal"]');
    await expect(modal).toBeVisible();

    // Verify quick-tap RFID chips are rendered and have touch targets
    const rfidChips = modal.locator('button:has-text("Alice Miller")');
    await expect(rfidChips).toBeVisible();
    await rfidChips.click();

    // Select should be populated with Alice's RFID tag
    const select = modal.locator('select');
    await expect(select).toHaveValue('04A1B2C3');

    // Close modal
    const cancelBtn = modal.locator('button:has-text("Cancel")');
    await cancelBtn.click();
    await expect(modal).toBeHidden();
  });

  test('Alarms page renders horizontal scrollable chips and mobile-friendly cards', async ({ page }) => {
    await page.goto('/plswk/Alarms');
    await page.waitForLoadState('domcontentloaded');

    // Check alarm filter chips
    const filterContainer = page.locator('[data-testid="alarm-filters"]');
    await expect(filterContainer).toBeVisible();

    // Filter container should have overflow-x-auto for horizontal scrolling on mobile
    const classAttr = await filterContainer.getAttribute('class');
    expect(classAttr).toContain('overflow-x-auto');

    // Critical alarm should be visible with touch targets for acknowledge
    await expect(page.locator('text=High Temperature Warning')).toBeVisible();
    const ackBtn = page.locator('button:has-text("Acknowledge")').first();
    if (await ackBtn.isVisible()) {
      const ackBox = await ackBtn.boundingBox();
      expect(ackBox).not.toBeNull();
      if (ackBox) {
        expect(ackBox.height).toBeGreaterThanOrEqual(40);
      }
    }
  });

  test('Telemetry Details modal renders full-width with chart, live value, properties, and touch steppers', async ({ page }) => {
    // Mock history with realistic time series
    await page.route('**/api/history**', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { ts: Date.now() - 3600000 * 2, value: 21.5, valueStr: null },
          { ts: Date.now() - 3600000 * 1, value: 22.0, valueStr: null },
          { ts: Date.now(), value: 22.8, valueStr: null }
        ])
      });
    });

    // Mock latest values
    await page.route('**/api/latest-values**', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          'knx-living-room_1_1_10': 22.8
        })
      });
    });

    await page.goto('/plswk/');
    await page.waitForLoadState('domcontentloaded');

    // Trigger openTelemetryDetails for an analog temperature sensor
    await page.evaluate(async () => {
      await (window as any).openTelemetryDetails('knx-living-room_1_1_10');
    });

    const modal = page.locator('#telemetryDetailsModal');
    await expect(modal).toBeVisible();

    // Verify modal content spans full width on mobile
    const modalContent = page.locator('#telemetryDetailsModal .modal-content');
    const classAttr = await modalContent.getAttribute('class');
    expect(classAttr).toContain('w-full');
    expect(classAttr).toContain('h-full');

    // Wait for loading overlay to disappear
    await expect(page.locator('#telModalLoadingOverlay')).toBeHidden({ timeout: 5000 });

    // 1. Verify Title, Value, and Units
    await expect(page.locator('#telTitle')).toHaveText('Temperature Sensor');
    await expect(page.locator('#telLiveValue')).toHaveText('22.80');
    await expect(page.locator('#telUnitLabel')).toHaveText('°C');

    // 2. Verify Trend & History Chart rendered with ApexCharts
    const chart = page.locator('#historyChart .apexcharts-canvas');
    await expect(chart).toBeVisible({ timeout: 5000 });

    // 3. Verify Extended Properties tab
    await page.locator('#tabBtn_properties').click();
    await expect(page.locator('#tabContent_properties')).toBeVisible();
    await expect(page.locator('#propsTable')).toBeVisible();
    await expect(page.locator('#propsBody tr')).toHaveCount(6); // 2 system + 4 custom properties

    // 4. Verify Inline Edit Mode & Steppers
    await page.locator('#tabBtn_trend').click();
    const editBtn = page.locator('#telInlineEditStartBtn');
    await expect(editBtn).toBeVisible();
    await editBtn.click();
    await expect(page.locator('#telValueEditMode')).toBeVisible();

    // Steppers inside inlineStepperGroup should have w-11 h-11 on mobile
    const stepperMinus = page.locator('#inlineStepperGroup button').first();
    const stepperClass = await stepperMinus.getAttribute('class');
    expect(stepperClass).toContain('w-11');
    expect(stepperClass).toContain('h-11');

    // Tap plus stepper to increment value
    const stepperPlus = page.locator('#inlineStepperGroup button').nth(1);
    await stepperPlus.click();
    const editInput = page.locator('#editValue');
    await expect(editInput).toHaveValue('23.3'); // increments by 0.5 for temperature

    // Cancel edit
    const cancelEditBtn = page.locator('#telValueEditMode button:has-text("Cancel")');
    await cancelEditBtn.click();
    await expect(page.locator('#telValueEditMode')).toBeHidden();

    // Close modal
    const closeBtn = page.locator('.close-modal');
    await closeBtn.click();
    await expect(modal).toBeHidden();
  });

});
