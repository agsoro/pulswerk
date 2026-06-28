using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
using Pulswerk.Storage;
using BACnet = System.IO.BACnet;

namespace Pulswerk.Dashboard
{
    /// <summary>
    /// Shared service holding the runtime state for the monitoring dashboard.
    /// Behaviour is split across focused partial-class files in Services/:
    ///   TelemetryService.cs  – UpdateTelemetries, GetAvailableTelemetries, GetCurrentValues, history
    ///   AssetTreeService.cs  – GetAssetTrees and tree-building helpers
    ///   ConsumptionService.cs– GetConsumptionHistoryAsync and gap-fill logic
    ///   HeartbeatService.cs  – GetHeartbeatStatsAsync, health snapshots
    ///   WriteBackService.cs  – WriteValueAsync, WriteComplexValueAsync
    ///   PropertiesService.cs – GetPropertiesAsync, GetUpdatesPerMinute
    /// </summary>
    public partial class DashboardDataService : IDisposable
    {
        // ── Public state ─────────────────────────────────────────────────────
        public LogBuffer LogBuffer { get; }
        private AppConfig _config;
        public AppConfig Config => _config;
        public TelemetryStore DataStore { get; }
        public AlarmStore AlarmStore { get; }
        public ConcurrentDictionary<string, byte> OfflineDevices { get; }
        public ConcurrentDictionary<string, DateTime> LastPolledAtMap { get; }
        public Dictionary<string, object> LatestValues { get; } = new();
        public Dictionary<string, DateTime> LatestTimestamps { get; } = new();
        public Dictionary<string, IDeviceDriver> Drivers { get; }
        public Stopwatch Uptime { get; } = Stopwatch.StartNew();
        public string Version { get; }

        // ── Stats / rate tracking ────────────────────────────────────────────
        private long _totalUpdates = 0;
        private long _totalPushUpdates = 0;
        private long _totalPullUpdates = 0;
        private readonly Queue<(DateTime Time, int Count)> _updateHistory = new();
        private readonly Queue<(DateTime Time, int Count)> _pushHistory = new();
        private readonly Queue<(DateTime Time, int Count)> _pullHistory = new();
        private readonly object _statsLock = new();
        private readonly HashSet<string> _bootstrappedKeys = new();

        public event Action<Dictionary<string, string>>? OnTelemetriesUpdated;

        // ── Health history (sampled every 5 min, kept 24h = 288 points) ─────
        private readonly Queue<HealthSnapshot> _healthHistory = new();
        private readonly object _healthLock = new();
        private System.Threading.Timer? _healthTimer;

        // ── Virtual telemetry tracking ───────────────────────────────────────
        private readonly List<(string Key, string Formula, string? Units, DeviceConfig Device)> _virtualTelemetries = new();

        // ── Calculation engine ───────────────────────────────────────────────
        private readonly CalculationEngine _calc;

        // ── Telemetry metadata cache ─────────────────────────────────────────
        private List<AvailableTelemetryDto>? _cachedTelemetries;
        private DateTime _cacheTimestamp = DateTime.MinValue;
        private readonly object _cacheLock = new();

        // ── Constructor ──────────────────────────────────────────────────────

        public DashboardDataService(LogBuffer logBuffer, AppConfig config,
            TelemetryStore dataStore, AlarmStore alarmStore,
            ConcurrentDictionary<string, byte> offlineDevices,
            ConcurrentDictionary<string, DateTime> lastPolledAtMap,
            Dictionary<string, IDeviceDriver> drivers)
        {
            LogBuffer = logBuffer;
            _config = config;
            DataStore = dataStore;
            AlarmStore = alarmStore;
            OfflineDevices = offlineDevices;
            LastPolledAtMap = lastPolledAtMap;
            Drivers = drivers;
            Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            // Calculation engine for consumption (kWh / m³)
            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
            _calc = new CalculationEngine(dataDir);

            Log.Info($"[Dashboard] DataService initialized with {Config.Devices.Count} devices.");

            // Register known driver keys in the calculation engine
            RegisterAllKnownKeys();

            // Track all virtual telemetries for live push evaluation
            if (Config.Devices != null)
            {
                foreach (var device in Config.Devices)
                {
                    if (device.DeviceType == "virtual" && device.Telemetries != null)
                    {
                        foreach (var dp in device.Telemetries)
                        {
                            if (!string.IsNullOrWhiteSpace(dp.Formula))
                            {
                                string key = $"{device.Id}_{dp.Id}";
                                _virtualTelemetries.Add((key, dp.Formula, dp.Units, device));
                            }
                        }
                    }
                }
            }

            // Start unified health sampling (every 5 min, first sample after 10 s)
            _healthTimer = new System.Threading.Timer(_ => SampleHealthSnapshot(), null, 10_000, 5 * 60_000);
        }

