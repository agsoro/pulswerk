# Agent Integration & Debugging Guide

Welcome, Agent! 🤖
This guide is the central reference for AI agents and human developers working on the **Pulswerk** codebase. It covers architecture, build steps, all test suites, and known gotchas.

---

## 📂 Codebase Directory Map

```
pulswerk/
├── src/
│   ├── Pulswerk.Host/           Entry point: config, Kestrel, polling orchestration
│   │   └── ConnectorHost.cs     sealed partial class — config, drivers, polling, COV, stores
│   ├── Pulswerk.Dashboard/      ASP.NET Core dashboard: API, SSE, static assets, Preact SPA
│   │   ├── Controllers/
│   │   │   └── ApiController.cs partial class — all REST routes
│   │   ├── Services/            DashboardDataService partial class split:
│   │   │   ├── TelemetryService.cs
│   │   │   ├── ConsumptionService.cs
│   │   │   ├── AssetTreeService.cs
│   │   │   ├── HeartbeatService.cs
│   │   │   ├── WriteBackService.cs
│   │   │   └── PropertiesService.cs
│   │   ├── src/frontend/        Preact/TypeScript/Vite source
│   │   ├── wwwroot/             Compiled JS/CSS bundles and static assets
│   │   ├── playwright.config.ts E2E test configuration
│   │   ├── package.json         Node dependencies (pinned @playwright/test 1.60.0)
│   │   └── tests/
│   │       ├── e2e/             Playwright browser + visual regression tests
│   │       │   ├── fixtures.ts               Shared test fixtures (mocked identity)
│   │       │   ├── billing-integration.spec.ts
│   │       │   ├── dashboard-interactions.spec.ts
│   │       │   ├── ems-integration.spec.ts
│   │       │   ├── ocpp-integration.spec.ts
│   │       │   ├── telemetry-crud.spec.ts
│   │       │   ├── ui-audit.spec.ts          Page screenshots + icon/font/layout audits
│   │       │   └── ui-audit.spec.ts-snapshots/  Visual baselines (linux + win32)
│   │       └── scada/           Vitest unit tests for frontend logic
│   ├── Pulswerk.Drivers/        Modbus TCP, BACnet/IP, OCPP, Glueck, SunSpec drivers
│   ├── Pulswerk.Storage/        InfluxDB timeseries, SQLite/JSON persistence
│   ├── Pulswerk.Billing/        EV charging billing, RFID, tenant metering
│   └── Pulswerk.Ems/            Trajectory / demand-response control
├── tests/                       C# xUnit test projects
│   ├── Pulswerk.Core.Tests/
│   ├── Pulswerk.Storage.Tests/
│   ├── Pulswerk.Drivers.Tests/
│   ├── Pulswerk.Dashboard.Tests/
│   └── Pulswerk.Billing.Tests/     BillingStore CRUD + segmented consumption algorithm
├── testing/                     Docker simulator images
│   ├── modbus-sim/              Janitza-style Modbus TCP server
│   ├── bacnet-sim/              Deziko-style BACnet/IP device
│   ├── OcppSim/                 OCPP 1.6 wallbox simulator
│   └── auth-proxy/              Fake Authelia nginx proxy (injected headers)
├── scripts/
│   └── run-e2e-docker.sh        One-command Docker E2E runner (no host Node required)
├── docker-compose.yml           Production stack (influxdb + pulswerk)
├── docker-compose.test.yml      Test overlay (+ simulators + browserless + auth-proxy)
├── pulswerk.json                Production config (not committed)
└── pulswerk.testing.json        Testing config (Docker-internal DNS for simulators)
```

---

## 🛠️ Build & Development Toolchain

### 1. Build C# Backend
```bash
dotnet build
```
Expected output: `Build succeeded. 0 Warning(s), 0 Error(s)`

### 2. Compile Frontend Assets (Vite)
```bash
# From: src/Pulswerk.Dashboard/
npm install
npm run build:js
```

### 3. Tailwind CSS
```bash
# From: src/Pulswerk.Dashboard/
npm run build:css    # one-shot compile
npm run watch:css    # watch mode during development
```

### 4. Run the Connector (development)
```bash
dotnet run --project src/Pulswerk.Host
# Dashboard available at http://localhost:5000/plswk/
```

---

## 🧪 Test Matrix

