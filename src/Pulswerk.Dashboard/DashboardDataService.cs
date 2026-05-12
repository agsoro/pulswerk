using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard
{
    /// <summary>
    /// Shared service holding the runtime state for the monitoring dashboard.
    /// </summary>
    public class DashboardDataService
    {
        public LogBuffer LogBuffer { get; }
        public AppConfig Config { get; }
        public TelemetryStore TsStore { get; }
        public AlarmStore AlarmStore { get; }
        public HashSet<string> OfflineDevices { get; }
        public Dictionary<string, DateTime> LastPolledAtMap { get; }
        public Dictionary<string, object> LatestValues { get; } = new();
        public Dictionary<string, DateTime> LatestTimestamps { get; } = new();
        public Dictionary<string, IDeviceDriver> Drivers { get; }
        public Stopwatch Uptime { get; } = Stopwatch.StartNew();
        public string Version { get; }

        private long _totalUpdates = 0;
        private readonly Queue<(DateTime Time, int Count)> _updateHistory = new();
        private readonly object _statsLock = new();
        private readonly HashSet<string> _bootstrappedKeys = new();

        public DashboardDataService(LogBuffer logBuffer, AppConfig config,
            TelemetryStore tsStore, AlarmStore alarmStore,
            HashSet<string> offlineDevices, Dictionary<string, DateTime> lastPolledAtMap,
            Dictionary<string, IDeviceDriver> drivers)
        {
            LogBuffer = logBuffer;
            Config = config;
            TsStore = tsStore;
            AlarmStore = alarmStore;
            OfflineDevices = offlineDevices;
            LastPolledAtMap = lastPolledAtMap;
            Drivers = drivers;
            Version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            // Calculation engine for consumption (kWh / m³)
            string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            Console.WriteLine($"  [Dashboard] DataService initialized with {Config.Devices.Count} devices.");

            // Initial registration of known keys (Modbus)
            RegisterAllKnownKeys();
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
                        {
                            // Register key for metadata purposes if needed, otherwise just skip
                        }
                    }
                }
            }
        }


        public Dictionary<string, (double val, DateTime ts)> UpdateTelemetry(Dictionary<string, object> values)
        {
            if (values == null) return new Dictionary<string, (double val, DateTime ts)>();

            var persistedResults = new Dictionary<string, (double val, DateTime ts)>();
            DateTime now = DateTime.UtcNow;
            long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

            lock (LatestValues)
            {
                foreach (var kvp in values)
                {
                    LatestValues[kvp.Key] = kvp.Value;
                    LatestTimestamps[kvp.Key] = now;

                    // If it's a numeric value, mark it for persistence
                    if (TryToDouble(kvp.Value, out double d))
                    {
                        persistedResults[kvp.Key] = (d, now);
                    }
                }
            }
            RecordUpdate(values.Count);
            return persistedResults;
        }

        private static bool TryToDouble(object val, out double d)
        {
            d = 0;
            if (val == null) return false;
            try { d = Convert.ToDouble(val); return true; }
            catch { return false; }
        }

        private void RecordUpdate(int count)
        {
            lock (_statsLock)
            {
                _totalUpdates += count;
                _updateHistory.Enqueue((DateTime.UtcNow, count));

                // Keep only last 60 seconds
                var cutoff = DateTime.UtcNow.AddSeconds(-60);
                while (_updateHistory.Count > 0 && _updateHistory.Peek().Time < cutoff)
                    _updateHistory.Dequeue();
            }
        }

        public double GetUpdatesPerMinute()
        {
            lock (_statsLock)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-60);
                while (_updateHistory.Count > 0 && _updateHistory.Peek().Time < cutoff)
                    _updateHistory.Dequeue();

                return _updateHistory.Sum(x => x.Count);
            }
        }

        public void UpdateAttributes(Dictionary<string, string> attrs)
        {
            lock (LatestValues)
            {
                foreach (var kv in attrs)
                    LatestValues[kv.Key] = kv.Value;
            }
        }

        public List<AssetNodeDto> GetAssetTrees()
        {
            var root = new AssetNodeDto { Name = "Root", IsView = true };

            foreach (var device in Config.Devices)
            {
                if (!device.HierarchyEnabled) continue;

                if (Drivers.TryGetValue(device.Name, out var driver))
                {
                    var deviceTree = driver.GetAssetHierarchy(device);
                    if (deviceTree != null)
                    {
                        // Populate live values and consumption points
                        PopulateTree(deviceTree);

                        // Merge into the global hierarchy based on the device's configured path
                        if (device.Path != null && device.Path.Count > 0)
                            MergeDtoIntoTree(root, deviceTree, device.Path);
                        else
                            root.Children.Add(deviceTree);
                    }
                }
            }

            return root.Children;
        }

        private void PopulateTree(AssetNodeDto node)
        {
            // Points at this level
            var originalPoints = node.Points.ToList();
            foreach (var p in originalPoints)
            {
                p.Value = GetLatestValue(p.Key);
                p.LastUpdate = FormatLastUpdate(p.Key);
            }

            // Recurse
            foreach (var child in node.Children)
                PopulateTree(child);
        }

        private void MergeDtoIntoTree(AssetNodeDto parent, AssetNodeDto node, List<string> path)
        {
            if (path == null || path.Count == 0)
            {
                // No path segments remaining, add node to parent (merging if name matches)
                var existing = parent.Children.FirstOrDefault(c => c.Name == node.Name);
                if (existing != null)
                {
                    existing.Points.AddRange(node.Points);
                    foreach (var child in node.Children)
                        MergeDtoIntoTree(existing, child, new List<string>());
                }
                else
                {
                    parent.Children.Add(node);
                }
                return;
            }

            // Find or create the next segment in the tree
            string segment = path[0];
            var nextNode = parent.Children.FirstOrDefault(c => c.Name == segment);
            if (nextNode == null)
            {
                nextNode = new AssetNodeDto
                {
                    Id = AssetNodeDto.PathSegmentId(segment),   // stable, deterministic
                    Name = segment,
                    Type = "Folder",
                    IsView = true
                };
                parent.Children.Add(nextNode);
            }

            var remainingPath = path.Skip(1).ToList();
            if (remainingPath.Count == 0)
            {
                // Last segment of the path. If node name matches segment, merge.
                if (node.Name == segment)
                {
                    nextNode.Points.AddRange(node.Points);
                    foreach (var child in node.Children)
                        MergeDtoIntoTree(nextNode, child, new List<string>());
                }
                else
                {
                    // Add node as child of the last segment
                    MergeDtoIntoTree(nextNode, node, new List<string>());
                }
            }
            else
            {
                MergeDtoIntoTree(nextNode, node, remainingPath);
            }
        }



        private string GetLatestValue(string key)
        {
            lock (LatestValues)
                return LatestValues.TryGetValue(key, out var v) ? v.ToString() ?? "" : "---";
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

        /// <summary>
        /// Returns telemetry history from InfluxDB for a single key.
        /// </summary>
        public async Task<List<TsPoint>> GetTelemetryHistoryAsync(string key, double days)
        {
            Console.WriteLine($"  [Dashboard] History requested: {key}, days={days}");
            long endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000; 
            long startTs = endTs - (long)(days * 24 * 60 * 60 * 1000.0) - 5000;
            return await TsStore.QueryAsync(key, startTs, endTs, limit: 5000);
        }

        public Task<bool> WriteValueAsync(string key, double value)
        {
            var device = IdentifyDeviceFromKey(key);
            if (device == null) return Task.FromResult(false);

            // Extract the technical point key from the scoped key {DeviceId}_{PointKey}
            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) return Task.FromResult(false);

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null || !writer.IsWritable(driverKey)) return Task.FromResult(false);

            try
            {
                writer.Write(conn, device, driverKey, value);
                Console.WriteLine($"  [Dashboard] Manual write success: {key} = {value}");

                // --- Force immediate update in dashboard cache and storage ---
                lock (LatestValues) 
                { 
                    LatestValues[key] = value; 
                    LatestTimestamps[key] = DateTime.UtcNow;
                }

                // Also update InfluxDB immediately so charts show the change
                TsStore.Insert(key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), value);
                TsStore.Flush();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [Dashboard] Write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> WriteComplexValueAsync(string key, object value)
        {
            var device = IdentifyDeviceFromKey(key);
            if (device == null) return Task.FromResult(false);

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) return Task.FromResult(false);

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null || !writer.IsWritable(driverKey)) return Task.FromResult(false);

            try
            {
                writer.WriteComplex(conn, device, driverKey, value);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [Dashboard] Complex write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Returns all available telemetry keys with metadata for the dashboard widget key picker.
        /// Flattens the asset tree into a list of selectable keys.
        /// </summary>
        public List<AvailableKeyDto> GetAvailableKeys()
        {
            var keys = new List<AvailableKeyDto>();
            var trees = GetAssetTrees();

            void ExtractKeys(List<AssetNodeDto> nodes, string pathPrefix)
            {
                foreach (var node in nodes)
                {
                    string currentPath = string.IsNullOrEmpty(pathPrefix)
                        ? node.Name
                        : $"{pathPrefix} › {node.Name}";

                    foreach (var point in node.Points)
                    {
                        var dev = IdentifyDeviceFromKey(point.Key);
                        keys.Add(new AvailableKeyDto
                        {
                            Key = point.Key,
                            Name = point.Name,
                            FullName = point.FullName,
                            Units = point.Units,
                            Type = point.Type,
                            Path = currentPath,
                            Value = point.Value,
                            LastUpdate = FormatLastUpdate(point.Key),
                            ParentId = point.ParentId,
                            ParentPath = point.ParentPath,
                            Device = dev?.Name ?? "System",
                            Connection = dev?.ConnectionId ?? "-",
                            IsWritable = point.IsWritable,
                            EnumValues = point.EnumValues
                        });
                    }

                    if (node.Children.Count > 0)
                        ExtractKeys(node.Children, currentPath);
                }
            }

            ExtractKeys(trees, "");
            return keys;
        }

        /// <summary>
        /// Fetches telemetry history for multiple keys within a time range.
        /// Used by dashboard widgets with the global timewindow.
        /// </summary>
        public async Task<Dictionary<string, List<TsPoint>>> GetTelemetryHistoryForWidgetAsync(
            List<string> telemetryKeys, long startTs, long endTs)
        {
            return await TsStore.QueryMultipleAsync(telemetryKeys, startTs, endTs);
        }

        /// <summary>
        /// Gets current values for a list of telemetry keys. Used by latest-values and single-value widgets.
        /// </summary>
        public Dictionary<string, string> GetCurrentValues(List<string> keys)
        {
            var result = new Dictionary<string, string>();
            lock (LatestValues)
            {
                foreach (var key in keys)
                {
                    result[key] = LatestValues.TryGetValue(key, out var val)
                        ? val?.ToString() ?? "---"
                        : "---";
                }
            }
            return result;
        }

        public async Task<List<PropertyDto>> GetPropertiesAsync(string key)
        {
            var device = IdentifyDeviceFromKey(key);
            if (device == null) return new List<PropertyDto>();

            if (Drivers.TryGetValue(device.Name, out var driver))
            {
                var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
                if (conn != null)
                {
                    return await driver.GetExtendedPropertiesAsync(conn, device, key);
                }
            }

            return new List<PropertyDto>();
        }

        private DeviceConfig? IdentifyDeviceFromKey(string key)
        {
            // The key format is {DeviceId}_{PointKey}. 
            // We search for the longest matching DeviceId to handle underscores in IDs correctly.
            return Config.Devices
                .Where(d => key.StartsWith(d.Id + "_"))
                .OrderByDescending(d => d.Id.Length)
                .FirstOrDefault();
        }

        public async Task<HeartbeatStatsDto> GetHeartbeatStatsAsync()
        {
            var dbStats = await TsStore.GetStatsAsync();

            // Get database size (SQLite + InfluxDB)
            long dbSizeBytes = 0;
            try
            {
                // 1. InfluxDB (mounted volume)
                var influxDir = new System.IO.DirectoryInfo("/var/lib/influxdb2");
                if (influxDir.Exists)
                {
                    dbSizeBytes += influxDir.EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length);
                }

                // 2. SQLite files in app directory
                var appDir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
                if (appDir.Exists)
                {
                    dbSizeBytes += appDir.EnumerateFiles("*.db").Sum(f => f.Length);
                }
            }
            catch { }

            // Fallback to InfluxDB internal stats if volume mount isn't yielding results
            if (dbSizeBytes == 0 || dbSizeBytes < 1000) dbSizeBytes = dbStats.DiskSizeBytes;

            return new HeartbeatStatsDto
            {
                UptimeSeconds = (long)Uptime.Elapsed.TotalSeconds,
                Version = Version,
                IsScanning = Drivers.Values.Any(d => d.IsBusy),
                TotalDevices = Config.Devices.Count,
                OnlineDevices = Config.Devices.Count - OfflineDevices.Count,
                TotalTelemetryKeys = dbStats.KeyCount,
                TotalDataPoints = dbStats.PointCount,
                UpdatesPerMinute = GetUpdatesPerMinute(),
                TotalUpdates = _totalUpdates,
                DatabaseSizeBytes = dbSizeBytes
            };
        }

    }

    public class HeartbeatStatsDto
    {
        [JsonPropertyName("uptimeSeconds")] public long UptimeSeconds { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("isScanning")] public bool IsScanning { get; set; }
        [JsonPropertyName("totalDevices")] public int TotalDevices { get; set; }
        [JsonPropertyName("onlineDevices")] public int OnlineDevices { get; set; }
        [JsonPropertyName("totalTelemetryKeys")] public long TotalTelemetryKeys { get; set; }
        [JsonPropertyName("totalDataPoints")] public long TotalDataPoints { get; set; }
        [JsonPropertyName("updatesPerMinute")] public double UpdatesPerMinute { get; set; }
        [JsonPropertyName("totalUpdates")] public long TotalUpdates { get; set; }
        [JsonPropertyName("databaseSizeBytes")] public long DatabaseSizeBytes { get; set; }
    }
}
