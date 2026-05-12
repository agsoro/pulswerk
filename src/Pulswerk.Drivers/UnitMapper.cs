using System.Collections.Generic;
using System.IO.BACnet;
using System.Linq;

namespace Pulswerk.Drivers
{
    public static class UnitMapper
    {
        private static readonly HashSet<string> _loggedUnmapped = new();

        /// <summary>
        /// Extended BACnet unit codes not present in the library's BacnetUnitsId enum.
        /// See ASHRAE 135-2020 / Addendum bj for the full table.
        /// </summary>
        private static readonly Dictionary<uint, string> _extendedUnits = new()
        {
            [318] = "hPa",       // hectopascals
            [319] = "mbar",      // millibar
            // Add more as needed from production "No mapping" logs
        };

        public static string Map(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return "";

            var normalized = new string(unit.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray())
                                 .ToLowerInvariant();

            // Default internal mappings for common BACnet units
            var result = normalized switch
            {
                "degreescelsius" => "°C",
                "percent" => "%",
                "pascals" => "Pa",
                "kilopascals" => "kPa",
                "nounits" => "",
                "percentrelativehumidity" => "% r.F.",
                "cubicmetersperhour" => "m³/h",
                "partspermillion" => "ppm",
                "literspersecond" => "l/s",
                "volts" => "V",
                "millivolts" => "mV",
                "amperes" => "A",
                "milliamperes" => "mA",
                "kilowatts" => "kW",
                "kilowatthours" => "kWh",
                "hertz" => "Hz",
                "revolutionsperminute" => "rpm",
                "watts" => "W",
                "degreesfahrenheit" => "°F",
                "kelvins" => "K",
                "degreeskelvin" => "K",
                "meterspersecond" => "m/s",
                "meters" => "m",
                "cubicmeters" => "m³",
                "liters" => "l",
                "kilograms" => "kg",
                "grams" => "g",
                "seconds" => "s",
                "minutes" => "min",
                "hours" => "h",
                "days" => "d",
                "hectopascals" => "hPa",
                "millibar" => "mbar",
                "bars" => "bar",
                "joules" => "J",
                "kilojoules" => "kJ",
                "megajoules" => "MJ",
                "watthours" => "Wh",
                "megawatthours" => "MWh",
                "btus" => "BTU",
                _ => unit // Return original if no mapping found
            };

            if (result == unit && !string.IsNullOrEmpty(unit) && _loggedUnmapped.Add(unit))
            {
                // Log unmapped units once to help expand the table
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

                // Check extended table first for unit codes missing from the library enum
                if (_extendedUnits.TryGetValue(val, out var ext))
                    return ext;

                var enumVal = (BacnetUnitsId)val;
                s = enumVal.ToString();

                // If the enum doesn't contain this value, ToString() returns the raw number
                if (s == val.ToString())
                    return ""; // Unknown unit code — return empty rather than showing "318"

                s = s.Replace("UNITS_", "").Replace("_", " ").ToLowerInvariant();
            }
            else
            {
                s = raw.ToString()?.ToLowerInvariant() ?? "";
                // BACnet error responses (e.g., "error_class_property: error_code_unknown_property")
                // occur when the object type doesn't support PROP_UNITS — treat as empty
                if (s.StartsWith("error_class") || string.IsNullOrWhiteSpace(s))
                    return "";
            }

            return Map(s);
        }
    }
}
