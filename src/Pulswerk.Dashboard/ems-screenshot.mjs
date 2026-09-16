import { chromium } from '@playwright/test';

const browser = await chromium.connectOverCDP('ws://localhost:3000?token=pulswerk-visual-tests');
const context = browser.contexts()[0] ?? await browser.newContext();
const page = await context.newPage();

await page.route('**/api/user/identity', async route => {
    await route.fulfill({
        status: 200, contentType: 'application/json',
        body: JSON.stringify({
            username: "admin", email: "admin@pulswerk.lan", name: "Administrator",
            groups: ["admins"],
            permissions: { canWriteValue: true, canAckAlarm: true, canEditDashboard: true, canEditFavorites: true, canEditConfig: true, canAccessEms: true, canAccessBilling: true, canAccessWallbox: true, canAccessHistoricalData: true, canAccessAlarms: true, canAccessLogs: true, canAccessHeartbeat: true, canAccessDashboards: true, canAccessAssets: true, canAccessTelemetry: true, canAccessConnections: true },
            modules: { ems: true, billing: true, wallbox: true, historicalData: true, alarms: true, logs: true, heartbeat: true, dashboards: true, assets: true, telemetry: true, connections: true }
        })
    });
});

await page.goto('http://pulswerk:5000/plswk/ems', { waitUntil: 'networkidle' });
await page.waitForTimeout(3000);

// Full page screenshot
await page.screenshot({ path: '/tmp/ems-full.png', fullPage: true });

// Focus on the autarky card
const autarkyCard = page.locator('div', { hasText: /^Autarky$/ }).first();
await autarkyCard.scrollIntoViewIfNeeded().catch(() => {});
await page.waitForTimeout(500);
await page.screenshot({ path: '/tmp/ems-autarky.png', fullPage: false });
console.log('Screenshots saved');
await page.close();
await browser.close();
