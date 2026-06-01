import { test, expect, Page } from './fixtures';

// The list of pages to audit in the dashboard interface
const PAGES = [
  { name: 'dashboard-home', path: '/plswk/' },
  { name: 'dashboards-list', path: '/plswk/Dashboards' },
  { name: 'assets', path: '/plswk/Assets' },
  { name: 'inventory', path: '/plswk/TelemetryList' },
  { name: 'connections', path: '/plswk/Connections' },
  { name: 'alarms', path: '/plswk/Alarms' },
  { name: 'logs', path: '/plswk/Logs' },
  { name: 'heartbeat', path: '/plswk/Heartbeat' },
  { name: 'billing', path: '/plswk/Billing' },
  { name: 'wallboxes', path: '/plswk/Wallboxes' },
  { name: 'trajectory', path: '/plswk/Trajectory' }
];

// Helper: Disable CSS animations and transitions to stabilize visual testing screenshots
async function disableAnimations(page: Page) {
  await page.addStyleTag({
    content: `
      *, *::before, *::after {
        animation-duration: 0s !important;
        animation-delay: 0s !important;
        transition-duration: 0s !important;
        transition-delay: 0s !important;
        caret-color: transparent !important;
      }
    `
  });
  await page.waitForTimeout(100);
}

// Helper: Wait for general dashboard Shell components to render
async function waitForDashboardShell(page: Page) {
  await page.waitForSelector('[data-testid="sidebar"]', { state: 'visible', timeout: 15000 });
  await page.waitForSelector('[data-testid="page-title"]', { state: 'visible', timeout: 15000 });
}

