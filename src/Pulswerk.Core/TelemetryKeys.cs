// TelemetryKeys.cs – Centralized constants for telemetry keys
namespace Pulswerk.Core
{
    public static class TelemetryKeys
    {
        // Standard energy/power keys used across all readers
        public const string PowerKw = "power";
        public const string EnergyImportKwh = "energy_import";
        public const string EnergyExportKwh = "energy_export";
        public const string PowerLimitPct = "power_limit";
        public const string UtilityLimitPct = "utility_limit";
    }
}