### 1. Backend C# Unit Tests
```bash
dotnet test
```
**Expected**: 59 passed, 0 failed across `Core.Tests`, `Storage.Tests`, `Drivers.Tests`, `Dashboard.Tests`, `Billing.Tests`.

| Project | Tests | Coverage |
|---|---|---|
| `Pulswerk.Core.Tests` | ~10 | Config parsing, log buffer, server DTOs |
| `Pulswerk.Storage.Tests` | ~8 | TelemetryStore query logic |
| `Pulswerk.Drivers.Tests` | ~2 | Driver smoke tests |
| `Pulswerk.Dashboard.Tests` | 21 | Alarm integration, calculated history, module gating, translations |
| `Pulswerk.Billing.Tests` | 38 | Tariffs, RFID, tenants, meter replacements, segmented consumption algorithm, transactions, curtailment targets |

---

### 2. Frontend JS/TS Unit Tests (Vitest)
```bash
# From: src/Pulswerk.Dashboard/
npm run test
```
Runs Vitest for `tests/scada/` — DrawIO codec, condition evaluator, naming logic, inspector.

---

### 3. E2E Tests (Playwright in Docker)

The E2E suite uses **Playwright 1.60.0** running inside a Docker container against the live test stack. No Node.js is required on the host machine.

#### Prerequisites

Start the full test stack (simulators + browserless + auth-proxy):
```bash
docker compose -f docker-compose.yml -f docker-compose.test.yml up -d
```

Services started:
| Service | Purpose | Host port |
|---|---|---|
| `pulswerk` | Dashboard under test | `5000` |
| `influxdb` | Time-series database | `8086` |
| `modbus-sim` | Janitza Modbus simulator | `502` |
| `modbus-sim2` | Second Modbus simulator | `503` |
| `bacnet-sim` | Deziko BACnet/IP simulator | `47809/udp` |
| `ocpp-sim` | OCPP wallbox simulator | — |
| `browserless` | Headless Chrome (CDP) | `3000` |
| `auth-proxy` | Fake Authelia headers | `5001` (users), `5002` (admins) |

#### Run All E2E Tests
> **Always use the shell script** — never call `docker run` directly. The script sets both
> `DASHBOARD_URL=http://pulswerk:5000` and `ADMIN_PROXY_URL=http://auth-proxy:81` automatically.
> Ad-hoc `docker run` invocations will miss these and cause `ERR_NAME_NOT_RESOLVED` failures.

```bash
bash scripts/run-e2e-docker.sh
```

#### Common Options
```bash
# Update visual baseline screenshots (run after intentional UI changes):
bash scripts/run-e2e-docker.sh --update-snapshots

# Run a subset by name:
bash scripts/run-e2e-docker.sh --grep "Billing"
bash scripts/run-e2e-docker.sh --grep "Dashboard Widget"

# Run a specific spec file:
bash scripts/run-e2e-docker.sh tests/e2e/billing-integration.spec.ts
```

#### What the Runner Does
1. Attaches a `mcr.microsoft.com/playwright:v1.60.0-jammy` container to the `pulswerk_default` compose network
2. Installs npm dependencies inside the container (no host Node needed)
3. Runs all tests and writes results to:
   - `src/Pulswerk.Dashboard/playwright-report/index.html` — HTML report
   - `src/Pulswerk.Dashboard/test-results/playwright-report.json` — machine-readable JSON

#### Docker Network & Service URLs (inside the container)
| Env var | Default | What it points to |
|---|---|---|
| `DASHBOARD_URL` | `http://pulswerk:5000` | Pulswerk dashboard (no auth) |
| `ADMIN_PROXY_URL` | `http://auth-proxy:81` | Admin-authenticated proxy |

Override for custom stacks: `DASHBOARD_URL=http://my-host:5000 bash scripts/run-e2e-docker.sh`

#### Parsing Results Programmatically
```bash
python3 -c "
import json
with open('src/Pulswerk.Dashboard/test-results/playwright-report.json') as f:
    r = json.load(f)
s = r['stats']
print(f'Passed: {s[\"expected\"]}  Failed: {s[\"unexpected\"]}  Flaky: {s[\"flaky\"]}')
"
```

