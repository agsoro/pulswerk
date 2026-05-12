namespace Pulswerk.Visual.Tests;

// ── Inventory Overview — AssetsList.cshtml (/plswk/AssetsList) ──────────

public static partial class UiSpec
{
    public static readonly ComponentSpec InventoryTable = new()
    {
        Id = "inventory-table",
        Name = "Asset Inventory Table",
        Source = "AssetsList.cshtml",
        PagePath = "/plswk/AssetsList",
        Purpose = "Searchable, sortable flat list of all telemetry points. " +
                  "Consists of a header with search bar and stats, followed by a data table. " +
                  "Provides quick access to Trends, Writes, and Properties for any point.",
        RequiredTestIds = ["assetsTable"],
        RequiredSelectors = ["#assetSearch", "#statsCounter", "#tableBody"],
        VisualRules =
        [
            new("Sticky table header", "position", "sticky"),
            new("Modern border-collapse table", "border-collapse", "collapse"),
        ],
        Behavior = "Search: live filters rows across Name, Key, Device, and Connection. " +
                   "Sorting: clicking column headers (Connection, Device, Name, ID, Type, Value, Last Update) " +
                   "toggles asc/desc with icon indicators. " +
                   "Live Refresh: values and timestamps update every 2s without row redraw. " +
                   "Actions: row hover reveals Trend, Edit, and Properties buttons.",
    };
}
