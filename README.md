# Pulswerk

Pulswerk is a professional-grade gateway for industrial building automation and energy management. It bridges **BACnet/IP** and **Modbus TCP** protocols with high-performance **InfluxDB** time-series storage and provides a built-in, real-time monitoring dashboard.

Designed for resilience and scalability, Pulswerk enables seamless integration of multi-vendor hardware into a unified data layer.

---

## 🚀 Key Features

- **BACnet/IP Integration**:
  - **Automated Discovery**: Scan networks and automatically extract object hierarchies.
  - **COV & Smart Polling**: High-performance Change-of-Value (COV) subscriptions with automated polling fallback for legacy devices.
  - **Deziko Driver**: Specialized support for Siemens Desigo CC, including proprietary property extraction and Structured View hierarchy walking.
  - **Alarm Routing**: Intelligent mapping of BACnet event states and status flags directly to asset metadata.
- **Modbus TCP Integration**:
  - **High-Speed Polling**: Optimized for energy meters and power quality analyzers.
  - **Multi-Vendor Support**: Out-of-the-box drivers for Janitza, ABB, Glück, SunSpec, and more.
- **Consumption Analytics**:
  - Integrated calculation engine for hourly, daily, monthly, and yearly consumption deltas.
  - Automated historical backfilling from InfluxDB.
- **Monitoring Dashboard**:
  - Modern, responsive UI with real-time data streaming.
  - Configurable widgets, historical trend charts, and asset tree navigation.
  - Full internationalization (English/German) and regional number formatting.
- **Resilient Architecture**:
  - Automated background backfilling of Trend Logs.
  - Periodic state persistence for all calculated metrics.

## 📂 Project Structure

- **src/Pulswerk.Core**: Central configuration schemas, shared models, and DTOs.
- **src/Pulswerk.Storage**: Data persistence layer (InfluxDB for telemetry, SQLite for alarms).
- **src/Pulswerk.Drivers**: Protocol implementations and device-specific driver logic.
- **src/Pulswerk.Dashboard**: Embedded Kestrel server and Razor Pages monitoring interface.
- **src/Pulswerk.Host**: System entry point, background services, and orchestration.
- **testing/**: Virtual testbed with BACnet and Modbus simulators.
- **tools/**: CLI utilities for network diagnostics and discovery.

## 🛠 Deployment & Setup

### Configuration Files

All configuration lives at the **project root**:

| File | In Git | Purpose |
|------|--------|---------|
| `pulswerk.json` | ❌ `.gitignore` | **Production** config — your real device IPs, credentials. Copy from `pulswerk.example.json`. |
| `pulswerk.testing.json` | ✅ | **Testing** config — Docker DNS names for simulators (`modbus-sim`, `influxdb`, etc.) |
| `pulswerk.example.json` | ✅ | **Template** — documented example to create your own `pulswerk.json`. |

### Production (Docker)
Requires `pulswerk.json` to exist at the project root — will fail to start otherwise:

```powershell
cp pulswerk.example.json pulswerk.json   # edit with real device addresses
docker compose up --build -d
```

### Testing Stack
Includes BACnet/Modbus simulators. Automatically uses `pulswerk.testing.json`:

```powershell
docker compose -f docker-compose.yml -f docker-compose.test.yml up --build -d
```

- **Dashboard**: [http://localhost:5000/plswk](http://localhost:5000/plswk)
- **InfluxDB**: [http://localhost:8086](http://localhost:8086)

### Local Development
Ensure you have the .NET 8 SDK installed. Place a `pulswerk.json` at the project root:

```powershell
dotnet build Pulswerk.sln
dotnet run --project src/Pulswerk.Host
```

## ⚙️ Configuration
System behavior is defined in `pulswerk.json`. A fully documented template is available at `pulswerk.example.json` in the project root.

## 📜 License
Pulswerk is released under the **GNU General Public License v3.0**. See the [LICENSE](LICENSE) file for the full text.

---
*Built with ❤️ for Building Automation professionals.*
