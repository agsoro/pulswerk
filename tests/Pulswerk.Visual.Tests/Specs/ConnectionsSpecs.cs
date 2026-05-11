namespace Pulswerk.Visual.Tests;

// ── Connections Page — Connections.cshtml (/Connections) ──────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec ConnectionsLayout = new()
    {
        Id = "conn-layout",
        Name = "Connections Master-Detail",
        Source = "Connections.cshtml",
        PagePath = "/Connections",
        Purpose = "Two-panel layout: left shows configured BACnet/Modbus connection cards with " +
                  "status indicators (green/red dot). Right shows discovered device details table " +
                  "when a card is selected.",
        RequiredTestIds = ["conn-layout", "conn-list", "conn-detail", "conn-detail-empty"],
        Behavior = "Click connection card: loads device table in detail panel. " +
                   "Each card: name, protocol badge (BACnet/Modbus), IP:port, status dot. " +
                   "Detail: table with Object ID, Name, Type, Value columns. " +
                   "Empty state: 'Select a connection' centered message.",
    };
}
