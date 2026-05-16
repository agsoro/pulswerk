# Pulswerk Data Model & Hierarchy

This document describes the internal data structures used by the Pulswerk Dashboard to represent hierarchies, assets, and data points.

## 1. System Topology

Pulswerk follows a strict five-layer hierarchy for data organization:

**Connection** → **Device** → **Hierarchy (Assets)** → **DataPoint** → **TimeSeries**

1.  **Connection**: The physical or logical transport layer (e.g., Modbus TCP, BACnet IP).
2.  **Device**: A specific piece of hardware defined in configuration (e.g., "Main Meter", "Deziko ASP-01").
3.  **Hierarchy (Assets)**: The logical organization layer. These are "Nodes" in the tree that can represent physical locations (Building A / Floor 1) or structured sub-sections of a device.
4.  **DataPoint**: An individual sensor, metric, or register (e.g., `power_kw`, `room_temp`).
5.  **TimeSeries**: The historical progression of a DataPoint's value over time, stored in the database.

---

## 2. Inventory & Hierarchy Tree

The dashboard represents the inventory as a unified tree of **Nodes** (`AssetNodeDto`).

### Node Types
- **Root**: The top-level container (Internal).
- **Folder**: Manual path segments defined in configuration.
- **Device**: The entry point for a hardware device.
- **Structured View**: (BACnet only) Folders derived from `OBJECT_STRUCTURED_VIEW`.

### ID Uniqueness
To prevent collisions, IDs are globally unique:
- **Manual Folders**: Prefixed with `path_` followed by a slugified name (e.g., `path_building_a`).
- **DataPoints**: Prefixed with the internal Device ID (e.g., `meter-01_energy_import`).

---

## 3. Data Structures (DTOs)

### AssetNodeDto (The "Asset" / "Hierarchy" Layer)
Represents a folder or device in the navigation tree.
| Property | Type | Description |
| :--- | :--- | :--- |
| `Id` | string | Globally unique ID. Used for URL deep-linking. |
| `Name` | string | Friendly display name. |
| `Type` | string | "Folder", "Device", "Structured View", etc. |
| `IsView` | bool | `true` if it acts as a container/folder. |
| `Children` | List | Sub-nodes of type `AssetNodeDto`. |
| `DataPoints` | List | List of `DataPointDto` belonging to this specific node. |

### DataPointDto (The "DataPoint" Layer)
Represents a single point of data (sensor, setpoint, or calculated metric).
| Property | Type | Description |
| :--- | :--- | :--- |
| `Id` | string | Unique identifier for favorites and navigation. |
| `Key` | string | The authoritative key used for data lookup (DeviceID_PointKey). |
| `FullName` | string | Combined name (Asset Name + Point Name) for unique identification. |
| `Value` | string | Current live value formatted as a string. |
| `Units` | string | Engineering units (e.g., "°C", "kW"). |
| `IsWritable` | bool | `true` if the point supports manual overrides. |
| `EnumValues` | List | Labels for Binary or Multi-state points. |
| `ParentPath` | List | Breadcrumb data back to the root. |

---

## 4. Storage & History

Historical data is stored as a **TimeSeries**. 
Each DataPoint is mapped to a series in the database (InfluxDB) where the full `Key` is used as a tag to retrieve history.
