using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulswerk.Storage
{
    /// <summary>
    /// Processes raw accumulator telemetry (kWh, m³) and generates delta-based
    /// consumption values for hourly, daily, and monthly intervals.
    /// Supports bootstrapping from InfluxDB to recover 'lost track' periods.
    /// </summary>
    public class CalculationEngine
    {
        private class State
        {
            [JsonPropertyName("hourBase")] public Dictionary<string, double> HourBase { get; set; } = new();
            [JsonPropertyName("dayBase")] public Dictionary<string, double> DayBase { get; set; } = new();
            [JsonPropertyName("monthBase")] public Dictionary<string, double> MonthBase { get; set; } = new();
            [JsonPropertyName("yearBase")] public Dictionary<string, double> YearBase { get; set; } = new();

            [JsonPropertyName("lastHour")] public Dictionary<string, int> LastHour { get; set; } = new();
            [JsonPropertyName("lastDay")] public Dictionary<string, int> LastDay { get; set; } = new();
            [JsonPropertyName("lastMonth")] public Dictionary<string, int> LastMonth { get; set; } = new();
            [JsonPropertyName("lastYear")] public Dictionary<string, int> LastYear { get; set; } = new();
        }

        private readonly string _statePath;
        private readonly State _state = new();
        private readonly HashSet<string> _trackedKeys = new();
        private readonly object _lock = new();

        public CalculationEngine(string dataDir)
        {
            _statePath = Path.Combine(dataDir, "consumption_state.json");
            Load();
        }

        /// <summary>
        /// Registers a telemetry key for consumption tracking if its units match.
        /// </summary>
        public void RegisterKey(string key, string units)
        {
            if (string.IsNullOrEmpty(units)) return;

            string u = units.ToLowerInvariant().Trim();
            if (u == "kwh" || u == "wh" || u == "mwh" || u == "m³" || u == "m3" || u == "cubic meters" || u == "cubic-meters" || u == "l" || u == "liters")
            {
                lock (_lock)
                {
                    _trackedKeys.Add(key);
                }
            }
        }

        /// <summary>
        /// Seeds the base values for a key. Useful for recovery/backfilling.
        /// </summary>
        public void SeedState(string key, double hourBase, double dayBase, double monthBase)
        {
            lock (_lock)
            {
                _state.HourBase[key] = hourBase;
                _state.DayBase[key] = dayBase;
                _state.MonthBase[key] = monthBase;
                _state.YearBase[key] = monthBase; // Seed year with month base if not provided
                Pulswerk.Core.Log.Debug($"[CalcEngine] Seeded '{key}': H={hourBase}, D={dayBase}, M={monthBase}");

                var now = DateTime.UtcNow;
                _state.LastHour[key] = now.Hour;
                _state.LastDay[key] = now.Day;
                _state.LastMonth[key] = now.Month;
                _state.LastYear[key] = now.Year;
                Save();
            }
        }

        /// <summary>
        /// Processes a raw value. 
        /// Returns 'live' deltas (always updated) and 'persisted' deltas (only on rollover).
        /// </summary>
        public (Dictionary<string, object> Live, Dictionary<string, (double val, DateTime ts)> Persisted) Process(string key, object rawValue, DateTime? timestamp = null)
        {
            var live = new Dictionary<string, object>();
            var persisted = new Dictionary<string, (double val, DateTime ts)>();

            if (!TryToDouble(rawValue, out double value)) return (live, persisted);

            lock (_lock)
            {
                if (!_trackedKeys.Contains(key)) return (live, persisted);

                var now = timestamp ?? DateTime.UtcNow;

                // Initialize if not present
                if (!_state.LastHour.ContainsKey(key))
                {
                    _state.LastHour[key] = now.Hour;
                    _state.LastDay[key] = now.Day;
                    _state.LastMonth[key] = now.Month;
                    _state.LastYear[key] = now.Year;
                    _state.HourBase[key] = value;
                    _state.DayBase[key] = value;
                    _state.MonthBase[key] = value;
                    _state.YearBase[key] = value;
                    Save();
                    return (live, persisted);
                }

                // Check for rollover (hour, day, month)
                if (now.Hour != _state.LastHour[key])
                {
                    double delta = Math.Round(Math.Max(0, value - _state.HourBase[key]), 4);
                    // Point at the top of the hour for the previous period
                    var ts = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
                    persisted[$"{key}_hourly"] = (delta, ts);

                    _state.LastHour[key] = now.Hour;
                    _state.HourBase[key] = value;
                }

                if (now.Day != _state.LastDay[key])
                {
                    double delta = Math.Round(Math.Max(0, value - _state.DayBase[key]), 4);
                    var ts = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
                    persisted[$"{key}_daily"] = (delta, ts);

                    _state.LastDay[key] = now.Day;
                    _state.DayBase[key] = value;
                }

                if (now.Month != _state.LastMonth[key])
                {
                    double delta = Math.Round(Math.Max(0, value - _state.MonthBase[key]), 4);
                    var ts = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    persisted[$"{key}_monthly"] = (delta, ts);

                    _state.LastMonth[key] = now.Month;
                    _state.MonthBase[key] = value;
                }

                if (!_state.LastYear.ContainsKey(key) || now.Year != _state.LastYear[key])
                {
                    double delta = Math.Round(Math.Max(0, value - _state.YearBase[key]), 4);
                    var ts = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    persisted[$"{key}_yearly"] = (delta, ts);

                    _state.LastYear[key] = now.Year;
                    _state.YearBase[key] = value;
                }

                if (persisted.Count > 0) Save();

                // Live values (always current delta)
                live[$"{key}_hourly"] = Math.Round(Math.Max(0, value - _state.HourBase[key]), 4);
                live[$"{key}_daily"] = Math.Round(Math.Max(0, value - _state.DayBase[key]), 4);
                live[$"{key}_monthly"] = Math.Round(Math.Max(0, value - _state.MonthBase[key]), 4);
                live[$"{key}_yearly"] = Math.Round(Math.Max(0, value - _state.YearBase[key]), 4);
            }

            return (live, persisted);
        }

        private static bool TryToDouble(object? v, out double result)
        {
            result = 0;
            if (v is double d) { result = d; return true; }
            if (v is float f) { result = f; return true; }
            if (v is int i) { result = i; return true; }
            if (v is long l) { result = l; return true; }
            if (v is string s && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
            {
                result = parsed;
                return true;
            }
            return false;
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_statePath))
                {
                    var json = File.ReadAllText(_statePath);
                    var loaded = JsonSerializer.Deserialize<State>(json);
                    if (loaded != null)
                    {
                        foreach (var kv in loaded.HourBase) _state.HourBase[kv.Key] = kv.Value;
                        foreach (var kv in loaded.DayBase) _state.DayBase[kv.Key] = kv.Value;
                        foreach (var kv in loaded.MonthBase) _state.MonthBase[kv.Key] = kv.Value;
                        foreach (var kv in loaded.LastHour) _state.LastHour[kv.Key] = kv.Value;
                        foreach (var kv in loaded.LastDay) _state.LastDay[kv.Key] = kv.Value;
                        foreach (var kv in loaded.LastMonth) _state.LastMonth[kv.Key] = kv.Value;
                        foreach (var kv in loaded.LastYear) _state.LastYear[kv.Key] = kv.Value;
                        foreach (var kv in loaded.YearBase) _state.YearBase[kv.Key] = kv.Value;
                    }
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_statePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_statePath, json);
            }
            catch { }
        }
    }
}
