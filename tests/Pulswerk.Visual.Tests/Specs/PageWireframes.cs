namespace Pulswerk.Visual.Tests;

// ── Page Wireframes — expected layout zones per page ────────────────────
// These define the structural contract for each page.
// Zone constraints use RELATIVE sizing (% of viewport) plus minimum pixel
// thresholds, making them resilient to different viewport sizes.
// Wireframe images are stored in Wireframes/ for human reference.

public static class PageWireframes
{
    // ── Dashboard Overview (/) ──────────────────────────────────────────

    public static readonly LayoutZone[] DashboardOverview =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            // Sidebar is position:fixed — height depends on actual viewport
            MinHeight = 400,
        },
        new()
        {
            Label = "page-header",
            Selector = "[data-testid='page-header']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 24, MaxHeight = 80,
        },
        new()
        {
            Label = "alarm-boxes",
            Selector = "[data-testid='alarm-boxes']",
            MustBeRightOf = "[data-testid='sidebar']",
            MustBeAbove = "[data-testid='favorites-list']",
            MinWidth = 500,
            MinHeight = 70, MaxHeight = 300,
        },
        new()
        {
            Label = "favorites-list",
            Selector = "[data-testid='favorites-list']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 300,
            // Empty favorites state may have minimal height
            MinHeight = 0,
        },
    ];

    // ── Dashboards (/Dashboards — list mode) ────────────────────────────

    public static readonly LayoutZone[] DashboardsList =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            MinHeight = 400,
        },
        new()
        {
            Label = "page-header",
            Selector = "[data-testid='page-header']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 24, MaxHeight = 80,
        },
        new()
        {
            Label = "dash-list-mode",
            Selector = "[data-testid='dash-list-mode']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 100,
        },
        new()
        {
            Label = "dash-grid",
            Selector = "[data-testid='dash-grid']",
            MinWidth = 280,
            MinHeight = 40,
        },
    ];

    // ── Assets (/Assets) ────────────────────────────────────────────────

    public static readonly LayoutZone[] Assets =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            MinHeight = 400,
        },
        new()
        {
            Label = "asset-sidebar (tree panel)",
            Selector = ".asset-sidebar",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 150, MaxWidth = 700,
            MinHeight = 200,
        },
        new()
        {
            Label = "asset-content (detail panel)",
            Selector = ".asset-content",
            MustBeRightOf = ".asset-sidebar",
            MinWidth = 50,
            MinHeight = 100,
            IsOptional = true, // Hidden until a tree node is selected
        },
    ];

    // ── Connections (/Connections) ───────────────────────────────────────

    public static readonly LayoutZone[] Connections =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            MinHeight = 400,
        },
        new()
        {
            Label = "conn-layout",
            Selector = "[data-testid='conn-layout']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 200,
        },
        new()
        {
            Label = "conn-list",
            Selector = "[data-testid='conn-list']",
            MinWidth = 180,
            MinHeight = 150,
        },
        new()
        {
            Label = "conn-detail",
            Selector = "[data-testid='conn-detail']",
            MustBeRightOf = "[data-testid='conn-list']",
            MinWidth = 200,
        },
    ];

    // ── Alarms (/Alarms) ────────────────────────────────────────────────

    public static readonly LayoutZone[] Alarms =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            MinHeight = 400,
        },
        new()
        {
            Label = "alarm-filters",
            Selector = "[data-testid='alarm-filters']",
            MustBeRightOf = "[data-testid='sidebar']",
            MustBeAbove = "[data-testid='alarm-list']",
            MinWidth = 250,
            MinHeight = 24, MaxHeight = 80,
        },
        new()
        {
            Label = "alarm-list",
            Selector = "[data-testid='alarm-list']",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 100,
        },
    ];

    // ── Logs (/Logs) ────────────────────────────────────────────────────

    public static readonly LayoutZone[] Logs =
    [
        new()
        {
            Label = "sidebar",
            Selector = "[data-testid='sidebar']",
            ExpectedX = 0, ExpectedY = 0,
            MinWidth = 70, MaxWidth = 85,
            MinHeight = 400,
        },
        new()
        {
            Label = "log-container",
            Selector = ".log-container",
            MustBeRightOf = "[data-testid='sidebar']",
            MinWidth = 500,
            MinHeight = 200,
        },
    ];

    // ── Registry ────────────────────────────────────────────────────────

    public static readonly (string PagePath, string PageName, LayoutZone[] Zones)[] All =
    [
        ("/",            "Dashboard Overview", DashboardOverview),
        ("/Dashboards",  "Dashboards List",    DashboardsList),
        ("/Assets",      "Assets",             Assets),
        ("/Connections",  "Connections",        Connections),
        ("/Alarms",      "Alarms",             Alarms),
        ("/Logs",        "Logs",               Logs),
    ];

    // ═══════════════════════════════════════════════════════════════════════
    //  MODAL / POPUP / PARTIAL WIREFRAMES
    // ═══════════════════════════════════════════════════════════════════════

    // ── Create Dashboard Modal ──────────────────────────────────────────

    public static readonly LayoutZone[] CreateDashboardModal =
    [
        new() { Label = "backdrop", Selector = "#createModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#createModal .modal-content", MinWidth = 300, MaxWidth = 500, MinHeight = 150 },
        new() { Label = "name-input", Selector = "#newDashName", MinWidth = 200, MinHeight = 25 },
        new() { Label = "desc-input", Selector = "#newDashDesc", MinWidth = 200, MinHeight = 25 },
        new() { Label = "create-btn", Selector = "#createModal .btn-primary", MinWidth = 100, MinHeight = 30 },
    ];

    // ── Add Widget Modal ────────────────────────────────────────────────

    public static readonly LayoutZone[] AddWidgetModal =
    [
        new() { Label = "backdrop", Selector = "#addWidgetModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#addWidgetModal .modal-content", MinWidth = 400, MinHeight = 300 },
        new() { Label = "widget-type-selector", Selector = "#widgetTypeSelector", MinWidth = 200, MinHeight = 30 },
        new() { Label = "key-tree", Selector = "#keyTree", MinWidth = 200, MinHeight = 50 },
        new() { Label = "confirm-btn", Selector = "#btnAddWidgetConfirm", MinWidth = 100, MinHeight = 30 },
    ];

    // ── Trend History Modal ─────────────────────────────────────────────

    public static readonly LayoutZone[] HistoryModal =
    [
        new() { Label = "backdrop", Selector = "#historyModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#historyModal .modal-content", MinWidth = 600, MinHeight = 300 },
        new() { Label = "chart-title", Selector = "#chartTitle", MinWidth = 50, MinHeight = 15 },
        new() { Label = "day-selector", Selector = "#daysSelector", MinWidth = 100, MinHeight = 25 },
        new() { Label = "chart-area", Selector = ".chart-container", MinWidth = 400, MinHeight = 150 },
        new() { Label = "close-btn", Selector = "#historyModal .close-modal", MinWidth = 24, MinHeight = 24 },
    ];

    // ── Edit Modal — Numeric Stepper ────────────────────────────────────

    public static readonly LayoutZone[] EditModalNumeric =
    [
        new() { Label = "backdrop", Selector = "#editModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#editModal .edit-modal-content", MinWidth = 300, MaxWidth = 500, MinHeight = 200 },
        new() { Label = "current-value", Selector = "#editCurrentValue", MinWidth = 20, MinHeight = 15 },
        new() { Label = "stepper", Selector = "#editStepper", MinWidth = 100, MinHeight = 35 },
        new() { Label = "minus-btn", Selector = "#editStepper .stepper-btn:first-child", MinWidth = 35, MinHeight = 35 },
        new() { Label = "stepper-input", Selector = ".stepper-input", MinWidth = 40, MinHeight = 35 },
        new() { Label = "plus-btn", Selector = "#editStepper .stepper-btn:last-child", MinWidth = 35, MinHeight = 35 },
        new() { Label = "save-btn", Selector = "#saveBtn", MinWidth = 100, MinHeight = 35 },
    ];

    // ── Edit Modal — Enum Dropdown ──────────────────────────────────────

    public static readonly LayoutZone[] EditModalEnum =
    [
        new() { Label = "backdrop", Selector = "#editModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#editModal .edit-modal-content", MinWidth = 300, MaxWidth = 500, MinHeight = 200 },
        new() { Label = "enum-select", Selector = "#editEnumSelect", MinWidth = 150, MinHeight = 30 },
        new() { Label = "save-btn", Selector = "#saveBtn", MinWidth = 100, MinHeight = 35 },
    ];

    // ── Edit Modal — Boolean Toggle ─────────────────────────────────────

    public static readonly LayoutZone[] EditModalBoolean =
    [
        new() { Label = "backdrop", Selector = "#editModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#editModal .edit-modal-content", MinWidth = 300, MaxWidth = 500, MinHeight = 200 },
        new() { Label = "toggle-wrap", Selector = "#editToggleWrap", MinWidth = 60, MinHeight = 25 },
        new() { Label = "toggle-label", Selector = "#editToggleLabel", MinWidth = 15, MinHeight = 12 },
        new() { Label = "save-btn", Selector = "#saveBtn", MinWidth = 100, MinHeight = 35 },
    ];

    // ── Properties Modal ────────────────────────────────────────────────

    public static readonly LayoutZone[] PropertiesModal =
    [
        new() { Label = "backdrop", Selector = "#propsModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#propsModal .modal-content", MinWidth = 600, MinHeight = 300 },
        new() { Label = "props-title", Selector = "#propsTitle", MinWidth = 50, MinHeight = 15 },
        new() { Label = "props-table", Selector = ".props-table-container", MinWidth = 400, MinHeight = 100 },
        new() { Label = "close-btn", Selector = "#propsModal .close-modal", MinWidth = 24, MinHeight = 24 },
    ];

    // ── Alarm Acknowledge Modal ─────────────────────────────────────────

    public static readonly LayoutZone[] AlarmAckModal =
    [
        new() { Label = "backdrop", Selector = "#ackModal", MinWidth = 800, MinHeight = 400 },
        new() { Label = "modal-card", Selector = "#ackModal .modal-content", MinWidth = 300, MaxWidth = 500, MinHeight = 150 },
        new() { Label = "confirm-btn", Selector = "#ackConfirmBtn", MinWidth = 80, MinHeight = 30 },
        new() { Label = "cancel-btn", Selector = "#ackCancelBtn", MinWidth = 60, MinHeight = 30 },
    ];

    // ── Timewindow Dropdown — Realtime ──────────────────────────────────

    public static readonly LayoutZone[] TimewindowRealtime =
    [
        new() { Label = "tw-selector-btn", Selector = "[data-testid='tw-selector']", MinWidth = 100, MinHeight = 25 },
        new() { Label = "tw-dropdown", Selector = "#twDropdown", MinWidth = 200, MinHeight = 120 },
        new() { Label = "realtime-tab", Selector = "button[data-tw-mode='realtime']", MinWidth = 60, MinHeight = 24 },
        new() { Label = "history-tab", Selector = "button[data-tw-mode='history']", MinWidth = 50, MinHeight = 24 },
        new() { Label = "presets-panel", Selector = "#twRealtimePanel", MinWidth = 150, MinHeight = 50 },
        new() { Label = "presets-grid", Selector = "#twPresets", MinWidth = 150, MinHeight = 30 },
    ];

    // ── Timewindow Dropdown — History ───────────────────────────────────

    public static readonly LayoutZone[] TimewindowHistory =
    [
        new() { Label = "tw-dropdown", Selector = "#twDropdown", MinWidth = 200, MinHeight = 100 },
        new() { Label = "history-panel", Selector = "#twHistoryPanel", MinWidth = 150, MinHeight = 50 },
        new() { Label = "from-input", Selector = "#twHistFrom", MinWidth = 100, MinHeight = 20 },
        new() { Label = "to-input", Selector = "#twHistTo", MinWidth = 100, MinHeight = 20 },
        new() { Label = "apply-btn", Selector = ".tw-apply", MinWidth = 60, MinHeight = 24 },
    ];

    // ── Sidebar — Collapsed (icon rail) ─────────────────────────────────

    public static readonly LayoutZone[] SidebarCollapsed =
    [
        new() { Label = "sidebar-rail", Selector = "[data-testid='sidebar']", MinWidth = 60, MaxWidth = 90, MinHeight = 400 },
        new() { Label = "brand-logo", Selector = ".brand", MinWidth = 30, MinHeight = 20 },
        new() { Label = "nav-links", Selector = ".nav-links", MinWidth = 40, MinHeight = 200 },
    ];

    // ── All modals/popups registry ──────────────────────────────────────

    public static readonly (string Name, LayoutZone[] Zones)[] ModalWireframes =
    [
        ("Create Dashboard Modal",    CreateDashboardModal),
        ("Add Widget Modal",          AddWidgetModal),
        ("History Modal",             HistoryModal),
        ("Edit Modal — Numeric",      EditModalNumeric),
        ("Edit Modal — Enum",         EditModalEnum),
        ("Edit Modal — Boolean",      EditModalBoolean),
        ("Properties Modal",          PropertiesModal),
        ("Alarm Ack Modal",           AlarmAckModal),
        ("Timewindow — Realtime",     TimewindowRealtime),
        ("Timewindow — History",      TimewindowHistory),
        ("Sidebar — Collapsed",       SidebarCollapsed),
    ];
}
