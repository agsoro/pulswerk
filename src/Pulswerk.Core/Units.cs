// Units.cs – Single source of truth for engineering / measurement units in Pulswerk.
//
// Every component (drivers, storage, dashboard) should resolve unit strings through
// this class so that the same physical quantity is always represented by the same
// canonical symbol (e.g. "°C", "kWh", "m³/h"). This avoids the historical situation
// where KNX, BACnet, OCPP, Modbus and the calculation engine each carried their own
// independent unit tables that could drift apart.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulswerk.Core
{
    public static class Units
    {
        // ---- Canonical symbols (use these constants instead of string literals) ----
        public const string None = "";

        public const string Percent = "%";
        public const string RelativeHumidity = "% r.F.";
        public const string PartsPerMillion = "ppm";

        // Temperature
        public const string Celsius = "°C";
        public const string Fahrenheit = "°F";
        public const string Kelvin = "K";

        // Electrical
        public const string Volt = "V";
        public const string Millivolt = "mV";
        public const string Ampere = "A";
        public const string Milliampere = "mA";
        public const string Watt = "W";
        public const string Kilowatt = "kW";
        public const string Megawatt = "MW";
        public const string VoltAmpere = "VA";
        public const string Hertz = "Hz";
        public const string Ohm = "Ω";

        // Energy
        public const string WattHour = "Wh";
        public const string KilowattHour = "kWh";
        public const string MegawattHour = "MWh";
        public const string GigawattHour = "GWh";
        public const string Joule = "J";
        public const string Kilojoule = "kJ";
        public const string Megajoule = "MJ";
        public const string Btu = "BTU";
        public const string VoltAmpereHour = "VAh";
        public const string VoltAmpereReactiveHour = "VARh";

        // Pressure
        public const string Pascal = "Pa";
        public const string Kilopascal = "kPa";
        public const string Hectopascal = "hPa";
        public const string Millibar = "mbar";
        public const string Bar = "bar";

        // Flow / volume
        public const string CubicMeter = "m³";
        public const string CubicMeterPerHour = "m³/h";
        public const string Liter = "l";
        public const string LiterPerSecond = "l/s";
        public const string LiterPerHour = "l/h";

        // Mechanical / misc
        public const string MeterPerSecond = "m/s";
        public const string KilometerPerHour = "km/h";
        public const string Meter = "m";
        public const string Millimeter = "mm";
        public const string Kilogram = "kg";
        public const string Gram = "g";
        public const string Lux = "lx";
        public const string Rpm = "rpm";
        public const string Degree = "°";
        public const string Radian = "rad";
        public const string Newton = "N";

        // Time
        public const string Second = "s";
        public const string Millisecond = "ms";
        public const string Minute = "min";
        public const string Hour = "h";
        public const string Day = "d";

        /// <summary>
        /// Maps spelled-out / alternative unit names (and a few common mis-spellings)
        /// to the canonical symbol. The lookup is whitespace-, case- and separator-
        /// insensitive, so "Degrees Celsius", "degrees-celsius" and "DEGREESCELSIUS"
        /// all resolve to "°C".
        /// </summary>
        private static readonly Dictionary<string, string> _aliases = new()
        {
            // dimensionless
            ["nounits"] = None,
            ["none"] = None,
            ["percent"] = Percent,
            ["percentrelativehumidity"] = RelativeHumidity,
            ["partspermillion"] = PartsPerMillion,
            ["ppm"] = PartsPerMillion,

            // temperature
            ["degreescelsius"] = Celsius,
            ["celsius"] = Celsius,
            ["°c"] = Celsius,
            ["c"] = Celsius,
            ["degreesfahrenheit"] = Fahrenheit,
            ["fahrenheit"] = Fahrenheit,
            ["°f"] = Fahrenheit,
            ["kelvins"] = Kelvin,
            ["kelvin"] = Kelvin,
            ["degreeskelvin"] = Kelvin,
            ["°k"] = Kelvin,
            ["k"] = Kelvin,

            // electrical
            ["volts"] = Volt,
            ["millivolts"] = Millivolt,
            ["amperes"] = Ampere,
            ["amps"] = Ampere,
            ["milliamperes"] = Milliampere,
            ["watts"] = Watt,
            ["kilowatts"] = Kilowatt,
            ["megawatts"] = Megawatt,
            ["voltamperes"] = VoltAmpere,
            ["hertz"] = Hertz,
            ["ohms"] = Ohm,
            ["revolutionsperminute"] = Rpm,

            // energy
            ["watthours"] = WattHour,
            ["kilowatthours"] = KilowattHour,
            ["megawatthours"] = MegawattHour,
            ["gigawatthours"] = GigawattHour,
            ["joules"] = Joule,
            ["kilojoules"] = Kilojoule,
            ["megajoules"] = Megajoule,
            ["btus"] = Btu,

            // pressure
            ["pascals"] = Pascal,
            ["kilopascals"] = Kilopascal,
            ["hectopascals"] = Hectopascal,
            ["millibar"] = Millibar,
            ["bars"] = Bar,

            // flow / volume
            ["cubicmeters"] = CubicMeter,
            ["cubicmetersperhour"] = CubicMeterPerHour,
            ["liters"] = Liter,
            ["litres"] = Liter,
            ["literspersecond"] = LiterPerSecond,
            ["litersperhour"] = LiterPerHour,

            // mechanical / misc
            ["meterspersecond"] = MeterPerSecond,
            ["kilometersperhour"] = KilometerPerHour,
            ["meters"] = Meter,
            ["millimeters"] = Millimeter,
            ["kilograms"] = Kilogram,
            ["grams"] = Gram,
            ["lux"] = Lux,
            ["degrees"] = Degree,
            ["radians"] = Radian,
            ["newtons"] = Newton,

            // time
            ["seconds"] = Second,
            ["milliseconds"] = Millisecond,
            ["minutes"] = Minute,
            ["hours"] = Hour,
            ["days"] = Day,
        };

        /// <summary>
        /// Returns the canonical unit symbol for an arbitrary unit string. Already-canonical
        /// symbols (e.g. "kWh") are returned unchanged. Spelled-out names ("kilowatt hours")
        /// are normalized via the alias table. Unknown values are returned trimmed but as-is
        /// so no information is lost.
        /// </summary>
        public static string Normalize(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return None;

            var trimmed = unit.Trim();

            // Strip whitespace and common separators, lower-case, then look up.
            var key = new string(trimmed.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray())
                          .ToLowerInvariant();

            return _aliases.TryGetValue(key, out var symbol) ? symbol : trimmed;
        }

        /// <summary>
        /// True when the (already canonical or raw) unit represents a cumulative
        /// consumption quantity – energy or volume – that should be delta-tracked.
        /// </summary>
        public static bool IsConsumption(string? unit)
        {
            var u = Normalize(unit).ToLowerInvariant();
            switch (u)
            {
                case "wh":
                case "kwh":
                case "mwh":
                case "gwh":
                case "btu":
                case "j":
                case "kj":
                case "mj":
                case "m³":
                case "m3":
                case "l":
                case "gal":
                case "ft³":
                case "ft3":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the unit represents a temperature (°C, °F, K). Used by UIs that
        /// apply temperature-specific behaviour such as a 0.5° input step.
        /// </summary>
        public static bool IsTemperature(string? unit)
        {
            var u = Normalize(unit);
            return u == Celsius || u == Fahrenheit || u == Kelvin;
        }
    }
}
