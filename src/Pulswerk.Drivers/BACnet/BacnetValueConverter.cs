// BacnetValueConverter.cs – Bidirectional BACnet value conversion
//
//  Read path:   raw BACnet value  →  typed display value
//               (int for binary, double for analog, string for StateText,
//                structured DTO for schedules)
//
//  Write path:  numeric input     →  BacnetValue with correct application tag
//
//  All telemetry paths (RPM, COV, COV-fallback) route through FormatValue
//  so that value types are always consistent for a given object type.

using System;
using System.Collections.Generic;
using System.IO.BACnet;
using System.Linq;

namespace Pulswerk.Drivers.BACnet
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Schedule DTOs – JSON-serializable structured schedule representation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>A single time-value pair within a daily schedule.</summary>
    public record ScheduleEntry(string Time, object? Value);

    /// <summary>One day's schedule (Mon–Sun).</summary>
    public record DaySchedule(string Day, List<ScheduleEntry> Entries);

    /// <summary>A full weekly schedule as a list of <see cref="DaySchedule"/>s.</summary>
    public record WeeklySchedule(List<DaySchedule> Days);

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Central, stateless value converter for BACnet data points.
    /// Every telemetry code path in <see cref="BacnetDriver"/> delegates to this
    /// class so that type semantics (binary → int, analog → double, schedule → DTO)
    /// are enforced in exactly one place.
    /// </summary>
    public static class BacnetValueConverter
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Type classification
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true for object types whose present value is an enumerated
        /// binary state (0 = inactive, 1 = active).  Calendar objects also use
        /// a boolean-like present value.
        /// </summary>
        public static bool IsBinary(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_BINARY_INPUT or
            BacnetObjectTypes.OBJECT_BINARY_OUTPUT or
            BacnetObjectTypes.OBJECT_BINARY_VALUE or
            BacnetObjectTypes.OBJECT_CALENDAR;

        /// <summary>
        /// Returns true for multi-state object types whose present value is an
        /// enumerated integer (1-based index into StateText).
        /// </summary>
        public static bool IsMultiState(BacnetObjectTypes t) => t is
            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT or
            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT or
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;

        /// <summary>
        /// Returns true for any object type whose present value is an integer
        /// (binary 0/1 or multistate 1..N).  These never use floating-point.
        /// </summary>
        public static bool IsEnumerated(BacnetObjectTypes t) =>
            IsBinary(t) || IsMultiState(t);

        private static readonly string[] DayNames =
            { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };

        // ─────────────────────────────────────────────────────────────────────
        //  Read path:  raw object → typed display value
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a raw BACnet property value into a typed display value.
        /// <list type="bullet">
        ///   <item>Binary objects → <c>int 0</c> / <c>int 1</c> (or StateText string)</item>
        ///   <item>Analog/integer objects → <c>double</c> rounded to 4 decimals</item>
        ///   <item>StateText-mapped values → the corresponding <c>string</c> label</item>
        ///   <item>Schedule properties → <see cref="WeeklySchedule"/> DTO</item>
        ///   <item>Null / error inputs → a typed default (<c>0</c> for binary, <c>0.0</c> for analog)</item>
        /// </list>
        /// </summary>
        /// <param name="objectType">The BACnet object type (determines binary vs. analog semantics).</param>
        /// <param name="propId">The property being formatted (UNITS, SCHEDULE get special treatment).</param>
        /// <param name="raw">The raw value from the BACnet transport layer. May be null for missing/error values.</param>
        /// <param name="stateText">Optional state text list from the object's PROP_STATE_TEXT.</param>
        public static object FormatValue(
            BacnetObjectTypes objectType,
            BacnetPropertyIds propId,
            object? raw,
            IReadOnlyList<string>? stateText = null)
        {
            bool enumerated = IsEnumerated(objectType);

            // ── Null / error guard ──────────────────────────────────────────
            if (raw == null)
                return DefaultValue(objectType);

            var rawStr = raw.ToString();
            if (rawStr != null && rawStr.Contains("ERROR_"))
                return DefaultValue(objectType);

            // ── Unit pass-through ───────────────────────────────────────────
            if (propId == BacnetPropertyIds.PROP_UNITS)
                return UnitMapper.Format(raw);

            // ── Schedule properties → structured DTO ────────────────────────
            if (propId == BacnetPropertyIds.PROP_WEEKLY_SCHEDULE ||
                propId == BacnetPropertyIds.PROP_EXCEPTION_SCHEDULE)
                return FormatSchedule(raw);

            // ── StateText lookup (binary 0-based, multistate 1-based) ──────
            if (stateText != null && stateText.Count > 0 && TryToDouble(raw, out double d))
            {
                int val = (int)d;
                int idx = IsBinary(objectType) ? val : val - 1;
                if (idx >= 0 && idx < stateText.Count)
                    return stateText[idx];
            }

            // ── BacnetBitString → int bitmask ─────────────────────────────────
            if (raw is BacnetBitString bs)
            {
                int bits = 0;
                for (int i = 0; i < bs.bits_used; i++)
                    if (bs.GetBit((byte)i)) bits += 1 << i;
                return bits;
            }

            // ── Numeric conversion ──────────────────────────────────────────
            if (TryToDouble(raw, out double d2))
                return enumerated ? (object)(int)d2 : Math.Round(d2, 4);

            return rawStr ?? "";
        }

        /// <summary>
        /// Overload that accepts a <see cref="BacnetObjectInfo"/> directly for
        /// call-site convenience in the driver.
        /// </summary>
        public static object FormatValue(
            BacnetObjectInfo obj,
            BacnetPropertyIds propId,
            object? raw)
        {
            return FormatValue(obj.ObjectId.type, propId, raw, obj.StateText);
        }

        /// <summary>
        /// Returns the typed default value for an object type:
        /// <c>0</c> (int) for enumerated types (binary + multistate),
        /// <c>0.0</c> (double) for everything else.
        /// </summary>
        public static object DefaultValue(bool enumerated) =>
            enumerated ? 0 : (object)0.0;

        /// <summary>
        /// Returns the typed default value for a given object type.
        /// </summary>
        public static object DefaultValue(BacnetObjectTypes objectType) =>
            DefaultValue(IsEnumerated(objectType));

        // ─────────────────────────────────────────────────────────────────────
        //  Write path:  display / UI value → internal numeric → BacnetValue
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a value coming from the UI (which may be a state-text label
        /// like "Normal" or a raw index like 0) back to the internal numeric value
        /// that BACnet expects. This is the reverse of FormatValue's state-text
        /// lookup.
        /// </summary>
        public static double FromDisplayValue(
            BacnetObjectTypes objectType,
            object? displayValue,
            IReadOnlyList<string>? stateText = null)
        {
            if (displayValue == null) return 0;

            // If the display value is already numeric, pass through
            if (TryToDouble(displayValue, out double d))
                return d;

            // Reverse state-text lookup: find index of the label
            var label = displayValue.ToString()?.Trim() ?? "";
            if (stateText != null && stateText.Count > 0 && label.Length > 0)
            {
                for (int i = 0; i < stateText.Count; i++)
                {
                    if (string.Equals(stateText[i], label, StringComparison.OrdinalIgnoreCase))
                    {
                        // Binary is 0-based, multistate is 1-based
                        return IsBinary(objectType) ? i : i + 1;
                    }
                }
            }

            // Fallback: try parse as number
            if (double.TryParse(label, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                return parsed;

            return 0;
        }

        /// <summary>
        /// Overload accepting a <see cref="BacnetObjectInfo"/> for call-site convenience.
        /// </summary>
        public static double FromDisplayValue(BacnetObjectInfo obj, object? displayValue)
            => FromDisplayValue(obj.ObjectId.type, displayValue, obj.StateText);

        /// <summary>
        /// Constructs a <see cref="BacnetValue"/> suitable for WriteProperty,
        /// using the correct application tag for the object type.
        /// Binary/multistate produce ENUMERATED (uint), all others produce REAL.
        /// </summary>
        public static BacnetValue ToWriteValue(BacnetObjectTypes objectType, double value)
        {
            if (IsEnumerated(objectType))
                return new BacnetValue(
                    BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED,
                    (uint)(IsBinary(objectType) ? (value != 0 ? 1 : 0) : Math.Round(value)));

            return new BacnetValue(
                BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL,
                (float)value);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Schedule conversion
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Converts a raw BACnet weekly/exception schedule into a structured
        /// <see cref="WeeklySchedule"/> DTO that serialises cleanly to JSON.
        /// </summary>
        public static object FormatSchedule(object? raw)
        {
            if (raw == null)
                return new WeeklySchedule(new List<DaySchedule>());

            if (raw is not System.Collections.IEnumerable list)
                return raw.ToString() ?? "";

            var days = new List<DaySchedule>();
            int dayIdx = 0;

            foreach (var day in list)
            {
                var entries = new List<ScheduleEntry>();
                object? dayVal = day;
                if (day is BacnetValue bv) dayVal = bv.Value;

                if (dayVal is System.Collections.IEnumerable timeList)
                {
                    var enumValues = timeList.Cast<BacnetValue>().ToList();
                    for (int i = 0; i < enumValues.Count - 1; i += 2)
                    {
                        var tVal = enumValues[i].Value;
                        var vVal = enumValues[i + 1].Value;

                        string timeStr = tVal is DateTime dt
                            ? dt.ToString("HH:mm")
                            : tVal?.ToString() ?? "00:00";

                        // For the value, preserve numeric types rather than stringifying
                        object? entryValue = vVal;
                        if (TryToDouble(vVal, out double dv))
                            entryValue = dv;

                        entries.Add(new ScheduleEntry(timeStr, entryValue));
                    }
                }

                string dayName = dayIdx < DayNames.Length ? DayNames[dayIdx] : $"Day{dayIdx}";
                days.Add(new DaySchedule(dayName, entries));
                dayIdx++;
            }

            return new WeeklySchedule(days);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Numeric parsing helper
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to convert a scalar BACnet value to a double.
        /// Rejects CLR enums and complex types (BacnetBitString, etc.).
        /// Uses <see cref="System.Globalization.CultureInfo.InvariantCulture"/>
        /// so that '.' is always the decimal separator.
        /// </summary>
        public static bool TryToDouble(object? v, out double result)
        {
            if (v is null) { result = 0; return false; }

            // CLR enums and complex BACnet types are metadata — not numeric values.
            if (v.GetType().IsEnum || v is BacnetBitString)
            { result = 0; return false; }

            try { result = Math.Round(Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture), 6); return true; }
            catch { result = 0; return false; }
        }
    }
}
