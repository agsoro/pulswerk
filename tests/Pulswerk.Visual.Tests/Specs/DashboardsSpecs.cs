namespace Pulswerk.Visual.Tests;

// ── Dashboard Page — Dashboards.cshtml (/Dashboards) ─────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec DashboardListMode = new()
    {
        Id = "dash-list-mode",
        Name = "Dashboard List View",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Grid of user-created dashboard cards (minmax 320px). Each card shows name, " +
                  "description (2-line clamp), widget count badge, and last-modified date. " +
                  "Uses glassmorphism (backdrop-filter blur 12px).",
        RequiredTestIds = ["dash-list-mode", "dash-create-btn", "dash-grid"],
        RequiredSelectors = [".dash-grid"],
        Behavior = "Click card → opens dashboard view. 'New Dashboard' button → create modal. " +
                   "Card hover: translateY(-3px), accent-primary border glow, shadow. " +
                   "Each card has Edit/Delete action buttons in footer.",
    };

    public static readonly ComponentSpec DashboardEditMode = new()
    {
        Id = "dash-edit-mode",
        Name = "Dashboard View/Edit Mode",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Interactive dashboard canvas with GridStack drag-resize widgets. " +
                  "View mode: displays widgets read-only. Edit mode: enables grid manipulation, " +
                  "shows Save/Cancel/Add Widget toolbar buttons.",
        RequiredTestIds = ["dash-edit-mode", "dash-toolbar", "dash-edit-btn", "dash-save-btn",
                           "dash-cancel-btn", "dash-add-widget-btn", "tw-selector"],
        RequiredSelectors = [".dash-toolbar", ".grid-stack"],
        Behavior = "Toolbar: back-link (←), title, timewindow selector, edit/save/cancel buttons. " +
                   "Edit mode toggle: shows Save+Cancel+AddWidget, hides Edit. " +
                   "Save: POST layout JSON. Cancel: reload. " +
                   "Widgets: chart, single-value, gauge — each with title and body area.",
    };

    public static readonly ComponentSpec TimewindowSelector = new()
    {
        Id = "tw-selector",
        Name = "Timewindow Selector",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Compact toolbar button that opens a dropdown for selecting the time range " +
                  "of all dashboard charts. Supports two modes: Realtime (last N minutes/hours) " +
                  "and History (custom date range with from/to pickers).",
        RequiredTestIds = ["tw-selector"],
        RequiredSelectors = [".tw-selector"],
        VisualRules =
        [
            new("Compact size with subtle border", "font-size", "13.12px"),
        ],
        Behavior = "Click opens dropdown below (position: absolute). Tabs switch Realtime/History. " +
                   "Realtime: preset buttons (Last 5m, 15m, 30m, 1h, 6h, 12h, 24h, 7d). " +
                   "History: datetime-local inputs for From/To + Apply button. " +
                   "Selected preset highlighted with accent-primary. Label updates to show selection.",
    };

    public static readonly ComponentSpec TimewindowDropdown = new()
    {
        Id = "tw-dropdown",
        Name = "Timewindow Dropdown Panel",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Dropdown panel positioned below the timewindow selector button. Contains " +
                  "two tabs: Realtime (preset duration buttons) and History (datetime-local pickers). " +
                  "Hidden by default, shown via .open class toggle.",
        RequiredTestIds = [],
        RequiredSelectors = [".tw-dropdown", ".tw-tabs", ".tw-presets"],
        VisualRules = [],
        Behavior = "Click tw-selector toggles .open class. Realtime tab: grid of preset buttons " +
                   "(5m, 15m, 30m, 1h, 6h, 12h, 24h, 7d). Active preset: accent-primary bg tint. " +
                   "History tab: From/To datetime-local inputs + Apply button. " +
                   "Click outside closes dropdown. Label updates to reflect selection.",
    };

    public static readonly ComponentSpec CreateDashboardModal = new()
    {
        Id = "create-dash-modal",
        Name = "Create Dashboard Modal",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Centered overlay modal with name input field for creating a new empty dashboard. " +
                  "Validates non-empty name. Backdrop: rgba(15,23,42,0.9) + blur(12px).",
        RequiredTestIds = ["create-dash-modal"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens on 'New Dashboard' click. Input field is autofocused. " +
                   "Create button: accent-primary bg, black text, weight 700. " +
                   "Submits via POST, redirects to new dashboard. " +
                   "Close: X button or backdrop click.",
        IsModal = true,
        TriggerSelector = "[data-testid='dash-create-btn']",
    };

    public static readonly ComponentSpec AddWidgetModal = new()
    {
        Id = "add-widget-modal",
        Name = "Add Widget Modal",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Full-featured configuration modal for adding/editing dashboard widgets. " +
                  "Top: widget type selector (Chart, Single Value, Gauge) as icon cards. " +
                  "Middle: asset tree with checkbox multi-select for data points. " +
                  "Bottom: title input + Add/Save button.",
        RequiredTestIds = ["add-widget-modal"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens from Edit mode toolbar. Type selection shows/hides config sections. " +
                   "Asset tree: expandable with checkboxes per data point. " +
                   "Single Value: single-select only. Chart: multi-select with legend preview. " +
                   "Save adds widget to GridStack canvas at next available position.",
        IsModal = true,
    };

    public static readonly ComponentSpec EmptyDashboardsList = new()
    {
        Id = "empty-dashboards",
        Name = "Empty Dashboards List State",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Centered empty state shown when no user dashboards exist. Shows puzzle-piece " +
                  "icon (3rem, 0.3 opacity), 'No dashboards yet' message, and 'Create your first " +
                  "dashboard' button with glassmorphism card background.",
        RequiredTestIds = [],
        RequiredSelectors = ["#emptyDashboards"],
        VisualRules = [],
        Behavior = "Visible when dashboard list is empty (JS toggles display). Hidden when at " +
                   "least one dashboard exists. Create button triggers createDashboard() modal.",
    };

    public static readonly ComponentSpec EmptyDashboardWidgets = new()
    {
        Id = "empty-dash-widgets",
        Name = "Empty Dashboard Widgets State",
        Source = "Dashboards.cshtml",
        PagePath = "/Dashboards",
        Purpose = "Shown inside a dashboard view when it has zero widgets. Displays puzzle-piece " +
                  "icon, message 'This dashboard has no widgets yet', and 'Add your first widget' " +
                  "button that enters edit mode and opens the add widget modal.",
        RequiredTestIds = [],
        RequiredSelectors = ["#emptyDash"],
        VisualRules = [],
        Behavior = "JS checks widget count on dashboard load. Shown with display:block when 0 widgets. " +
                   "Hidden when widgets exist. Button calls enterEditMode() then openAddWidget().",
    };
}
