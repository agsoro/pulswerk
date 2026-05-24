# Agent Integration & Debugging Guide

Welcome, Agent! 🤖
This guide serves as a central reference and onboarding manual for AI agents (and human developers) working on the **Pulswerk Dashboard** codebase. It outlines the system architecture, build steps, test suites, and diagnostic toolchains.

---

## 📂 Codebase Directory Map

*   **`src/`** — Core implementation codebase.
    *   **`Pulswerk.Host/`** — The entry point assembly. Contains configuration setup, host services, and Kestrel server initialization.
    *   **`Pulswerk.Dashboard/`** — Razor/MVC server serving dashboard views, static assets, and client APIs.
        *   `src/frontend/` — The Preact/TypeScript/Vite source code.
        *   `wwwroot/` — Compiled JS/CSS bundles (`wwwroot/dist`) and static assets.
    *   **`Pulswerk.Drivers/`** — Connection drivers (Modbus TCP/RTU, BACnet IP, etc.).
    *   **`Pulswerk.Storage/`** — Data persistence layers (InfluxDB for timeseries, local SQLite/JSON for settings).
*   **`tests/`** — Test suites.
    *   `Pulswerk.Core.Tests/`, `Pulswerk.Storage.Tests/`, `Pulswerk.Drivers.Tests/` — Backend unit tests.
    *   `Pulswerk.Dashboard/tests/e2e/` — Playwright end-to-end browser and visual regression tests.
*   **`tools/`** — Automation and diagnostic scripts.
    *   `tools/agent-check.ps1` — The automated health check and diagnostic script.

---

## 🛠️ Build and Development Toolchain

### 1. Build C# Backend
Restore dependencies and compile the full .NET Solution:
```powershell
dotnet build
```

### 2. Compile Frontend Assets (Vite)
Navigate to the dashboard project directory to work with node assets:
```powershell
# Directory: src/Pulswerk.Dashboard
npm install
npm run build:js
```

### 3. Tailwind CSS Compile & Watch
Regenerate CSS classes manually or start the Tailwind watcher during active development:
```powershell
# Build tailwindcss once
npm run build:css

# Start tailwindcss watcher
npm run watch:css
```

---

## 🧪 Test Matrix & Commands

### 1. Backend C# Tests
Execute all standard xUnit/NUnit C# unit tests:
```powershell
dotnet test
```

### 2. Frontend JS/TS Unit Tests
Runs Vitest for Preact components and TS helpers:
```powershell
# Directory: src/Pulswerk.Dashboard
npm run test
```

### 3. Playwright E2E & Visual Audits
Run browser-based system flow and UI audit tests:
```powershell
# Directory: src/Pulswerk.Dashboard
npm run test:e2e
```
> [!NOTE]
> The backend server (running on `http://localhost:5000`) must be active for E2E tests to succeed.

### 4. Playwright Diagnostic JSON Reports
A custom JSON reporter is configured to output test failures programmatically. You can inspect failures directly by parsing the generated report:
```json
// Path: src/Pulswerk.Dashboard/test-results/playwright-report.json
```

---

## ⚙️ Automated Codebase Diagnosis (`agent-check.ps1`)
To verify the entire environment (compilation, unit tests, frontend assets, local server connectivity, and E2E tests) in a single command, run the diagnostic script:
```powershell
powershell -File .\tools\agent-check.ps1
```
This script runs each diagnostic phase and exports a summary report to **`agent-health-report.md`**.

---

## ⚠️ Known Gotchas & Best Practices

1.  **Playwright Parallelism Limit**:
    Playwright is configured with `workers: 2` locally. Dotnet Kestrel runs as a single local instance during tests. Spawning more than 2 browser instances simultaneously saturates server database requests, causing random `TimeoutError` exceptions. Do not increase the worker count.
2.  **Log Visual Masking**:
    The Logs view (`/plswk/Logs`) lists dynamic, timestamped lines. Playwright screenshot comparisons mask `[data-testid="log-container"]` to prevent text differences from failing visual regression checks. If adding new dynamic components, ensure they are masked or omitted in `ui-audit.spec.ts`.
3.  **Updating UI Snapshots**:
    If you intentionally modify layout templates or dashboard views, you must regenerate the baseline visual snapshots:
    ```powershell
    npx playwright test --update-snapshots
    ```
4.  **BACnet / Modbus Simulation**:
    Simulators run inside the environment or can be configured via `pulswerk.testing.json`. Use it for integration checks when live hardware drivers need a loopback connector.
