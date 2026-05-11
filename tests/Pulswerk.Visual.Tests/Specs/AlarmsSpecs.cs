namespace Pulswerk.Visual.Tests;

// ── Alarms Page — Alarms.cshtml (/Alarms) ────────────────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec AlarmFilters = new()
    {
        Id = "alarm-filters",
        Name = "Alarm Severity Filter Bar",
        Source = "Alarms.cshtml",
        PagePath = "/Alarms",
        Purpose = "Horizontal chip bar for filtering alarms by severity. Each chip shows " +
                  "severity label + count badge. Active chip uses severity-specific color. " +
                  "'All' chip aggregates total count.",
        RequiredTestIds = ["alarm-filters"],
        Behavior = "Click chip: navigates to /Alarms?severity=X. Active chip: filled bg + white text. " +
                   "Inactive: transparent bg + border. Severity colors match alarm box scheme.",
    };

    public static readonly ComponentSpec AlarmList = new()
    {
        Id = "alarm-list",
        Name = "Alarm List Table",
        Source = "Alarms.cshtml",
        PagePath = "/Alarms",
        Purpose = "Chronological list of active alarms with: severity badge (color-coded), " +
                  "source device name, alarm type, timestamp, and Acknowledge button. " +
                  "Each row uses glassmorphism card styling.",
        RequiredTestIds = ["alarm-list"],
        Behavior = "Rows sorted newest-first. Each row: severity pill, device name (weight 600), " +
                   "alarm type, relative timestamp. Ack button: ghost style, opens confirm modal. " +
                   "Empty state when no alarms match filter.",
    };

    public static readonly ComponentSpec AckModal = new()
    {
        Id = "ack-modal",
        Name = "Alarm Acknowledge Modal",
        Source = "Alarms.cshtml",
        PagePath = "/Alarms",
        Purpose = "Confirmation overlay showing alarm details (source, type, time) and requiring " +
                  "explicit Confirm/Cancel action. Confirm sends POST to acknowledge the alarm.",
        RequiredTestIds = ["ack-modal"],
        VisualRules =
        [
            new("Hidden by default", "display", "none"),
        ],
        Behavior = "Opens on alarm row Ack click. Shows alarm source and type. " +
                   "Confirm: accent-primary button, sends POST. Cancel: ghost button, closes. " +
                   "Backdrop click also closes. Success removes row from list.",
        IsModal = true,
        TriggerSelector = ".ack-btn",
    };

    public static readonly ComponentSpec EmptyAlarmState = new()
    {
        Id = "empty-alarms",
        Name = "Alarms Empty State",
        Source = "Alarms.cshtml",
        PagePath = "/Alarms",
        Purpose = "Centered empty state shown when no active alarms match the current severity filter. " +
                  "Displays bell-slash icon (3rem, 0.2 opacity) and 'No active alarms detected' message. " +
                  "Uses glassmorphism card background.",
        RequiredTestIds = [],
        RequiredSelectors = [".empty-state"],
        IsConditional = true,
        VisualRules = [],
        Behavior = "Server-rendered: shown when Model.Alarms.Count == 0. Hidden when alarms exist. " +
                   "Appears both for 'All' filter and individual severity filters.",
    };
}