#### E2E Test Specs (75 tests total)
| Spec | Tests | Coverage |
|---|---|---|
| `billing-integration.spec.ts` | 8 | Invoice list, replacement badge, RFID cards, tenant metering, replacement panel expand/collapse, tariff form |
| `dashboard-interactions.spec.ts` | 8 | Timewindow selector, edit modal, drag/resize, key picker |
| `ems-integration.spec.ts` | 6 | Trajectory visualizer, curtailment targets, config form |
| `ocpp-integration.spec.ts` | 5 | Wallbox status, RFID auth, charge controls |
| `telemetry-crud.spec.ts` | 2 | Historical data page, module gating / sidebar visibility |
| `ui-audit.spec.ts` | 46 | Full-page screenshots (11 pages), icon render, font consistency, nav consistency, layout sizing |

---

## ⚙️ Docker-based Diagnostic Runner (legacy)

```bash
# From repo root (PowerShell / WSL):
powershell -File ./tools/agent-check.ps1
```
Runs build → unit tests → frontend assets → local server ping → E2E. Outputs `agent-health-report.md`.

---

## ⚠️ Known Gotchas & Best Practices

### Testing

1. **Playwright version must match Docker image exactly.**
   `@playwright/test` in `package.json` is pinned to **exact `1.60.0`** (no `^` caret).
   The Docker image is `mcr.microsoft.com/playwright:v1.60.0-jammy`.
   If you upgrade one, upgrade both. A mismatch causes `Executable doesn't exist` errors.

2. **`--network host` is broken on WSL2 / Docker Desktop.**
   Always use `--network pulswerk_default` (the compose bridge). The runner script handles this.
   Services inside the network are reachable by container name (`pulswerk`, `auth-proxy`, etc.).

3. **Visual baseline snapshots are platform-specific.**
   Snapshots are stored with the OS in the filename:
   - `page-alarms-chromium-linux.png` — Docker/CI baselines
   - `page-alarms-chromium-win32.png` — Windows developer baselines

   Both coexist safely. When the E2E tests run, Playwright automatically uses the correct baseline for its platform.
   After intentional UI changes, regenerate the appropriate platform's baselines:
   ```bash
   bash scripts/run-e2e-docker.sh --update-snapshots   # Linux/Docker baselines
   ```

4. **Auth proxy ports:**
   - Port `5001` (host) / `80` (container): injects `Remote-Groups: users,viewers`
   - Port `5002` (host) / `81` (container): injects `Remote-Groups: admins,users,viewers`

   Tests that require admin write access navigate via `ADMIN_PROXY_URL` (`auth-proxy:81`).
   Tests that do NOT use the proxy (`http://pulswerk:5000`) are treated as unauthenticated — identity is mocked via Playwright's `page.route()` in `fixtures.ts`.

5. **Worker count.**
   Tests run `--workers=1` inside Docker to avoid saturating the Kestrel server.
   Locally (with a running dev server) you can safely use `workers: 2`.

6. **Log view masking.**
   The Logs page (`/plswk/Logs`) contains dynamic timestamped lines.
   The `ui-audit.spec.ts` screenshot for `logs` masks `[data-testid="log-container"]` to prevent dynamic text from failing visual regression. Add similar masks for any new dynamic content areas.

7. **BACnet / Modbus simulators.**
   Simulator configs are in `pulswerk.testing.json`. Docker DNS resolves `modbus-sim`, `modbus-sim2`, `bacnet-sim` automatically on the compose network.
   To debug a single device offline, point a device entry at `127.0.0.1` with the simulator running locally.

### Backend

8. **`DashboardDataService` is a `partial class`.**
   Logic is split across `Services/TelemetryService.cs`, `ConsumptionService.cs`, `AssetTreeService.cs`, `HeartbeatService.cs`, `WriteBackService.cs`, `PropertiesService.cs`. All compile into a single class — no DI changes needed.

9. **`ConcurrentDictionary` + `lock` pattern.**
   `LatestValues` uses `ConcurrentDictionary` for reads. Writes and snapshot operations that need atomic multi-key consistency use `lock (LatestValues)`. Don't mix a lock-free read with a lock-protected write for the same set of keys.

10. **InfluxDB health timing.**
    InfluxDB takes up to 30 seconds to become healthy after `docker compose up`. The `pulswerk` service does NOT wait for it — it retries internally. If you see `connection refused` in logs on startup, wait 30–60 seconds.
