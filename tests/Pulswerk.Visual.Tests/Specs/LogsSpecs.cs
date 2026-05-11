namespace Pulswerk.Visual.Tests;

// ── Logs Page — Logs.cshtml (/Logs) ──────────────────────────────────────

public static partial class UiSpec
{
    public static readonly ComponentSpec LogConsole = new()
    {
        Id = "log-console",
        Name = "Log Terminal Console",
        Source = "Logs.cshtml",
        PagePath = "/Logs",
        Purpose = "Full-height terminal-style log viewer with dark background (#020617). " +
                  "Each log line shows: timestamp (slate, 180px), severity (colored, 60px), " +
                  "source (cyan, 120px), message (light). Uses monospace font (Fira Code). " +
                  "Header has 3 macOS-style dots (red, amber, green) and entry count.",
        RequiredTestIds = [],
        RequiredSelectors = [".log-container", ".terminal-header", ".terminal-controls"],
        VisualRules = [],
        Behavior = "Auto-scrolls to bottom on load. Shows last 500 entries. " +
                   "Refresh link reloads page. Severity colors: info=green, warning=amber, " +
                   "error=red, debug=slate. Line height: 1.4, word-break: break-all.",
    };
}
