using System.Collections.Generic;
using System.IO.BACnet;
using System.Linq;
using Pulswerk.Core;

namespace Pulswerk.Drivers
{
    public static class UnitMapper
    {
        private static Dictionary<string, string>? _translations;

        public static void Initialize(Dictionary<string, string>? translations)
        {
            if (translations == null)
            {
                _translations = null;
                return;
            }
            _translations = new Dictionary<string, string>();
            foreach (var kv in translations)
            {
                var normKey = new string(kv.Key.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray())
                                  .ToLowerInvariant();
                _translations[normKey] = kv.Value;
            }
        }

        public static string Map(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return "";

            var normalized = new string(unit.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray())
                                 .ToLowerInvariant();

            if (_translations != null && _translations.TryGetValue(normalized, out var translated))
                return translated;

            // Default internal mappings for common BACnet units
            var result = normalized switch
            {
                "degreescelsius" => "°C",
                "percent" => "%",
                "pascals" => "Pa",
                "nounits" => "",
                "percentrelativehumidity" => "% r.F.",
                "cubicmetersperhour" => "m³/h",
                "literspersecond" => "l/s",
                "volts" => "V",
                "amperes" => "A",
                "kilowatts" => "kW",
                "kilowatthours" => "kWh",
                "hertz" => "Hz",
                "revolutionsperminute" => "rpm",
                "watts" => "W",
                "degreesfahrenheit" => "°F",
                "kelvins" => "K",
                "meterspersecond" => "m/s",
                "cubicmeters" => "m³",
                "liters" => "l",
                "kilograms" => "kg",
                "grams" => "g",
                "seconds" => "s",
                "minutes" => "min",
                "hours" => "h",
                "days" => "d",
                _ => unit // Return original if no mapping found
            };

            if (result == unit && !string.IsNullOrEmpty(unit))
            {
                // Log unmapped units to help expand the table
                System.Console.WriteLine($"[UnitMapper] No mapping for unit: '{unit}' (normalized: '{normalized}')");
            }

            return result;
        }

        public static string Format(object? raw)
        {
            if (raw == null) return "";

            string s;
            if (raw is BacnetUnitsId uid)
            {
                s = uid.ToString().Replace("UNITS_", "").Replace("_", " ").ToLowerInvariant();
            }
            else if (raw is byte || raw is ushort || raw is uint || raw is int)
            {
                var val = System.Convert.ToUInt32(raw);
                s = ((BacnetUnitsId)val).ToString().Replace("UNITS_", "").Replace("_", " ").ToLowerInvariant();
            }
            else
            {
                s = raw.ToString()?.ToLowerInvariant() ?? "";
            }

            var result = Map(s);
            System.Console.WriteLine($"[UnitMapper] Format '{raw}' -> '{s}' -> '{result}'");
            return result;
        }
    }
}
