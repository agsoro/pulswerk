// TelemetryKeys.cs – Centralized constants for data point keys
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

        private static readonly System.Collections.Generic.Dictionary<string, (string Name, string Unit)> _metadata = new()
        {
            [PowerKw] = ("Leistung", "kW"),
            [EnergyImportKwh] = ("Energie Import", "kWh"),
            [EnergyExportKwh] = ("Energie Export", "kWh"),
            [PowerLimitPct] = ("Leistungsgrenze", "%"),
            [UtilityLimitPct] = ("Netzgrenze", "%")
        };

        public static string GetFriendlyName(string key) => _metadata.TryGetValue(key, out var m) ? m.Name : key;
        public static string GetFriendlyUnit(string key) => _metadata.TryGetValue(key, out var m) ? m.Unit : "";
    }
}