        private bool _disposed;

        /// <summary>
        /// Disposes the health-sampling timer. The timer holds a rooted callback delegate
        /// closing over this service, so without disposal the whole service graph (stores,
        /// config, queues) is kept alive and the timer keeps firing.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _healthTimer?.Dispose(); } catch { }
            _healthTimer = null;
        }

        // ── Config helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Replaces the Modules portion of the in-memory config so module toggles
        /// take effect immediately without a restart.
        /// </summary>
        public void UpdateModules(ModulesConfig? modules)
        {
            _config = new AppConfig(
                _config.InfluxDb,
                _config.Database,
                _config.Polling,
                _config.Connections,
                _config.Devices,
                _config.Server,
                modules
            );
        }

        // ── Shared helpers used across partials ──────────────────────────────

        public void UpdateAttributes(Dictionary<string, string> attrs)
        {
            lock (LatestValues)
            {
                foreach (var kv in attrs)
                    LatestValues[kv.Key] = kv.Value;
            }
        }

        private DeviceConfig? IdentifyDeviceFromTelemetryKey(string key)
        {
            // Key format is {DeviceId}_{PointKey}.
            // Search for the longest matching DeviceId to handle underscores in IDs.
            return Config.Devices
                .Where(d => key.StartsWith(d.Id + "_"))
                .OrderByDescending(d => d.Id.Length)
                .FirstOrDefault();
        }

        private string ExpandFormula(string formula, DeviceConfig? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(formula)) return formula;
            if (formula.Contains("pathsum(", StringComparison.OrdinalIgnoreCase)) return formula;

            return System.Text.RegularExpressions.Regex.Replace(formula, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
            {
                string token = match.Value;
                if (token.Equals("consumption", StringComparison.OrdinalIgnoreCase)) return token;
                if (token.Equals("pathsum", StringComparison.OrdinalIgnoreCase)) return token;

                string baseToken = token.Contains(":") ? token.Split(':')[0] : token;
                if (Config.Devices.Any(d => baseToken.StartsWith(d.Id + "_")))
                    return token;

                return $"{device.Id}_{token}";
            });
        }

        private string GetLatestValue(string key)
        {
            lock (LatestValues)
                return LatestValues.TryGetValue(key, out var v) ? v?.ToString() ?? "0" : "---";
        }

        private string FormatLastUpdate(string key)
        {
            lock (LatestValues)
            {
                if (LatestTimestamps.TryGetValue(key, out var ts))
                {
                    var diff = DateTime.UtcNow - ts;
                    if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}s ago";
                    if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
                    return ts.ToLocalTime().ToString("HH:mm:ss");
                }
                return "-";
            }
        }

        private static bool TryToDouble(object val, out double d)
        {
            d = 0;
            if (val == null) return false;
            try { d = Convert.ToDouble(val); return true; }
            catch { return false; }
        }

        private void RegisterAllKnownKeys()
        {
            foreach (var device in Config.Devices)
            {
                if (Drivers.TryGetValue(device.Name, out var driver))
                {
                    var keys = driver.GetTelemetryKeys();
                    var units = driver.GetTelemetryUnits();
                    foreach (var k in keys)
                    {
                        string pointKey = $"{device.Id}_{k}";
                        if (units.TryGetValue(k, out var u))
                            _calc.RegisterKey(pointKey, u);
                    }
                }
            }
        }
    }
}
