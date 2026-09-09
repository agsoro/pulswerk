// SmgwParser.cs – HTML and OBIS parser for Smart Meter Gateway responses
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Smgw
{
    public record SmgwMeter(string Mid, string Name);
    public record SmgwFirmwareVersion(string Component, string Version, string Checksum);
    public record SmgwReading(string ObisRaw, string ObisNormalized, double Value, string Unit, DateTime? Timestamp, string? Name = null, string? Status = null, string? Signature = null);

    /// <summary>
    /// Parses HTML responses from the Smart Meter Gateway HAN web interface.
    /// </summary>
    public static class SmgwParser
    {
        private static readonly Regex MeterOptionRegex = new(
            @"<option[^>]*value=[""']([^""']+)[""'][^>]*>(.*?)</option>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex RowRegex = new(
            @"<tr[^>]*>(.*?)</tr>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        // Matches cells by id attribute even when closing tag is malformed (e.g. </td id='...'>)
        private static readonly Regex IdCellRegex = new(
            @"<td\s+id=['""]([^'""]+)['""][^>]*>(.*?)(?:</td[^>]*>|(?=<td|$))",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex GenericCellRegex = new(
            @"<t[dh][^>]*>(.*?)</t[dh][^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>
        /// Parses connected meters from the 'meterform' HTML response.
        /// </summary>
        public static List<SmgwMeter> ParseMeters(string html)
        {
            var meters = new List<SmgwMeter>();
            if (string.IsNullOrWhiteSpace(html)) return meters;

            // Scope to select element if present
            string searchScope = html;
            int selectStart = html.IndexOf("meterform_select_meter", StringComparison.OrdinalIgnoreCase);
            if (selectStart >= 0)
            {
                int tagClose = html.IndexOf("</select>", selectStart, StringComparison.OrdinalIgnoreCase);
                if (tagClose > selectStart)
                {
                    searchScope = html.Substring(selectStart, tagClose - selectStart + 9);
                }
            }

            var matches = MeterOptionRegex.Matches(searchScope);
            foreach (Match match in matches)
            {
                string mid = match.Groups[1].Value.Trim();
                string name = StripHtml(match.Groups[2].Value).Trim();
                if (!string.IsNullOrEmpty(mid))
                {
                    meters.Add(new SmgwMeter(mid, name));
                }
            }

            return meters;
        }

        /// <summary>
        /// Parses OBIS readings from 'showMeterProfile' HTML table into standard Pulswerk telemetry values.
        /// </summary>
        public static Dictionary<string, object> ParseMeterReadings(string html)
        {
            var telemetries = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var readings = ParseDetailedReadings(html);

            foreach (var r in readings)
            {
                string? key = MapObisToTelemetryKey(r.ObisNormalized);
                if (key == null) continue;

                double normalizedValue = NormalizeValue(key, r.Value, r.Unit);
                telemetries[key] = normalizedValue;
            }

            return telemetries;
        }

        /// <summary>
        /// Parses detailed OBIS readings from 'showMeterProfile' HTML table.
        /// </summary>
        public static List<SmgwReading> ParseDetailedReadings(string html)
        {
            var readings = new List<SmgwReading>();
            if (string.IsNullOrWhiteSpace(html)) return readings;

            // Scope to table with id 'metervalue' if present (scoping to footer to avoid cutting off at inner signature tables)
            string searchScope = html;
            int tableStart = html.IndexOf("id=\"metervalue\"", StringComparison.OrdinalIgnoreCase);
            if (tableStart < 0) tableStart = html.IndexOf("id='metervalue'", StringComparison.OrdinalIgnoreCase);
            if (tableStart >= 0)
            {
                int footerStart = html.IndexOf("id=\"footer\"", tableStart, StringComparison.OrdinalIgnoreCase);
                if (footerStart < 0) footerStart = html.IndexOf("id='footer'", tableStart, StringComparison.OrdinalIgnoreCase);
                searchScope = footerStart > tableStart ? html.Substring(tableStart, footerStart - tableStart) : html.Substring(tableStart);
            }

            var rows = RowRegex.Matches(searchScope);
            foreach (Match row in rows)
            {
                string rowContent = row.Groups[1].Value;

                string? timestampStr = null;
                string? obisStr = null;
                string? valueStr = null;
                string? unitStr = null;
                string? statusStr = null;
                string? nameStr = null;
                string? signatureStr = null;

                // 1. Try cell matching by id attribute
                var idCells = IdCellRegex.Matches(rowContent);
                if (idCells.Count > 0)
                {
                    foreach (Match cell in idCells)
                    {
                        string id = cell.Groups[1].Value.ToLowerInvariant();
                        string text = StripHtml(cell.Groups[2].Value).Trim();

                        if (id.Contains("timestamp")) timestampStr = text;
                        else if (id.Contains("obis")) obisStr = text;
                        else if (id.Contains("einheit") || id.Contains("unit")) unitStr = text;
                        else if (id.Contains("status")) statusStr = text;
                        else if (id.Contains("name")) nameStr = text;
                        else if (id.Contains("sign")) signatureStr = text;
                        else if (id.Contains("wert") || id.Contains("col_val") || id.EndsWith("value") || id.Contains("_value")) valueStr = text;
                    }
                }

                // 2. Fallback to generic td/th indexing if id matching didn't yield obis or value
                if (string.IsNullOrWhiteSpace(obisStr) || string.IsNullOrWhiteSpace(valueStr))
                {
                    var genericCells = GenericCellRegex.Matches(rowContent);
                    for (int i = 0; i < genericCells.Count; i++)
                    {
                        string text = StripHtml(genericCells[i].Groups[1].Value).Trim();
                        if (i == 0 && string.IsNullOrEmpty(timestampStr)) timestampStr = text;
                        else if (i == 1 && string.IsNullOrEmpty(statusStr)) statusStr = text;
                        else if (i == 2 && string.IsNullOrEmpty(valueStr)) valueStr = text;
                        else if (i == 3 && string.IsNullOrEmpty(unitStr)) unitStr = text;
                        else if (i == 5 && string.IsNullOrEmpty(nameStr)) nameStr = text;
                        else if (i == 6 && string.IsNullOrEmpty(obisStr)) obisStr = text;
                        else if (i == 7 && string.IsNullOrEmpty(signatureStr)) signatureStr = text;
                    }
                }

                if (string.IsNullOrWhiteSpace(obisStr) || string.IsNullOrWhiteSpace(valueStr))
                    continue;

                if (!TryParseDouble(valueStr, out double numericVal))
                    continue;

                DateTime? timestamp = null;
                if (!string.IsNullOrWhiteSpace(timestampStr) &&
                    DateTime.TryParse(timestampStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                {
                    timestamp = dt;
                }

                string normalizedObis = NormalizeObis(obisStr);
                readings.Add(new SmgwReading(
                    obisStr,
                    normalizedObis,
                    numericVal,
                    unitStr ?? "",
                    timestamp,
                    nameStr,
                    statusStr,
                    signatureStr
                ));
            }

            return readings;
        }

        /// <summary>
        /// Parses firmware versions from 'swversions' HTML table.
        /// </summary>
        public static List<SmgwFirmwareVersion> ParseFirmwareVersions(string html)
        {
            var versions = new List<SmgwFirmwareVersion>();
            if (string.IsNullOrWhiteSpace(html)) return versions;

            var rows = RowRegex.Matches(html);
            foreach (Match row in rows)
            {
                var cells = GenericCellRegex.Matches(row.Groups[1].Value);
                if (cells.Count >= 2)
                {
                    string component = StripHtml(cells[0].Groups[1].Value).Trim();
                    string version = StripHtml(cells[1].Groups[1].Value).Trim();
                    string checksum = cells.Count >= 3 ? StripHtml(cells[2].Groups[1].Value).Trim() : "";
                    if (!string.IsNullOrEmpty(component) && !component.Equals("component", StringComparison.OrdinalIgnoreCase) && !component.Equals("komponente", StringComparison.OrdinalIgnoreCase))
                    {
                        versions.Add(new SmgwFirmwareVersion(component, version, checksum));
                    }
                }
            }

            return versions;
        }

        /// <summary>
        /// Parses meter metadata table from 'showMeterProfile' HTML.
        /// </summary>
        public static Dictionary<string, string> ParseMeterMetadata(string html)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return meta;

            var rows = RowRegex.Matches(html);
            foreach (Match row in rows)
            {
                var cells = GenericCellRegex.Matches(row.Groups[1].Value);
                if (cells.Count == 2)
                {
                    string key = StripHtml(cells[0].Groups[1].Value).Trim();
                    string val = StripHtml(cells[1].Groups[1].Value).Trim();
                    if (!string.IsNullOrEmpty(key) && !key.Equals("zeitstempel", StringComparison.OrdinalIgnoreCase))
                    {
                        meta[key] = val;
                    }
                }
            }

            return meta;
        }

        /// <summary>
        /// Normalizes raw OBIS string into canonical representation (e.g. '1-0:1.8.0*255' -> '1.8.0').
        /// </summary>
        public static string NormalizeObis(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string s = raw.Trim();
            // Remove '1-0:' prefix if present
            if (s.StartsWith("1-0:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);
            else if (s.StartsWith("1-1:", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(4);

            // Remove '*255' suffix if present
            int starIdx = s.IndexOf('*');
            if (starIdx > 0)
                s = s.Substring(0, starIdx);

            return s.Trim();
        }

        /// <summary>
        /// Maps canonical OBIS codes to Pulswerk standard telemetry keys.
        /// </summary>
        public static string? MapObisToTelemetryKey(string obis)
        {
            return obis switch
            {
                // Active import / export energy (totals)
                "1.8.0" => TelemetryKeys.EnergyImportKwh,
                "2.8.0" => TelemetryKeys.EnergyExportKwh,

                // Instantaneous total active power
                "16.7.0" => TelemetryKeys.PowerKw,

                // Per-phase active power
                "36.7.0" => "power_l1",
                "56.7.0" => "power_l2",
                "76.7.0" => "power_l3",

                // Per-phase voltage
                "32.7.0" => "voltage_l1",
                "52.7.0" => "voltage_l2",
                "72.7.0" => "voltage_l3",

                // Per-phase current
                "31.7.0" => "current_l1",
                "51.7.0" => "current_l2",
                "71.7.0" => "current_l3",

                // Grid frequency
                "14.7.0" => "frequency",

                _ => null
            };
        }

        /// <summary>
        /// Normalizes values to Pulswerk standard units (kW for power, kWh for energy).
        /// </summary>
        private static double NormalizeValue(string key, double value, string unit)
        {
            string u = unit.Trim().ToLowerInvariant();

            if (key == TelemetryKeys.PowerKw || key.StartsWith("power_"))
            {
                if (u == "w" || u == "va" || u == "var") return value / 1000.0;
                if (u == "mw") return value * 1000.0;
                return value; // already in kW
            }

            if (key == TelemetryKeys.EnergyImportKwh || key == TelemetryKeys.EnergyExportKwh)
            {
                if (u == "wh" || u == "vah") return value / 1000.0;
                if (u == "mwh") return value * 1000.0;
                return value; // already in kWh
            }

            return value;
        }

        private static bool TryParseDouble(string s, out double result)
        {
            // Normalize German decimal comma to point if needed
            string clean = s.Trim().Replace(" ", "");
            if (clean.Contains(',') && !clean.Contains('.'))
                clean = clean.Replace(',', '.');

            return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string StripHtml(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return Regex.Replace(input, "<.*?>", string.Empty);
        }
    }
}
