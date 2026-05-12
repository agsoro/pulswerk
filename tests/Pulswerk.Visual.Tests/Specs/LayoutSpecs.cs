namespace Pulswerk.Visual.Tests;

// ── Layout Specs — _Layout.cshtml (every page) ─────────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec Sidebar = new()
    {
        Id = "sidebar",
        Name = "Sidebar Navigation",
        Source = "_Layout.cshtml",
        Purpose = "Collapsible icon-rail sidebar providing primary navigation to all 8 pages. " +
                  "Collapsed by default (72px), expands to 200px on hover revealing text labels. " +
                  "Uses accent-primary highlight for the active page link.",
        RequiredTestIds = ["sidebar", "sidebar-brand", "nav-links", "sidebar-footer"],
        RequiredSelectors = [".nav-links a[data-testid]"],
        VisualRules =
        [
            new("Dark card background", "background-color", DesignTokens.CardBg),
            new("Fixed left position", "position", "fixed"),
            new("Collapsed width (72px + padding ≈ 80px computed)", "width", "80px"),
        ],
        HoverRules =
        [
            new("Expands to 200px on hover", "width", DesignTokens.SidebarExpandedWidth),
        ],
        Behavior = "Collapsed: shows icons only (24px), text labels hidden via opacity:0. " +
                   "Hover: expands with cubic-bezier(0.4,0,0.2,1), adds 10px box-shadow. " +
                   "Active link: accent-primary bg tint + accent text. Footer shows version on hover.",
        ChildCount = new("nav-links", "a", 8, "Dashboard, Dashboards, Assets, Inventory, Connections, Alarms, Logs, Heartbeat"),
    };

    public static readonly ComponentSpec PageHeader = new()
    {
        Id = "page-header",
        Name = "Page Header Bar",
        Source = "_Layout.cshtml",
        Purpose = "Top bar showing page title (h1, 1.25rem, weight 700) and live connection status " +
                  "indicator (green dot when connected, red when disconnected).",
        RequiredTestIds = ["page-header", "page-title"],
        RequiredSelectors = ["#connection-status"],
        VisualRules =
        [
            new("Flex row with space-between", "display", "flex"),
            new("Bottom spacing", "margin-bottom", "16px"),
        ],
        Behavior = "Title is set from ViewData['Title'] per page. Connection dot is 8×8px circle. " +
                   "Machine name shows after dot.",
    };
}
