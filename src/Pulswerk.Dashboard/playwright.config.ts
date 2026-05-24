import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E and Visual testing configuration for Pulswerk.
 */
export default defineConfig({
  testDir: './tests/e2e',
  /* Run tests in files in parallel */
  fullyParallel: true,
  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on CI only */
  retries: process.env.CI ? 2 : 0,
  /* Opt out of parallel tests on CI. Limit local workers to prevent backend Kestrel starvation. */
  workers: process.env.CI ? 1 : 2,
  /* Reporter to use. Output HTML for humans and JSON for agent parsing. */
  reporter: [
    ['html'],
    ['json', { outputFile: 'test-results/playwright-report.json' }]
  ],
  use: {
    /* Base URL to use in actions like `await page.goto('/')`. */
    baseURL: process.env.DASHBOARD_URL || 'http://localhost:5000',

    /* Connect to a remote browser (e.g. Browserless) if specified */
    connectOptions: process.env.PLAYWRIGHT_CDP_URL
      ? { wsEndpoint: process.env.PLAYWRIGHT_CDP_URL }
      : undefined,

    /* Collect trace when retrying the failed test. */
    trace: 'on-first-retry',
    viewport: { width: 1920, height: 1080 },
    actionTimeout: 10000,
    navigationTimeout: 15000,
  },

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
