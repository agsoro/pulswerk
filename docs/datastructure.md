# Pulswerk Data Model & Hierarchy

This document describes the internal data structures used by the Pulswerk Dashboard to represent asset hierarchies, telemetry points, and navigation states.

## 1. Asset Hierarchy Overview

Pulswerk uses a unified tree structure to represent assets from different protocols (BACnet, Modbus). The tree is composed of nodes (`AssetNodeDto`) which can contain other nodes or a list of telemetry points (`AssetPointDto`).

### Node Types
- **Root**: The top-level container (internal).
- **Folder**: Manual path segments defined in `pulswerk.compose.json` (e.g., "Building A", "Floor 1").
- **BACnet Device**: The entry point for a BACnet controller.
- **Structured View**: Folders derived from BACnet `OBJECT_STRUCTURED_VIEW` (Type 29) objects.
- **Modbus Device**: The entry point for a Modbus device.

### ID Uniqueness
To prevent collisions between multiple devices or connections, IDs are globally unique:
- **Manual Segments**: Prefixed with `path_` followed by a slugified name (e.g., `path_building_a`).
- **BACnet/Modbus Nodes**: Prefixed with the Connection Name (e.g., `bacnet-deziko_OBJECT_STRUCTURED_VIEW:10`).
- **Telemetry Points**: Prefixed with the Connection Name (e.g., `bacnet-deziko_OBJECT_ANALOG_INPUT:5`).

---

## 2. Data Structures (DTOs)

### AssetNodeDto
Represents a folder or device in the navigation tree.
| Property | Type | Description |
| :--- | :--- | :--- |
| `Id` | string | Globally unique ID (ConnectionPrefix + ObjectId). Used for URL deep-linking. |
| `Name` | string | Friendly display name. |
| `Type` | string | "Folder", "BACnet Device", "OBJECT_ANALOG_INPUT", etc. |
| `IsView` | bool | `true` if it acts as a container/folder. |
| `Children` | List | Sub-nodes. |
| `Points` | List | Data points belonging to this specific node. |

### AssetPointDto
Represents a single data point (sensor, setpoint, or schedule).
| Property | Type | Description |
| :--- | :--- | :--- |
| `Id` | string | Unique identifier for favorites and navigation. |
| `Key` | string | Telemetry key used for data lookup (InfluxDB/Live values). |
| `Value` | string | Current live value formatted as a string. |
| `Units` | string | Engineering units (e.g., "°C", "kW"). |
| `IsWritable` | bool | `true` if the point supports manual overrides. |
| `EnumValues` | List | Labels for Binary or Multi-state points (renders as a dropdown). |
| `ParentPath` | List | Breadcrumb data back to the root. |

---

## 3. Telemetry Key Schema

Telemetry keys are used for real-time updates and historical data queries.

### Unified Key Schema
All telemetry keys follow the pattern: `{DeviceId}_{PointKey}`

- **BACnet Example**: `ahu-01_G01'ASP01'RLT002'REG'TPF102'TSu_value`
- **Modbus Example**: `meter-main_power_kw`

---

## 4. Special Objects

### Schedules (BACnet)
Objects of type `OBJECT_SCHEDULE` (17) are rendered with a specialized UI.
- **Read**: The `Weekly_Schedule` property is parsed into a JSON-like array of switching points for 7 days.
- **Write**: The dashboard sends a `WriteComplex` request containing the full updated 7-day schedule to the device.

### Favorites
Favorites are stored in `localStorage` as a list of `AssetPointDto.Key` strings. The dashboard uses these keys to fetch live values and metadata from the `DashboardDataService`.
