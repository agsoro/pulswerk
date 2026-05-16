# Virtual Devices & Data Points

Virtual devices allow you to create "Calculated" data points that are derived from other real or virtual points in the system. These are useful for aggregations, KPIs, and multi-step analytics.

## Virtual Device Definition

A virtual device is defined in the configuration with `"deviceType": "virtual"`. It contains a list of `dataPoints`, each with a `formula`.

```json
{
  "id": "building-analytics",
  "name": "Building Analytics",
  "deviceType": "virtual",
  "dataPoints": [
    {
      "id": "total-energy",
      "name": "Total Building Energy",
      "formula": "pathsum(\"Building A\", \"energy_import\")",
      "units": "kWh"
    }
  ]
}
```

## The `pathsum` Function

The `pathsum` function is the primary tool for spatial aggregation. it searches the asset hierarchy for data points matching specific criteria and returns their current sum.

### Syntax
`pathsum(path_selector, filter)`

| Parameter | Description |
| :--- | :--- |
| `path_selector` | The location in the asset tree to search. Supports wildcards and combined key patterns. |
| `filter` | A string used to filter the discovered points. Usually the technical unit (e.g., `kWh`) or a key pattern. |

### Advanced Selection & Wildcards

Both parameters support `*` as a glob-style wildcard.

#### 1. Unit/Key Filtering (Second Parameter)
*   **Exact Unit**: `pathsum("Building A", "kWh")` sums all points in "Building A" with the unit "kWh".
*   **Global Wildcard**: `pathsum("Building A", "*")` sums **all** numerical data points found in that path.
*   **Key Pattern**: `pathsum("Building A", "temp_*")` sums all points whose key matches the pattern (e.g., `temp_room_1`, `temp_outdoor`).

#### 2. Path Selection (First Parameter)
*   **Subtree Matching**: `pathsum("Building A/*", "energy_import")` matches any asset located anywhere inside "Building A".
*   **Combined Path & Key**: `pathsum("Building A/Floor 1/*_energy_import", "kWh")`
    *   The part before the last `/` is the **Path**.
    *   The part after the last `/` (if it contains `*`) is an explicit **Key Pattern**.
    *   This allows precise selection of specific registers across a range of devices in a sub-path.

## Consumption Modifiers

Calculated points can be combined with temporal modifiers to calculate deltas over time.

*   `pathsum(...):consumption:1h` -> Hourly delta of the sum.
*   `pathsum(...):consumption:1d` -> Daily delta of the sum.

## Simple Formulas

Virtual points can also reference other points directly or perform simple arithmetic:

*   `device-id_point-key * 2`
*   `point_a / point_b`
*   `100` (Static constant)

> [!NOTE]
> All formulas are evaluated in real-time. If a source point is offline or missing, the calculated point will reflect a fallback state (usually 0 or "-").