test.describe('UI Quality and Layout Audits', () => {

  test.beforeEach(async ({ page }) => {
    // Mock dashboards list API response to return a fixed, deterministic list of dashboards
    await page.route('**/api/dashboards', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: "dash-mock-1",
            name: "Edit Mode Test",
            description: "Auto-created for visual testing",
            widgets: [{ id: "w-1" }],
            updatedAt: "2026-05-15T12:00:00Z"
          },
          {
            id: "dash-mock-2",
            name: "TW Structure Test",
            description: "Auto-created for visual testing",
            widgets: [{ id: "w-1" }, { id: "w-2" }, { id: "w-3" }, { id: "w-4" }, { id: "w-5" }],
            updatedAt: "2026-05-20T12:00:00Z"
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

    // Mock /api/billing/tenants
    await page.route('**/api/billing/tenants', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { id: "tenant-101", name: "Apartment 101", meterKey: "meter-101_active-energy" },
          { id: "tenant-202", name: "Office 202", meterKey: "meter-202_active-energy" }
        ])
      });
    });

    // Mock /api/billing/tariffs
    await page.route('**/api/billing/tariffs', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ ratePerKwh: 0.35, baseMonthlyFee: 12.50 })
      });
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
              billingPeriod: "2026-05"
            }
          ],
          ratePerKwh: 0.35,
          baseMonthlyFee: 12.50
        })
      });
    });

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
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          { telemetryKey: "wallbox-sim-01_max_charge_current", normalValue: 16, warningValue: 10, criticalValue: 6 },
          { telemetryKey: "hvac-01_setpoint", normalValue: 22, warningValue: 19, criticalValue: 16 }
        ])
      });
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

    // Mock /api/wallboxes
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

    // Mock /api/alarms (alarms page fetches this on mount via Preact component)
    await page.route('**/api/alarms**', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          alarms: [],
          countCritical: 0,
          countMajor: 0,
          countMinor: 0,
          countWarning: 0,
          countMaintenance: 0,
          countAcked: 0
        })
      });
    });

    // Mock /api/heartbeat/stats (heartbeat page polls this on mount)
    await page.route('**/api/heartbeat/stats', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          uptimeSeconds: 3661,
          updatesPerMinute: 120.0,
          totalTelemetries: 1024,
          databaseSizeBytes: 536870912,
          totalUpdates: 98765,
          totalPushUpdates: 70000,
          totalPullUpdates: 28765,
          workingSetMb: 256,
          gcHeapMb: 80,
          tcpConnections: 3,
          oldestDeviceSeenUtc: "2026-05-31 10:00:00",
          isScanning: false,
          deviceCount: 9,
          connectedDeviceCount: 9,
          dataPointKeyCount: 1337,
          version: "2.6.0",
          environment: "Test"
        })
      });
    });
  });

  // 1. Core Page Navigation & Visual Screenshot Regressions
  for (const pg of PAGES) {
    test(`Page Navigation & Screenshot: ${pg.name}`, async ({ page }) => {
      await page.goto(pg.path);
      await waitForDashboardShell(page);
      await disableAnimations(page);
      
      // Native Playwright visual comparison (compares with snapshot file)
      await expect(page).toHaveScreenshot(`page-${pg.name}.png`, {
        fullPage: true,
        maxDiffPixelRatio: 0.02,
        mask: pg.name === 'logs' ? [page.locator('[data-testid="log-container"]')] : []
      });
    });
  }

  // 2. Icon Render Audit: Catch broken Font Awesome icons (empty glyphs or wrong classes)
  for (const pg of PAGES) {
    test(`Icon Rendering Audit: ${pg.name}`, async ({ page }) => {
      await page.goto(pg.path);
      await waitForDashboardShell(page);

      const brokenIcons = await page.evaluate(() => {
        const results: any[] = [];
        const icons = document.querySelectorAll(
          '.fa, .fas, .far, .fab, .fa-solid, .fa-regular, .fa-brands, [class*="fa-"]'
        );

        icons.forEach(el => {
          const style = getComputedStyle(el, '::before');
          const content = style.content;
          const fontFamily = style.fontFamily;
          const r = el.getBoundingClientRect();

          let problem: string | null = null;

          // Content missing or explicitly 'none'
          if (!content || content === 'none' || content === '""' || content === '""') {
            problem = 'empty ::before content';
          }
          // Font family doesn't include Font Awesome
          else if (!fontFamily.toLowerCase().includes('awesome') && !fontFamily.toLowerCase().includes('fa ')) {
            problem = `wrong font-family: ${fontFamily}`;
          }
          // Zero-size icon (not rendered) but not inside a hidden container
          else if (r.width === 0 && r.height === 0 && getComputedStyle(el).display !== 'none') {
            let ancestor = el.parentElement;
            let ancestorHidden = false;
            while (ancestor) {
              const as2 = getComputedStyle(ancestor);
              if (as2.display === 'none' || as2.visibility === 'hidden') {
                ancestorHidden = true;
                break;
              }
              ancestor = ancestor.parentElement;
            }
            if (!ancestorHidden) {
              problem = 'zero-size (0x0px) while element is visible';
            }
          }

          if (problem) {
            results.push({
              className: el.className,
              parentTag: el.parentElement?.tagName || '',
              parentId: el.parentElement?.id || '',
              problem: problem,
            });
          }
        });

        return results;
      });

      expect(brokenIcons).toEqual([]);
    });
  }

  // 3. Font Consistency Audit: Catch system serif font fallbacks indicating broken CSS imports
  for (const pg of PAGES) {
    test(`Font Consistency Audit: ${pg.name}`, async ({ page }) => {
      await page.goto(pg.path);
      await waitForDashboardShell(page);

      const serifFallbacks = await page.evaluate(() => {
        const results: any[] = [];
        const fallbackFonts = ['times new roman', 'times,', 'georgia', 'palatino', 'book antiqua'];
        const checked = new Set<string>();

        document.querySelectorAll('*').forEach(el => {
          const style = getComputedStyle(el);
          if (style.display === 'none') return;
          if (el.tagName === 'HTML' || el.tagName === 'SCRIPT' || el.tagName === 'STYLE') return;

          const font = style.fontFamily.toLowerCase();
          if (checked.has(font)) return;
          checked.add(font);

          const isFallback = fallbackFonts.some(f => font.includes(f));
          if (isFallback && (el.textContent || '').trim().length > 0) {
            results.push({
              tag: el.tagName,
              id: el.id,
              classes: el.className,
              font: font
            });
          }
        });

        return results;
      });

      expect(serifFallbacks).toEqual([]);
    });
  }

  // 4. Sibling Consistency: Ensure uniform heights and fonts in repeated grid items (like cards or nav)
  test('Navbar Consistency Across Pages', async ({ page }) => {
    const navStructures: string[] = [];

    for (const pg of PAGES) {
      await page.goto(pg.path);
      await waitForDashboardShell(page);

      const navString = await page.evaluate(() => {
        const links = Array.from(document.querySelectorAll('.nav-links a'));
        return links.map(a => {
          const icon = a.querySelector('i, .fa, [class*="fa-"]');
          const iconClass = icon?.className?.replace(/\s+/g, ' ').trim() || 'NO_ICON';
          const text = a.querySelector('.nav-text')?.textContent?.trim() || 'NO_TEXT';
          const href = a.getAttribute('href') || '';
          return `${href}|${iconClass}|${text}`;
        }).join(';;');
      });

      navStructures.push(navString);
    }

    // Assert that the sidebar layout, links, and text are identical across all routes
    const firstStructure = navStructures[0];
    for (let i = 1; i < navStructures.length; i++) {
      expect(navStructures[i]).toBe(firstStructure);
    }
  });

  // 5. Layout Sibling Sizing Consistency Audits
  for (const pg of PAGES) {
    test(`Layout Arrangement Consistency: ${pg.name}`, async ({ page }) => {
      await page.goto(pg.path);
      await waitForDashboardShell(page);

      const inconsistencies = await page.evaluate(() => {
        const results: any[] = [];
        const groups = [
          { name: 'Nav links', parent: '.nav-links', child: 'a' },
          { name: 'Alarm boxes', parent: '[data-testid="alarm-boxes"]', child: '.alarm-box' },
          { name: 'Filter chips', parent: '[data-testid="alarm-filters"]', child: '.filter-chip, a' },
          { name: 'Dashboard cards', parent: '[data-testid="dash-grid"]', child: '.dash-card' },
        ];

        groups.forEach(({ name, parent, child }) => {
          const container = document.querySelector(parent);
          if (!container) return;
          const scopedSelector = child.split(',').map(s => `:scope > ${s.trim()}`).join(', ');
          const children = Array.from(container.querySelectorAll(scopedSelector));
          if (children.length < 2) return;

          const metrics = children.map(c => {
            const r = c.getBoundingClientRect();
            const s = getComputedStyle(c);
            return {
              height: r.height,
              fontSize: parseFloat(s.fontSize),
            };
          });

          // Font sizes must be equal among direct sibling controls
          const fontSizes = metrics.map(m => m.fontSize);
          const uniqueFontSizes = Array.from(new Set(fontSizes.map(s => Math.round(s * 10) / 10)));
          if (uniqueFontSizes.length > 1) {
            results.push({
              group: name,
              issue: 'inconsistent font sizes',
              values: uniqueFontSizes.map(s => `${s}px`).join(', ')
            });
          }

          // Height of identical elements must not differ greatly
          const heights = metrics.map(m => m.height).filter(h => h > 0);
          if (heights.length >= 2) {
            const avgH = heights.reduce((a, b) => a + b) / heights.length;
            const dev = heights.map(h => Math.abs(h - avgH));
            const maxDev = Math.max(...dev);
            if (maxDev > avgH * 0.2 && maxDev > 5) {
              results.push({
                group: name,
                issue: 'height deviation exceeds 20%',
                average: `${Math.round(avgH)}px`,
                deviation: `${Math.round(maxDev)}px`
              });
            }
          }
        });

        return results;
      });

      expect(inconsistencies).toEqual([]);
    });
  }
});
