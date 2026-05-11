namespace Pulswerk.Visual.Tests;

// ── Shared Modals — _AssetModals.cshtml (included on /, /Assets, /Favorites, /Dashboards) ──

public static partial class UiSpec
{
    public static readonly ComponentSpec HistoryModal = new()
    {
        Id = "history-modal",
        Name = "Trend History Chart Modal",
        Source = "_AssetModals.cshtml",
        Purpose = "Full-width chart modal (max-width 1100px, 80vh height) showing historical " +
                  "time-series data via Chart.js with gradient fill. Header: gradient text " +
                  "(white→slate), live value badge, close button. " +
                  "Controls: day-range selector (1, 3, 7, 30 days) as dropdown. " +
                  "Backdrop: dark overlay with blur(12px).",
        RequiredTestIds = ["history-modal"],
        RequiredSelectors = ["#historyModal .chart-container", "#chartLiveValue"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens from Trend button on any data point. Fetches /api/history?key=X&days=N. " +
                   "Live value updates in header during polling. Chart auto-scales Y axis. " +
                   "New live points appended (max 500, oldest dropped). " +
                   "Close: X button or backdrop click. Chart area: rgba(0,0,0,0.2) rounded bg.",
        IsModal = true,
    };

    public static readonly ComponentSpec EditModal = new()
    {
        Id = "edit-modal",
        Name = "Write Value Modal",
        Source = "_AssetModals.cshtml",
        Purpose = "Compact form modal (max-width 450px) for writing values to writable BACnet objects. " +
                  "Shows: point name, BACnet path, current value, units. " +
                  "Input: number stepper (−/input/+) with 50px buttons, or enum dropdown. " +
                  "Save: accent-primary button with status feedback (green success, red error).",
        RequiredTestIds = ["edit-modal"],
        RequiredSelectors = ["#editModal .edit-form", "#editModal .save-btn"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens from Edit button (writable points only). Stepper ± buttons increment/decrement. " +
                   "Save sends POST to write command API. Disabled state while saving (opacity 0.5). " +
                   "Success: green border status message. Error: red border status message. " +
                   "Status message auto-hides after 3 seconds.",
        IsModal = true,
    };

    public static readonly ComponentSpec PropertiesModal = new()
    {
        Id = "props-modal",
        Name = "Properties Inspector Modal",
        Source = "_AssetModals.cshtml",
        Purpose = "Read-only inspector (max-width 1100px) showing all BACnet object properties " +
                  "as a key-value table with human-readable formatting. " +
                  "Shows: object type, units, description, alarm limits, COV increment, state texts.",
        RequiredTestIds = ["props-modal"],
        RequiredSelectors = ["#propsModal .modal-content"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens from Properties button. Shows spinner while fetching /api/properties?key=X. " +
                   "Renders property rows: key (uppercase label, secondary color), " +
                   "value (primary color, monospace for numeric). Close: X button or backdrop.",
        IsModal = true,
    };

    public static readonly ComponentSpec EditModalEnumVariant = new()
    {
        Id = "edit-enum-variant",
        Name = "Edit Modal — Enum/Multi-State Input",
        Source = "_AssetModals.cshtml",
        Purpose = "Alternative input mode within the Edit Value modal for Multi-State BACnet objects. " +
                  "Replaces the number stepper with a <select> dropdown populated with state labels " +
                  "(e.g. '1 – Off', '2 – On'). Pre-selects the current value.",
        RequiredTestIds = [],
        RequiredSelectors = ["#editEnumSelect"],
        VisualRules = [],
        Behavior = "Shown when openEdit() detects non-null enumValues. Hides stepper and toggle. " +
                   "Options are 1-based (BACnet multi-state convention). Save reads selected value.",
    };

    public static readonly ComponentSpec EditModalBoolVariant = new()
    {
        Id = "edit-bool-variant",
        Name = "Edit Modal — Boolean Toggle Input",
        Source = "_AssetModals.cshtml",
        Purpose = "Alternative input mode within the Edit Value modal for Binary BACnet objects. " +
                  "Replaces the number stepper with a CSS toggle switch + On/Off label. " +
                  "Toggle sends 1/0 as the value.",
        RequiredTestIds = [],
        RequiredSelectors = ["#editToggleWrap", "#editToggle"],
        VisualRules = [],
        Behavior = "Shown when openEdit() detects binary object type. Hides stepper and enum. " +
                   "Label updates dynamically ('On'/'Off'). Checked state maps to current value.",
    };

    public static readonly ComponentSpec ChartLoadingSpinner = new()
    {
        Id = "chart-loading",
        Name = "Chart Loading Indicator",
        Source = "_AssetModals.cshtml",
        Purpose = "Inline loading spinner shown in the History modal while fetching time-series data. " +
                  "Displays spinning icon + 'Loading...' text next to the day-range selector. " +
                  "Uses accent-primary color.",
        RequiredTestIds = [],
        RequiredSelectors = ["#chartLoading"],
        VisualRules = [],
        Behavior = "Shown with display:flex when reloadHistory() starts fetch. Hidden with " +
                   "display:none when fetch completes (success or error). Animated via fa-spin class.",
    };
}
