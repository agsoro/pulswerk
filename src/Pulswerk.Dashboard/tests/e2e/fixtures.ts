/**
 * Shared E2E test fixtures for Pulswerk Dashboard.
 * 
 * Provides a pre-configured `page` fixture that automatically mocks the
 * /api/user/identity endpoint with a fully-authorized admin user.
 * This ensures the Preact SPA route resolver doesn't fall through to the 
 * "Home" catch-all page when running tests against the live server.
 * 
 * Individual tests can override the identity mock by calling page.unrouteAll()
 * and re-registering a custom identity handler.
 */
import { test as base, expect } from '@playwright/test';

/** Default mock identity: admin user with all modules & permissions enabled */
const DEFAULT_IDENTITY = {
    username: "admin",
    email: "admin@pulswerk.lan",
    name: "Administrator",
    groups: ["admins"],
    permissions: {
        canWriteValue: true,
        canAckAlarm: true,
        canEditDashboard: true,
        canEditFavorites: true,
        canEditConfig: true,
        canAccessEms: true,
        canAccessBilling: true,
        canAccessWallbox: true,
        canAccessHistoricalData: true,
        canAccessAlarms: true,
        canAccessLogs: true,
        canAccessHeartbeat: true,
        canAccessDashboards: true,
        canAccessAssets: true,
        canAccessTelemetry: true,
        canAccessConnections: true
    },
    modules: {
        ems: true,
        billing: true,
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
};

/**
 * Extended Playwright test with auto-mocked identity.
 * Import this instead of `@playwright/test` in your E2E tests.
 */
export const test = base.extend({
    page: async ({ page }, use) => {
        // Mock identity endpoint before any navigation
        await page.route('**/api/user/identity', async route => {
            await route.fulfill({
                status: 200,
                contentType: 'application/json',
                body: JSON.stringify(DEFAULT_IDENTITY)
            });
        });

        // Mock dashboards API GET to prevent 403 from the live server
        // (the Favorites/Home page fetches /api/dashboards on load).
        // Only intercept GET requests so POST/PUT/DELETE still reach the server.
        await page.route('**/api/dashboards', async route => {
            if (route.request().method() === 'GET') {
                await route.fulfill({
                    status: 200,
                    contentType: 'application/json',
                    body: JSON.stringify([])
                });
            } else {
                await route.fallback();
            }
        });

        await use(page);
    }
});

export { expect, DEFAULT_IDENTITY };
export type { Page } from '@playwright/test';
