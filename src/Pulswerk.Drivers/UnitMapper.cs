using System.Collections.Generic;
using System.IO.BACnet;
using Pulswerk.Core;

namespace Pulswerk.Drivers
{
    /// <summary>
    /// BACnet-specific helper that turns the library's <see cref="BacnetUnitsId"/> enum /
    /// numeric unit codes into a display string. The actual name→symbol normalization is
    /// delegated to the shared <see cref="Pulswerk.Core.Units"/> registry so BACnet uses
    /// the same canonical symbols as every other driver.
    /// </summary>
    public static class UnitMapper
    {
        private static readonly HashSet<string> _loggedUnmapped = new();

        /// <summary>
        /// Extended BACnet unit codes not present in the library's BacnetUnitsId enum.
        /// See ASHRAE 135-2020 / Addendum bj for the full table.
        /// </summary>
        private static readonly Dictionary<uint, string> _extendedUnits = new()
        {
            [318] = Units.Hectopascal,
            [319] = Units.Millibar,
            // Add more as needed from production "No mapping" logs
        };

        /// <summary>
        /// Normalizes a spelled-out unit name to its canonical symbol via the shared
        /// <see cref="Units"/> registry, logging once when no mapping exists.
        /// </summary>
        public static string Map(string unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return "";

            var result = Units.Normalize(unit);

            if (result == unit.Trim() && _loggedUnmapped.Add(unit))
            {
                // Log unmapped units once to help expand the shared alias table
                Log.Debug($"[UnitMapper] No mapping for unit: '{unit}'");
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
