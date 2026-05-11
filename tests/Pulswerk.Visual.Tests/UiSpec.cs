namespace Pulswerk.Visual.Tests;

/// <summary>
/// UI Specification Registry — the single source of truth for the intended look,
/// behavior, and structure of every view, partial, modal, and popup.
///
/// Component specs are defined in the Specs/ folder, split by page path:
///   Specs/DesignTokens.cs           — Color/type/layout tokens
///   Specs/LayoutSpecs.cs            — Sidebar, PageHeader (_Layout.cshtml)
///   Specs/DashboardOverviewSpecs.cs — AlarmBoxes, Favorites (Index.cshtml)
///   Specs/DashboardsSpecs.cs        — Dashboard list/edit, Timewindow, Modals
///   Specs/AssetsSpecs.cs            — Asset tree, detail panel
///   Specs/ConnectionsSpecs.cs       — Master-detail layout
///   Specs/AlarmsSpecs.cs            — Filter bar, alarm list, ack modal
///   Specs/LogsSpecs.cs              — Log terminal console
///   Specs/SharedModalSpecs.cs       — History, Edit, Properties modals
///
/// This file IS the design contract. When you add a new component, you document it
/// in the appropriate Specs/ file, register it here, and <see cref="UiSpecTests"/>
/// enforces it automatically.
/// </summary>
public static partial class UiSpec
{
    // ═══════════════════════════════════════════════════════════════════════
    //  REGISTRY — every spec must be listed here
    // ═══════════════════════════════════════════════════════════════════════

    public static readonly ComponentSpec[] All =
    [
        // Layout (every page) — LayoutSpecs.cs
        Sidebar, PageHeader,

        // Dashboard overview (/) — DashboardOverviewSpecs.cs
        AlarmBoxes, FavoritesSection, EmptyFavoritesState,

        // Dashboards (/Dashboards) — DashboardsSpecs.cs
        DashboardListMode, DashboardEditMode, TimewindowSelector, TimewindowDropdown,
        CreateDashboardModal, AddWidgetModal,
        EmptyDashboardsList, EmptyDashboardWidgets,

        // Assets (/Assets) — AssetsSpecs.cs
        AssetTree, AssetDetailPanel, AssetEmptySelection,

        // Connections (/Connections) — ConnectionsSpecs.cs
        ConnectionsLayout,

        // Alarms (/Alarms) — AlarmsSpecs.cs
        AlarmFilters, AlarmList, AckModal, EmptyAlarmState,

        // Logs (/Logs) — LogsSpecs.cs
        LogConsole,

        // Shared modals — SharedModalSpecs.cs
        HistoryModal, EditModal, PropertiesModal,

        // Modal variants & loading — SharedModalSpecs.cs
        EditModalEnumVariant, EditModalBoolVariant, ChartLoadingSpinner,
    ];
}

// ── Spec data types ─────────────────────────────────────────────────────────

/// <summary>CSS property assertion: property must have expected value.</summary>
public record VisualRule(string Description, string CssProperty, string ExpectedValue);

/// <summary>CSS property assertion on hover state.</summary>
public record HoverRule(string Description, string CssProperty, string ExpectedValue);

/// <summary>Expected child element count for a container.</summary>
public record ChildCountRule(string ParentTestId, string ChildSelector, int ExpectedCount, string Reason);

/// <summary>
/// Layout zone — an expected bounding region for wireframe-based validation.
/// Coordinates are in pixels at 1920×1080 viewport.
/// Use null for any dimension that should not be constrained.
/// </summary>
public record LayoutZone
{
    /// <summary>Human-readable label for this zone (shown in wireframe overlays).</summary>
    public required string Label { get; init; }

    /// <summary>CSS selector to locate this zone in the DOM.</summary>
    public required string Selector { get; init; }

    // ── Position constraints (nullable = unconstrained) ──
    public float? ExpectedX { get; init; }
    public float? ExpectedY { get; init; }
    public float? MinWidth { get; init; }
    public float? MaxWidth { get; init; }
    public float? MinHeight { get; init; }
    public float? MaxHeight { get; init; }

    // ── Edge constraints ──
    /// <summary>If set, element's left edge must be within this tolerance of ExpectedX.</summary>
    public float PositionTolerance { get; init; } = 20;

    /// <summary>If true, element should span the full viewport width (minus sidebar).</summary>
    public bool FullWidth { get; init; }

    /// <summary>If true, element should span the full viewport height.</summary>
    public bool FullHeight { get; init; }

    /// <summary>Element must appear ABOVE this selector (lower Y value).</summary>
    public string? MustBeAbove { get; init; }

    /// <summary>Element must appear to the RIGHT of this selector.</summary>
    public string? MustBeRightOf { get; init; }

    /// <summary>If true, zone may be absent/hidden (e.g. panels requiring user interaction).</summary>
    public bool IsOptional { get; init; }
}

/// <summary>Full specification for a UI component.</summary>
public record ComponentSpec
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Source { get; init; }
    public required string Purpose { get; init; }
    public string? PagePath { get; init; }

    // Structure
    public string[] RequiredTestIds { get; init; } = [];
    public string[] RequiredSelectors { get; init; } = [];

    // Visual baseline
    public VisualRule[] VisualRules { get; init; } = [];
    public HoverRule[] HoverRules { get; init; } = [];

    // Behavior
    public string? Behavior { get; init; }
    public ChildCountRule? ChildCount { get; init; }

    // Modal specifics
    public bool IsModal { get; init; }
    public string? TriggerSelector { get; init; }

    /// <summary>True if element is server-rendered conditionally (may not exist in DOM).</summary>
    public bool IsConditional { get; init; }

    // Layout zones for wireframe-based validation
    public LayoutZone[] LayoutZones { get; init; } = [];

    public override string ToString() => Name;
}
