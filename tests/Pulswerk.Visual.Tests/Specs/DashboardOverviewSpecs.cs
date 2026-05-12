namespace Pulswerk.Visual.Tests;

// ── Dashboard Overview — Index.cshtml (/) ───────────────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec AlarmBoxes = new()
    {
        Id = "alarm-boxes",
        Name = "Alarm Priority Boxes",
        Source = "Index.cshtml",
        PagePath = "/",
        Purpose = "Six color-coded clickable cards showing alarm counts by severity. " +
                  "Provides at-a-glance operational status. Each box uses severity-specific " +
                  "gradient backgrounds: Critical=red, Major=amber, Minor=blue, Warning=slate, Maintenance=purple, Total=dark.",
        RequiredTestIds = ["alarm-boxes"],
        RequiredSelectors = ["#box-critical", "#box-major", "#box-minor", "#box-warning", "#box-maintenance", "#box-total"],
        VisualRules =
        [
            new("CSS Grid layout", "display", "grid"),
        ],
        Behavior = "Each box links to /Alarms?severity=X. Hover: translateY(-3px) + box-shadow. " +
                   "Count text is 2.25rem weight 800. Label is 0.7rem uppercase.",
        ChildCount = new("alarm-boxes", ".alarm-box", 6, "Critical, Major, Minor, Warning, Maintenance, Total"),
    };

    public static readonly ComponentSpec FavoritesSection = new()
    {
        Id = "favorites-list",
        Name = "Favorites Grid",
        Source = "Index.cshtml",
        PagePath = "/",
        Purpose = "Grid of user-pinned BACnet data points with live values and quick-action buttons " +
                  "(Star, Trend, Edit, Properties). Shows real-time values from server polling. " +
                  "Empty state: centered guidance text with star icon.",
        RequiredTestIds = ["favorites-list"],
        RequiredSelectors = ["#emptyFavorites"],
        VisualRules = [],
        Behavior = "Populated from localStorage. Each card shows: icon (42×42px cyan), name, " +
                   "BACnet path, value+units. Trend opens history modal. Edit opens write modal " +
                   "(writable points only). Properties opens inspector. Star toggles favorite.",
    };

    public static readonly ComponentSpec EmptyFavoritesState = new()
    {
        Id = "empty-favorites",
        Name = "Favorites Empty State",
        Source = "Index.cshtml",
        PagePath = "/",
        Purpose = "Centered empty state in the favorites section. Shows star icon (3rem, 0.2 opacity), " +
                  "'You haven't added any favorites yet' heading, and instruction to pin from Assets page. " +
                  "Uses glassmorphism card background.",
        RequiredTestIds = [],
        RequiredSelectors = ["#emptyFavorites"],
        VisualRules = [],
        Behavior = "Visible when localStorage has no favorites (display:flex). Hidden when at " +
                   "least one favorite exists. Favorites grid and empty state toggle mutually.",
    };
}
