namespace Pulswerk.Visual.Tests;

// ── Assets Page — Assets.cshtml (/Assets) ───────────────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec AssetTree = new()
    {
        Id = "asset-tree",
        Name = "Asset Hierarchy Tree",
        Source = "Assets.cshtml",
        PagePath = "/Assets",
        Purpose = "Resizable left sidebar (320px default, 150–800px range) showing the full BACnet " +
                  "device hierarchy as a collapsible tree. Each node has an icon (server for devices, " +
                  "folder for views, tag for points), indentation lines, and chevron toggles.",
        RequiredTestIds = [],
        RequiredSelectors = ["#assetTree", ".asset-sidebar"],
        VisualRules = [],
        Behavior = "Loads via fetch(?handler=Tree). Click node: highlights active (cyan bg tint), " +
                   "shows data points in right panel, updates URL ?node=X. " +
                   "Toggle chevron: rotates 90° via CSS. " +
                   "Tree rows: 0.85rem, hover shows rgba(255,255,255,0.05) bg. " +
                   "Active: rgba(0,209,209,0.15) bg + cyan text. " +
                   "Splitter: 4px draggable bar, cyan on hover.",
    };

    public static readonly ComponentSpec AssetDetailPanel = new()
    {
        Id = "asset-detail",
        Name = "Asset Data Points Panel",
        Source = "Assets.cshtml",
        PagePath = "/Assets",
        Purpose = "Right panel showing all data points for the selected tree node. Each point " +
                  "is a card with: type icon (40×40px cyan), name, BACnet path (monospace), " +
                  "live value (cyan, 1.125rem, weight 700), and action buttons.",
        RequiredTestIds = [],
        RequiredSelectors = [".asset-content", "#contentView", "#emptyView"],
        VisualRules = [],
        Behavior = "Hidden until a node is selected. Shows breadcrumb path at top. " +
                   "Points listed vertically with hover: border-color #008b91 + translateX(4px). " +
                   "Actions: Star (favorite toggle), Trend (history modal), " +
                   "Edit (write modal, writable only), Properties (inspector modal). " +
                   "Empty node: 'No data points in this view' message.",
    };

    public static readonly ComponentSpec AssetEmptySelection = new()
    {
        Id = "asset-empty-selection",
        Name = "Asset Empty Selection State",
        Source = "Assets.cshtml",
        PagePath = "/Assets",
        Purpose = "Full-height centered placeholder shown in the right panel before any tree node " +
                  "is selected. Displays sitemap icon (3rem, 0.5 opacity) and instruction text " +
                  "'Select a node from the tree to view data points'.",
        RequiredTestIds = [],
        RequiredSelectors = ["#emptyView"],
        VisualRules = [],
        Behavior = "Visible by default. Hidden when a tree node is clicked (JS sets display:none " +
                   "and shows #contentView). Re-shown if user navigates back without selection.",
    };
}
