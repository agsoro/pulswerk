namespace Pulswerk.Visual.Tests;

// ── Design Tokens ─────────────────────────────────────────────────────
// Baseline color/type/layout system from _Layout.cshtml :root variables

public static partial class UiSpec
{
    public static class DesignTokens
    {
        // Colors
        public const string BgColor = "rgb(15, 23, 42)";    // --bg-color: #0f172a
        public const string CardBg = "rgb(30, 41, 59)";    // --card-bg: #1e293b
        public const string TextPrimary = "rgb(248, 250, 252)"; // --text-primary: #f8fafc
        public const string TextSecondary = "rgb(148, 163, 184)"; // --text-secondary: #94a3b8
        public const string AccentPrimary = "rgb(56, 189, 248)";  // --accent-primary: #38bdf8
        public const string AccentSecondary = "rgb(14, 165, 233)";  // --accent-secondary: #0ea5e9
        public const string StatusOnline = "rgb(16, 185, 129)";  // --status-online: #10b981
        public const string StatusOffline = "rgb(239, 68, 68)";   // --status-offline: #ef4444
        public const string StatusWarning = "rgb(245, 158, 11)";  // --status-warning: #f59e0b
        public const string BorderColor = "rgb(51, 65, 85)";    // --border-color: #334155
        public const string Cyan = "rgb(0, 209, 209)";   // #00d1d1

        // Typography
        public const string FontFamily = "Inter";
        public const string H1Size = "20px";
        public const string BodySize = "16px";
        public const string SmallSize = "14px";
        public const string MonoFont = "monospace";

        // Layout
        public const string SidebarWidth = "72px";
        public const string SidebarExpandedWidth = "200px";
        public const string BorderRadius = "12px";
        public const string CardRadius = "20px";

        // Severity colors
        public const string Critical = "rgb(239, 68, 68)";
        public const string Major = "rgb(245, 158, 11)";
        public const string Minor = "rgb(56, 189, 248)";
    }
}
