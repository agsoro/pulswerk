import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright E2E and Visual testing configuration for Pulswerk.
 *
 * DASHBOARD_URL         – override the base URL (default: http://localhost:5000)
 * PLAYWRIGHT_CDP_URL    – connect to a remote browser via CDP / Browserless
 * UPDATE_SNAPSHOTS      – set to "1" to auto-update visual baselines (equivalent to --update-snapshots)
 *
 * Visual snapshot names embed the OS platform so that Linux (Docker) baselines
 * are stored alongside the existing Windows developer baselines without conflict:
 *   tests/e2e/ui-audit.spec.ts-snapshots/page-alarms-chromium-linux.png   ← Docker / CI
 *   tests/e2e/ui-audit.spec.ts-snapshots/page-alarms-chromium-win32.png   ← dev machines
 */
export default defineConfig({
  testDir: './tests/e2e',
  /* Run tests in files in parallel */
  fullyParallel: true,
  /* Fail the build on CI if you accidentally left test.only in the source code. */
  forbidOnly: !!process.env.CI,
  /* Retry on first failure so flaky tests don't break the run */
  retries: process.env.CI ? 2 : 1,
  /* Limit workers to prevent Kestrel starvation (each test opens a new browser context) */
  workers: process.env.CI ? 1 : 2,
  /* Reporter to use: HTML for humans, JSON for agent parsing */
  reporter: [
    ['html', { open: 'never' }],
    ['json', { outputFile: 'test-results/playwright-report.json' }],
    ['list']
  ],
  use: {
    /* Base URL – override via DASHBOARD_URL env var (set by Docker runner) */
    baseURL: process.env.DASHBOARD_URL || 'http://localhost:5000',

    /* Connect to a remote browser (Browserless) when CDP endpoint is set */
    connectOptions: process.env.PLAYWRIGHT_CDP_URL
      ? { wsEndpoint: process.env.PLAYWRIGHT_CDP_URL }
      : undefined,

    /* Capture trace on every first retry so failures are diagnosable */
    trace: 'on-first-retry',
    /* Capture screenshot on failure */
    screenshot: 'only-on-failure',
    /* Standard 1080p viewport */
    viewport: { width: 1920, height: 1080 },
    actionTimeout: 15000,
    navigationTimeout: 20000,
  },

  /* Configure projects for major browsers */
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
