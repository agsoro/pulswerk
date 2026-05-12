using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.BACnet;
using System.IO.BACnet.Storage;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
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
        public Dictionary<string, IDeviceDriver> Drivers { get; }
        public Stopwatch Uptime { get; } = Stopwatch.StartNew();
        public string Version { get; }
        public CalculationEngine CalcEngine { get; }

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
            CalcEngine = new CalculationEngine(dataDir);

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
                        string pointKey = device.DeviceType.Equals("modbus", StringComparison.OrdinalIgnoreCase)
                            ? $"{device.Name}_{k}"
                            : k; // BACnet keys are already scoped or discovered
                        if (units.TryGetValue(k, out var u))
                        {
                            CalcEngine.RegisterKey(pointKey, u);
                            _ = BootstrapKeyAsync(pointKey);
                        }
                    }
                }
            }
        }

        private async Task BootstrapKeyAsync(string key)
        {
            if (_bootstrappedKeys.Contains(key)) return;
            _bootstrappedKeys.Add(key);

            // Fetch last 24h to seed the engine
            var history = await GetTelemetryHistoryAsync(key, 1);
            if (history != null && history.Count > 0)
            {
                foreach (var p in history)
                {
                    if (p.Value.HasValue)
                        CalcEngine.Process(key, p.Value.Value, DateTimeOffset.FromUnixTimeMilliseconds(p.Ts).UtcDateTime);
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

                    // If it's a numeric value, mark it for persistence and push to CalcEngine
                    if (TryToDouble(kvp.Value, out double d))
                    {
                        persistedResults[kvp.Key] = (d, now);

                        // Update CalcEngine and get results (hourly/daily consumption)
                        var calcRes = CalcEngine.Process(kvp.Key, d, now);
                        foreach (var c in calcRes.Live)
                        {
                            LatestValues[c.Key] = c.Value;
                        }
                        foreach (var c in calcRes.Persisted)
                        {
                            persistedResults[c.Key] = c.Value;
                        }
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
                bool hasHierarchy = device.HierarchyEnabled;
                bool isBacnet = device.DeviceType.Equals("bacnet", StringComparison.OrdinalIgnoreCase)
                             || device.DeviceType.Equals("deziko", StringComparison.OrdinalIgnoreCase);

                if (!hasHierarchy) continue;

                if (isBacnet)
                {
                    var bacnetDrv = Drivers.TryGetValue(device.Name, out var drv) ? drv as BacnetDriver : null;
                    if (bacnetDrv == null || device.DeviceId == null) continue;
                    var discovered = bacnetDrv.GetDiscoveredObjects(device.Name);
                    if (discovered == null || discovered.Count == 0) continue;

                    var tree = bacnetDrv.GetDiscoveredTree(device.Name);
                    if (tree == null) continue;

                    var lookup = discovered.ToDictionary(o => o.ObjectId, o => o);

                    foreach (var rootNode in tree.Roots)
                    {
                        var dto = ConvertNode(rootNode, device.DeviceId.Value, lookup, new List<PathSegmentDto>());
                        MergeDtoIntoTree(root, dto, rootNode.NamingPath);
                    }
                }
                else if (device.Path != null && device.Path.Count > 0)
                {
                    var reader = Drivers.TryGetValue(device.Name, out var d) ? d : null;
                    if (reader == null) continue;
                    var keys = reader.GetTelemetryKeys();
                    var units = reader.GetTelemetryUnits();

                    // Build the ParentPath the favorites back-link needs: stable IDs matching MergeDtoIntoTree
                    var parentPath = device.Path
                        .Select(seg => new PathSegmentDto { Id = PathSegmentId(seg), Name = seg })
                        .ToList();

                    var deviceNode = new AssetNodeDto
                    {
                        Id = device.Name,
                        Name = device.Name,
                        Type = "Modbus Device",
                        IsView = true
                    };

                    foreach (var key in keys)
                    {
                        string pointKey = $"{device.Name}_{key}";   // globally unique
                        var pDto = new AssetPointDto
                        {
                            Id = pointKey,
                            Name = key.Replace("_", " "),
                            FullName = $"{device.Name} / {key}",
                            Description = $"Modbus point: {key}",
                            Units = units.TryGetValue(key, out var u) ? u : "",
                            Type = "Analog",
                            Key = pointKey,
                            Value = GetLatestValue(pointKey) ?? GetLatestValue(key),
                            IsWritable = reader is IDeviceWriter,
                            ParentId = PathSegmentId(device.Path.Last()),
                            ParentPath = parentPath
                        };
                        deviceNode.Points.Add(pDto);
                        AddCalculatedPoints(pDto, deviceNode.Points);
                    }

                    MergeDtoIntoTree(root, deviceNode, device.Path);
                }
            }

            return root.Children;
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
                    Id = PathSegmentId(segment),   // stable, deterministic
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

        /// <summary>Stable, URL-safe node ID for a path segment used in the tree and ParentPath links.</summary>
        private static string PathSegmentId(string segment)
            => "path_" + System.Text.RegularExpressions.Regex.Replace(
                segment.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');

        private AssetNodeDto ConvertNode(DezikoNode node, uint deviceId, Dictionary<BacnetObjectId, BacnetObjectInfo> objectLookup, List<PathSegmentDto> currentPath)
        {
            var dto = new AssetNodeDto
            {
                Id = node.ObjectId.ToString(),
                Name = node.FriendlyName,
                Description = node.Description,
                IsView = node.IsView,
                Type = node.IsView ? "Folder" : node.ObjectId.type.ToString()
            };

            var newPath = new List<PathSegmentDto>(currentPath);
            newPath.Add(new PathSegmentDto { Id = node.ObjectId.ToString(), Name = node.FriendlyName });

            foreach (var child in node.Children)
            {
                if (child.IsView)
                {
                    dto.Children.Add(ConvertNode(child, deviceId, objectLookup, newPath));
                }
                else
                {
                    var keyPrefix = $"dev{deviceId}_{BacnetObjectInfo.Sanitise(child.ObjectName)}";
                    var telemetryKey = $"{keyPrefix}_value"; // PROP_PRESENT_VALUE suffix is "value"

                    bool isWritable = false;
                    List<string>? enumValues = null;
                    if (objectLookup.TryGetValue(child.ObjectId, out var info))
                    {
                        isWritable = info.Commandable;

                        bool isMultiState = child.ObjectId.type is
                            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT or
                            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT or
                            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE;

                        bool isBinary = child.ObjectId.type is
                            BacnetObjectTypes.OBJECT_BINARY_INPUT or
                            BacnetObjectTypes.OBJECT_BINARY_OUTPUT or
                            BacnetObjectTypes.OBJECT_BINARY_VALUE;

                        if (isMultiState && info.StateText?.Count > 0)
                            enumValues = info.StateText;
                        else if (isBinary && info.StateText?.Count >= 2)
                            enumValues = info.StateText;   // [inactiveText, activeText]
                        // Binary without StateText → enumValues stays null; frontend shows a toggle
                    }

                    var pDto = new AssetPointDto
                    {
                        Id = child.ObjectId.ToString(),
                        Name = child.FriendlyName,
                        FullName = child.ObjectName,
                        Description = child.Description,
                        Units = child.Units,
                        Type = child.ObjectId.type.ToString(),
                        Key = telemetryKey,
                        Value = GetLatestValue(telemetryKey),
                        IsWritable = isWritable,
                        EnumValues = enumValues,
                        ParentId = node.ObjectId.ToString(),
                        ParentPath = newPath
                    };
                    dto.Points.Add(pDto);
                    AddCalculatedPoints(pDto, dto.Points);
                }
            }

            return dto;
        }

        private void AddCalculatedPoints(AssetPointDto parentPoint, List<AssetPointDto> pointList)
        {
            string unit = parentPoint.Units?.ToLowerInvariant().Trim() ?? "";
            // Normalize m3 and m³ for comparison
            string normUnit = unit.Replace("³", "3");
            string[] validUnits = { "kwh", "wh", "mwh", "m3", "cubic meters", "cubic-meters", "l", "liters" };

            if (!validUnits.Contains(normUnit)) return;

            string baseKey = parentPoint.Key;
            string friendlyUnit = (unit.Contains("wh")) ? (unit == "wh" ? "Wh" : (unit == "mwh" ? "MWh" : "kWh")) : (unit.Contains("l") ? "L" : "m³");

            var intervals = new[] {
                (Suffix: "_hourly",  Label: "hourly"),
                (Suffix: "_daily",   Label: "daily"),
                (Suffix: "_monthly", Label: "monthly"),
                (Suffix: "_yearly",  Label: "yearly")
            };

            foreach (var intv in intervals)
            {
                pointList.Add(new AssetPointDto
                {
                    Id = baseKey + intv.Suffix,
                    Name = parentPoint.Name + " " + intv.Label,
                    FullName = parentPoint.FullName + intv.Suffix,
                    Description = "Calculated consumption value",
                    Units = friendlyUnit,
                    Type = "CALCULATED",
                    Key = baseKey + intv.Suffix,
                    Value = GetLatestValue(baseKey + intv.Suffix),
                    IsWritable = false,
                    ParentId = parentPoint.ParentId,
                    ParentPath = parentPoint.ParentPath
                });
            }
        }

        private string GetLatestValue(string key)
        {
            lock (LatestValues)
            {
                return LatestValues.TryGetValue(key, out var val) ? val?.ToString() ?? "---" : "---";
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
            // 1. Identify the device from the key (e.g. "dev123_...")
            if (!key.StartsWith("dev")) return Task.FromResult(false);
            int firstUnderscore = key.IndexOf('_');
            if (firstUnderscore <= 3) return Task.FromResult(false);

            if (!uint.TryParse(key.Substring(3, firstUnderscore - 3), out uint bacnetId))
                return Task.FromResult(false);

            var device = Config.Devices.FirstOrDefault(d => d.BacnetDeviceId == bacnetId);
            if (device == null) return Task.FromResult(false);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) return Task.FromResult(false);

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null) return Task.FromResult(false);

            try
            {
                // Pass the FULL key to the driver - it already handles the prefix
                writer.Write(conn, device, key, value);
                Console.WriteLine($"  [Dashboard] Manual write success: {key} = {value}");

                // --- Force immediate update in dashboard cache and storage ---
                lock (LatestValues) { LatestValues[key] = value; }

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
                        keys.Add(new AvailableKeyDto
                        {
                            Key = point.Key,
                            Name = point.Name,
                            FullName = point.FullName,
                            Units = point.Units,
                            Type = point.Type,
                            Path = currentPath,
                            Value = point.Value,
                            ParentId = point.ParentId,
                            ParentPath = point.ParentPath,
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

        public List<PropertyDto> GetPointProperties(string key)
        {
            var props = new List<PropertyDto>();
            if (!key.StartsWith("dev")) return props;
            int firstUnderscore = key.IndexOf('_');
            if (firstUnderscore <= 3) return props;

            if (!uint.TryParse(key.Substring(3, firstUnderscore - 3), out uint bacnetId))
                return props;

            var device = Config.Devices.FirstOrDefault(d => d.BacnetDeviceId == bacnetId);
            if (device == null) return props;

            var bacnetDrv = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as BacnetDriver;
            if (bacnetDrv == null) return props;

            var discovered = bacnetDrv.GetDiscoveredObjects(device.Name);
            // Search by key
            var info = discovered.FirstOrDefault(i => i.KeyPrefix + "_value" == key);
            if (info != null)
            {
                props.Add(new PropertyDto { Name = "Object ID", Value = info.ObjectId.ToString() });
                props.Add(new PropertyDto { Name = "Object Name", Value = info.ObjectName });
                props.Add(new PropertyDto { Name = "Description", Value = info.Description });
                props.Add(new PropertyDto { Name = "Profile Name", Value = info.ProfileName });
                props.Add(new PropertyDto { Name = "Category", Value = info.Category.ToString() });
                props.Add(new PropertyDto { Name = "Writable", Value = info.Commandable ? "Yes" : "No" });

                // Add friendly path
                props.Add(new PropertyDto { Name = "Friendly Path", Value = string.Join(" > ", info.NamingPath) });
                if (!string.IsNullOrEmpty(info.NameExtension))
                    props.Add(new PropertyDto { Name = "Alias", Value = info.NameExtension });

                if (info.HighLimit.HasValue)
                    props.Add(new PropertyDto { Name = "High Limit", Value = info.HighLimit.Value.ToString("F2") });
                if (info.LowLimit.HasValue)
                    props.Add(new PropertyDto { Name = "Low Limit", Value = info.LowLimit.Value.ToString("F2") });
                if (info.Deadband.HasValue)
                    props.Add(new PropertyDto { Name = "Deadband", Value = info.Deadband.Value.ToString("F2") });
                if (info.LimitEnable.HasValue)
                {
                    bool hi = (info.LimitEnable.Value & 1) != 0;
                    bool lo = (info.LimitEnable.Value & 2) != 0;
                    string le = (hi && lo) ? "High & Low" : hi ? "High Only" : lo ? "Low Only" : "Disabled";
                    props.Add(new PropertyDto { Name = "Limit Enable", Value = le });
                }

                // --- Live Read of Extended Properties ---
                try
                {
                    using var client = new BacnetClient(new BacnetIpUdpProtocolTransport(0));
                    client.Start();
                    var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
                    if (conn == null) return props;
                    var address = BacnetDriver.ResolveAddress(client, conn.Address, conn.Port, device.DeviceId!.Value, 1000);

                    var extraPropIds = new[] {
                        BacnetPropertyIds.PROP_ALL,             // Try to get everything at once
                        BacnetPropertyIds.PROP_PRESENT_VALUE,
                        BacnetPropertyIds.PROP_STATUS_FLAGS,
                        BacnetPropertyIds.PROP_EVENT_STATE,
                        BacnetPropertyIds.PROP_RELIABILITY,
                        BacnetPropertyIds.PROP_OUT_OF_SERVICE,
                        BacnetPropertyIds.PROP_SETPOINT,
                        BacnetPropertyIds.PROP_PRIORITY_ARRAY,
                        BacnetPropertyIds.PROP_RELINQUISH_DEFAULT,
                        (BacnetPropertyIds)4311, // Substitution Value
                        (BacnetPropertyIds)4312  // Substitution Active
                    };

                    var liveValues = BacnetDriver.ReadObjectProperties(client, address, info.ObjectId, extraPropIds);
                    foreach (var kv in liveValues)
                    {
                        if (kv.Value == null) continue;
                        string name = kv.Key.ToString().Replace("PROP_", "").Replace("_", " ");
                        name = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());

                        string valStr;
                        if (kv.Key == BacnetPropertyIds.PROP_PRIORITY_ARRAY && kv.Value is System.Collections.Generic.IList<BacnetValue> pArray)
                        {
                            // Format priority array as "P1: val, P2: val..."
                            var parts = new List<string>();
                            for (int i = 0; i < pArray.Count; i++)
                            {
                                var v = pArray[i].Value;
                                if (v != null && v.ToString() != "Null")
                                    parts.Add($"P{i + 1}: {v}");
                            }
                            valStr = parts.Count > 0 ? string.Join(", ", parts) : "No active priorities";
                        }
                        else
                        {
                            valStr = kv.Value.ToString() ?? "";
                        }

                        // Avoid duplicates from cached section
                        if (props.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                        props.Add(new PropertyDto { Name = name, Value = valStr });
                    }
                }
                catch { /* best effort live read */ }
            }

            return props;
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
