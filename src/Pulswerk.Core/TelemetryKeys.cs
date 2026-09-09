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

        // Battery State of Charge
        public const string BatterySocPct = "battery_soc";

        // Battery / Hybrid Power Control & Setpoints
        public const string ForcePowerKw = "force_power";

        private static readonly System.Collections.Generic.Dictionary<string, (string Name, string Unit)> _metadata = new()
        {
            [PowerKw] = ("Leistung", Units.Kilowatt),
            [EnergyImportKwh] = ("Energie Import", Units.KilowattHour),
            [EnergyExportKwh] = ("Energie Export", Units.KilowattHour),
            [PowerLimitPct] = ("Leistungsgrenze", Units.Percent),
            [UtilityLimitPct] = ("Netzgrenze", Units.Percent),

            [BatterySocPct] = ("Batterie Ladezustand", Units.Percent),

            [ForcePowerKw] = ("Batterie Zwangssollwert", Units.Kilowatt)
        };

        public static string GetFriendlyName(string key) => _metadata.TryGetValue(key, out var m) ? m.Name : key;
        public static string GetFriendlyUnit(string key) => _metadata.TryGetValue(key, out var m) ? m.Unit : "";
    }
}
