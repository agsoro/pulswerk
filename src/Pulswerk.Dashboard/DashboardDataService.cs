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
    /// </summary>
    public class DashboardDataService
    {
        public LogBuffer LogBuffer { get; }
        public AppConfig Config { get; }
        public TelemetryStore DataStore { get; }
        public AlarmStore AlarmStore { get; }
        public ConcurrentDictionary<string, byte> OfflineDevices { get; }
        public ConcurrentDictionary<string, DateTime> LastPolledAtMap { get; }
        public Dictionary<string, object> LatestValues { get; } = new();
        public Dictionary<string, DateTime> LatestTimestamps { get; } = new();
        public Dictionary<string, IDeviceDriver> Drivers { get; }
        public Stopwatch Uptime { get; } = Stopwatch.StartNew();
        public string Version { get; }

        private long _totalUpdates = 0;
        private long _totalPushUpdates = 0;
        private long _totalPullUpdates = 0;
        private readonly Queue<(DateTime Time, int Count)> _updateHistory = new();
        private readonly Queue<(DateTime Time, int Count)> _pushHistory = new();
        private readonly Queue<(DateTime Time, int Count)> _pullHistory = new();
        private readonly object _statsLock = new();
        private readonly HashSet<string> _bootstrappedKeys = new();

        // ── Unified health history ───────────────────────────────────────────
        // Sampled every 5 minutes, kept for 24 hours (288 data points max).
        private readonly Queue<HealthSnapshot> _healthHistory = new();
        private readonly CalculationEngine _calc;
        private readonly object _healthLock = new();
        private System.Threading.Timer? _healthTimer;

        public DashboardDataService(LogBuffer logBuffer, AppConfig config,
            TelemetryStore dataStore, AlarmStore alarmStore,
            ConcurrentDictionary<string, byte> offlineDevices, ConcurrentDictionary<string, DateTime> lastPolledAtMap,
            Dictionary<string, IDeviceDriver> drivers)
        {
            LogBuffer = logBuffer;
            Config = config;
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

            // Initial registration of known keys (Modbus)
            RegisterAllKnownKeys();

            // Start unified health sampling (every 5 minutes, first sample after 10s)
            _healthTimer = new System.Threading.Timer(_ => SampleHealthSnapshot(), null, 10_000, 5 * 60_000);
        }

        // ── Unified health snapshot ──────────────────────────────────────────

        /// <summary>
        /// A single snapshot of all system health metrics at a point in time.
        /// </summary>
        public class HealthSnapshot
        {
            [JsonPropertyName("t")] public DateTime Time { get; set; }
            [JsonPropertyName("pushTotal")] public long PushTotal { get; set; }
            [JsonPropertyName("pullTotal")] public long PullTotal { get; set; }
            [JsonPropertyName("workingSetMb")] public long WorkingSetMb { get; set; }
            [JsonPropertyName("gcHeapMb")] public long GcHeapMb { get; set; }
            [JsonPropertyName("dbSizeMb")] public long DbSizeMb { get; set; }
            [JsonPropertyName("telemetryKeys")] public long TelemetryKeys { get; set; }
            [JsonPropertyName("totalTelemetries")] public long TotalTelemetries { get; set; }
            [JsonPropertyName("connections")] public Dictionary<string, ConnSnapshotEntry> Connections { get; set; } = new();
        }

        public class ConnSnapshotEntry
        {
            [JsonPropertyName("online")] public int Online { get; set; }
            [JsonPropertyName("total")] public int Total { get; set; }
        }

        private async void SampleHealthSnapshot()
        {
            try
            {
                var now = DateTime.UtcNow;
                var process = System.Diagnostics.Process.GetCurrentProcess();

                // ── Database size on disk ─────────────────────────────────────
                long dbSizeBytes = 0;
                try
                {
                    // 1. InfluxDB (mounted volume)
                    var influxDir = new DirectoryInfo("/var/lib/influxdb2");
                    if (influxDir.Exists)
                        dbSizeBytes += influxDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

                    // 2. SQLite / local data files
                    var appDir = new DirectoryInfo(AppContext.BaseDirectory);
                    if (appDir.Exists)
                        dbSizeBytes += appDir.EnumerateFiles("*.db").Sum(f => f.Length);

                    string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                    if (Directory.Exists(dataDir))
                        dbSizeBytes += new DirectoryInfo(dataDir)
                            .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                }
                catch { /* non-critical */ }

                // ── InfluxDB data point stats (key count, point count) ─────────
                long telemetryKeys = 0, totalTelemetries = 0;
                try
                {
                    var dbStats = await DataStore.GetStatsAsync();
                    telemetryKeys = dbStats.KeyCount;
                    totalTelemetries = dbStats.PointCount;
                    // Use InfluxDB internal stats as fallback for disk size
                    if (dbSizeBytes == 0 || dbSizeBytes < 1000)
                        dbSizeBytes = dbStats.DiskSizeBytes;
                }
                catch (Exception ex)
                {
                    Log.Debug($"[Dashboard] InfluxDB stats query failed in snapshot: {ex.Message}");
                }

                var snapshot = new HealthSnapshot
                {
                    Time = now,
                    PushTotal = _totalPushUpdates,
                    PullTotal = _totalPullUpdates,
                    WorkingSetMb = process.WorkingSet64 / (1024 * 1024),
                    GcHeapMb = GC.GetTotalMemory(false) / (1024 * 1024),
                    DbSizeMb = dbSizeBytes / (1024 * 1024),
                    TelemetryKeys = telemetryKeys,
                    TotalTelemetries = totalTelemetries
                };

                foreach (var conn in Config.Connections)
                {
                    var devices = Config.Devices.Where(d => d.ConnectionId == conn.Id).ToList();
                    snapshot.Connections[conn.Id] = new ConnSnapshotEntry
                    {
                        Total = devices.Count,
                        Online = devices.Count(d =>
                            !OfflineDevices.ContainsKey(d.Name) &&
                            LastPolledAtMap.TryGetValue(d.Name, out var lp) && lp != default &&
                            (now - lp).TotalMinutes <= 5)
                    };
                }

                lock (_healthLock)
                {
                    _healthHistory.Enqueue(snapshot);
                    while (_healthHistory.Count > 288)
                        _healthHistory.Dequeue();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Health snapshot failed: {ex.Message}");
            }
        }

        /// <summary>Returns the full 24h health history.</summary>
        public List<HealthSnapshot> GetHealthHistory()
        {
            lock (_healthLock)
            {
                return _healthHistory.ToList();
            }
        }

        /// <summary>Returns the health history for a specific connection.</summary>
        public List<(DateTime Time, int Online, int Total)> GetConnectionHealth(string connId)
        {
            lock (_healthLock)
            {
                return _healthHistory
                    .Where(s => s.Connections.ContainsKey(connId))
                    .Select(s => (s.Time, s.Connections[connId].Online, s.Connections[connId].Total))
                    .ToList();
            }
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
                            // Register key for metadata purposes and for calculation engine
                            _calc.RegisterKey(pointKey, u);
                        }
                    }
                }
            }
        }


        public Dictionary<string, (double val, DateTime ts)> UpdateTelemetries(Dictionary<string, object> values, bool isPush = false)
        {
            if (values == null) return new Dictionary<string, (double val, DateTime ts)>();

            var persistedResults = new Dictionary<string, (double val, DateTime ts)>();
            DateTime now = DateTime.UtcNow;
            long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

            lock (LatestValues)
            {
                foreach (var kvp in values)
                {
                    // Skip BACnet error strings – they must never reach the UI
                    var vs = kvp.Value?.ToString();
                    if (vs != null && vs.Contains("ERROR_")) continue;
                    if (kvp.Value is null) continue;
                    LatestValues[kvp.Key] = kvp.Value;
                    LatestTimestamps[kvp.Key] = now;

                    // If it's a numeric value, mark it for persistence
                    if (TryToDouble(kvp.Value, out double d))
                    {
                        persistedResults[kvp.Key] = (d, now);

                        // Process via Calculation Engine to generate live/persisted deltas (_hourly, _daily, etc)
                        var (live, persisted) = _calc.Process(kvp.Key, kvp.Value, now);
                        foreach (var l in live)
                        {
                            LatestValues[l.Key] = l.Value;
                            LatestTimestamps[l.Key] = now;
                        }
                        foreach (var p in persisted)
                        {
                            persistedResults[p.Key] = p.Value;
                        }
                    }
                }
            }
            RecordUpdate(values.Count, isPush);
            return persistedResults;
        }

        private static bool TryToDouble(object val, out double d)
        {
            d = 0;
            if (val == null) return false;
            try { d = Convert.ToDouble(val); return true; }
            catch { return false; }
        }

        private void RecordUpdate(int count, bool isPush)
        {
            lock (_statsLock)
            {
                _totalUpdates += count;
                _updateHistory.Enqueue((DateTime.UtcNow, count));

                if (isPush)
                {
                    _totalPushUpdates += count;
                    _pushHistory.Enqueue((DateTime.UtcNow, count));
                }
                else
                {
                    _totalPullUpdates += count;
                    _pullHistory.Enqueue((DateTime.UtcNow, count));
                }

                // Keep only last 60 seconds
                var cutoff = DateTime.UtcNow.AddSeconds(-60);
                while (_updateHistory.Count > 0 && _updateHistory.Peek().Time < cutoff)
                    _updateHistory.Dequeue();
                while (_pushHistory.Count > 0 && _pushHistory.Peek().Time < cutoff)
                    _pushHistory.Dequeue();
                while (_pullHistory.Count > 0 && _pullHistory.Peek().Time < cutoff)
                    _pullHistory.Dequeue();
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

        public List<AssetNodeDto> GetAssetTrees(bool includeLiveValues = true)
        {
            var root = new AssetNodeDto { Name = "Root", Type = "Root" };

            foreach (var device in Config.Devices)
            {
                AssetNodeDto? deviceTree = null;

                if (device.DeviceType == "virtual")
                {
                    deviceTree = new AssetNodeDto
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Type = "Device",
                        IsView = true
                    };
                }
                else if (Drivers.TryGetValue(device.Name, out var driver))
                {
                    deviceTree = driver.GetAssetHierarchy(device);
                }

                if (deviceTree == null) continue;

                // Add virtual points if configured
                if (device.Telemetries != null)
                {
                    foreach (var dp in device.Telemetries)
                    {
                        var tDto = new TelemetryDto
                        {
                            Id = $"{device.Id}_{dp.Id}",
                            Key = $"{device.Id}_{dp.Id}",
                            Name = dp.Name,
                            FullName = $"{device.Name} {dp.Name}",
                            Units = dp.Units ?? "",
                            Type = "Calculated",
                            Description = $"Formula: {dp.Formula}",
                            LastUpdate = "Live",
                            Value = includeLiveValues ? GetLiveValueForFormula(dp.Formula, dp.Units, device) : "---"
                        };

                        if (dp.Path != null && dp.Path.Count > 0)
                        {
                            tDto.ParentPath = dp.Path.Take(dp.Path.Count - 1).Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg }).ToList();
                            tDto.ParentId = AssetNodeDto.PathSegmentId(dp.Path.Last());

                            var lastSegment = dp.Path.Last();
                            var parentPath = dp.Path.Take(dp.Path.Count - 1).ToList();

                            var folderNode = new AssetNodeDto
                            {
                                Id = AssetNodeDto.PathSegmentId(lastSegment),
                                Name = lastSegment,
                                Type = "Folder",
                                IsView = true
                            };
                            folderNode.Telemetries.Add(tDto);
                            // Merge independently of the device tree
                            MergeDtoIntoTree(root, folderNode, parentPath);
                        }
                        else
                        {
                            if (device.Path != null && device.Path.Count > 0)
                            {
                                tDto.ParentPath = device.Path.Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg }).ToList();
                                tDto.ParentId = AssetNodeDto.PathSegmentId(device.Path.Last());
                            }
                            deviceTree.Telemetries.Add(tDto);
                        }
                    }
                }

                // Populate live values and consumption points
                PopulateTree(deviceTree);

                // Merge into the global hierarchy
                if (device.Path != null && device.Path.Count > 0)
                    MergeDtoIntoTree(root, deviceTree, device.Path);
                else
                    root.Children.Add(deviceTree);
            }

            return root.Children;
        }

        private string GetLiveValueForFormula(string formula, string? units, DeviceConfig? device = null)
        {
            // Simple live value resolver for tree view
            try
            {
                formula = ExpandFormula(formula, device);

                if (formula.StartsWith("pathsum("))
                {
                    var keys = ResolvePathSumKeys(formula);
                    double sum = 0;
                    foreach (var k in keys)
                    {
                        if (LatestValues.TryGetValue(k, out var v) && TryToDouble(v, out double d))
                            sum += d;
                    }
                    return Math.Round(sum, 2).ToString(CultureInfo.InvariantCulture);
                }

                // Handle consumption modifiers in tree (current hour/day from CalculationEngine)
                if (formula.Contains(":consumption:"))
                {
                    var parts = formula.Split(":consumption:");
                    var baseKey = parts[0];
                    var interval = parts[1];
                    string suffix = interval switch
                    {
                        "1h" => "_hourly",
                        "1d" => "_daily",
                        "1m" => "_monthly",
                        "1y" => "_yearly",
                        _ => ""
                    };
                    if (LatestValues.TryGetValue(baseKey + suffix, out var v))
                        return v?.ToString() ?? "0";
                }

                // If formula is just a key, return its value
                if (LatestValues.TryGetValue(formula, out var rawVal) && TryToDouble(rawVal, out double rawNum))
                {
                    return Math.Round(rawNum, 2).ToString(CultureInfo.InvariantCulture);
                }

                // Mathematical evaluation fallback
                // Extract keys wrapped in square brackets e.g. [meter_power] - [other_power]
                string expr = formula;
                bool hasMath = false;

                expr = System.Text.RegularExpressions.Regex.Replace(expr, @"\[([^\]]+)\]", match =>
                {
                    hasMath = true;
                    string key = match.Groups[1].Value;
                    if (LatestValues.TryGetValue(key, out var val) && TryToDouble(val, out double num))
                    {
                        return num.ToString(CultureInfo.InvariantCulture);
                    }
                    return "0";
                });

                // If no square brackets were found, but there are math operators
                if (!hasMath && (expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/")))
                {
                    expr = System.Text.RegularExpressions.Regex.Replace(expr, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
                    {
                        string key = match.Value;
                        if (key == "pathsum" || key == "consumption") return key;
                        if (LatestValues.TryGetValue(key, out var val) && TryToDouble(val, out double num))
                        {
                            return num.ToString(CultureInfo.InvariantCulture);
                        }
                        return "0";
                    });
                    hasMath = true;
                }

                if (hasMath)
                {
                    using var dt = new DataTable();
                    var result = dt.Compute(expr, "");
                    if (result != DBNull.Value && TryToDouble(result, out double d))
                    {
                        return Math.Round(d, 2).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { /* fallback to default */ }

            return "-";
        }

        private void PopulateTree(AssetNodeDto node)
        {
            // Telemetries at this level
            var originalPoints = node.Telemetries.ToList();
            foreach (var p in originalPoints)
            {
                if (p.Type != "Calculated")
                {
                    p.Value = GetLatestValue(p.Key);
                    p.LastUpdate = FormatLastUpdate(p.Key);
                }
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
                    existing.Telemetries.AddRange(node.Telemetries);
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
                    nextNode.Telemetries.AddRange(node.Telemetries);
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

        /// <summary>
        /// Returns data point history from InfluxDB for a single key within a specific time range.
        /// </summary>
        public async Task<List<TsPoint>> GetTelemetryHistoryAsync(string key, long startTs, long endTs)
        {
            Log.Debug($"[Dashboard] History requested: {key}, range={startTs} to {endTs}");

            // Intercept virtual telemetry points with formulas to generate timeseries on the fly
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device != null && device.DeviceType == "virtual")
            {
                string pointKey = key.Substring(device.Id.Length + 1);
                var point = device.Telemetries?.FirstOrDefault(t => t.Id == pointKey);
                if (point != null && !string.IsNullOrWhiteSpace(point.Formula))
                {
                    return await GetVirtualTelemetryHistoryAsync(point.Formula, device, startTs, endTs);
                }
            }

            return await DataStore.QueryAsync(key, startTs, endTs, limit: 5000);
        }

        private async Task<List<TsPoint>> GetVirtualTelemetryHistoryAsync(string formula, DeviceConfig device, long startTs, long endTs)
        {
            var keys = new HashSet<string>();
            formula = ExpandFormula(formula, device);

            if (formula.StartsWith("pathsum("))
            {
                var pathSumKeys = ResolvePathSumKeys(formula);
                foreach (var k in pathSumKeys) keys.Add(k);
            }
            else if (formula.Contains(":consumption:"))
            {
                var parts = formula.Split(":consumption:");
                var baseKey = parts[0];
                var interval = parts[1];
                string suffix = interval switch { "1h" => "_hourly", "1d" => "_daily", "1m" => "_monthly", "1y" => "_yearly", _ => "" };
                keys.Add(baseKey + suffix);
            }
            else
            {
                bool hasBrackets = false;
                System.Text.RegularExpressions.Regex.Replace(formula, @"\[([^\]]+)\]", match =>
                {
                    hasBrackets = true;
                    keys.Add(match.Groups[1].Value);
                    return "";
                });

                if (!hasBrackets && (formula.Contains("+") || formula.Contains("-") || formula.Contains("*") || formula.Contains("/")))
                {
                    System.Text.RegularExpressions.Regex.Replace(formula, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
                    {
                        string k = match.Value;
                        if (k != "pathsum" && k != "consumption") keys.Add(k);
                        return "";
                    });
                }
                else if (!hasBrackets)
                {
                    keys.Add(formula);
                }
            }

            var keyList = keys.ToList();
            if (keyList.Count == 0) return new List<TsPoint>();

            if (keyList.Count == 1 && (formula == keyList[0] || formula == $"[{keyList[0]}]"))
            {
                return await DataStore.QueryAsync(keyList[0], startTs, endTs, limit: 5000);
            }

            var historyMap = await DataStore.QueryMultipleAsync(keyList, startTs, endTs, maxPointsPerKey: 5000);
            var mergedPoints = new List<TsPoint>();

            var allTimestamps = historyMap.Values
                .SelectMany(list => list)
                .Select(p => p.Ts)
                .Distinct()
                .OrderBy(ts => ts)
                .ToList();

            if (allTimestamps.Count == 0) return mergedPoints;

            var lastKnownValues = new Dictionary<string, double>();
            var iterators = new Dictionary<string, int>();
            foreach (var k in keyList) iterators[k] = 0;

            using var dt = new System.Data.DataTable();

            foreach (var ts in allTimestamps)
            {
                foreach (var k in keyList)
                {
                    var list = historyMap.TryGetValue(k, out var l) ? l : null;
                    if (list == null) continue;

                    int idx = iterators[k];
                    while (idx < list.Count && list[idx].Ts <= ts)
                    {
                        var val = list[idx].Value;
                        if (val.HasValue) lastKnownValues[k] = val.Value;
                        idx++;
                    }
                    iterators[k] = idx;
                }

                try
                {
                    string expr = formula;

                    if (formula.StartsWith("pathsum("))
                    {
                        double sum = 0;
                        foreach (var k in keyList) if (lastKnownValues.TryGetValue(k, out double v)) sum += v;
                        mergedPoints.Add(new TsPoint(ts, sum, null));
                        continue;
                    }

                    if (formula.Contains(":consumption:"))
                    {
                        string k = keyList.First();
                        if (lastKnownValues.TryGetValue(k, out double v))
                        {
                            mergedPoints.Add(new TsPoint(ts, v, null));
                        }
                        continue;
                    }

                    bool hasMath = false;
                    expr = System.Text.RegularExpressions.Regex.Replace(expr, @"\[([^\]]+)\]", match =>
                    {
                        hasMath = true;
                        string k = match.Groups[1].Value;
                        if (lastKnownValues.TryGetValue(k, out double num)) return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return "0";
                    });

                    if (!hasMath && (expr.Contains("+") || expr.Contains("-") || expr.Contains("*") || expr.Contains("/")))
                    {
                        expr = System.Text.RegularExpressions.Regex.Replace(expr, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
                        {
                            string k = match.Value;
                            if (lastKnownValues.TryGetValue(k, out double num)) return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            return "0";
                        });
                        hasMath = true;
                    }

                    if (hasMath)
                    {
                        var result = dt.Compute(expr, "");
                        if (result != DBNull.Value && TryToDouble(result, out double d))
                        {
                            mergedPoints.Add(new TsPoint(ts, Math.Round(d, 2), null));
                        }
                    }
                }
                catch { /* Ignore compute errors for this point */ }
            }

            return mergedPoints;
        }

        /// <summary>
        /// Returns data point history from InfluxDB for a single key, looking back a number of days from now.
        /// </summary>
        public async Task<List<TsPoint>> GetTelemetryHistoryAsync(string key, double days = 1.0)
        {
            long endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000;
            long startTs = endTs - (long)(days * 24 * 60 * 60 * 1000.0) - 5000;
            return await GetTelemetryHistoryAsync(key, startTs, endTs);
        }

        public Task<bool> WriteValueAsync(string key, double value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null) { Log.Error($"[Dashboard] Write rejected: no device found for key '{key}'"); return Task.FromResult(false); }

            // Extract the technical point key from the scoped key {DeviceId}_{PointKey}
            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) { Log.Error($"[Dashboard] Write rejected: no connection for device '{device.Name}'"); return Task.FromResult(false); }

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null) { Log.Error($"[Dashboard] Write rejected: driver for '{device.Name}' is not an IDeviceWriter"); return Task.FromResult(false); }
            if (!writer.IsWritable(driverKey)) { Log.Error($"[Dashboard] Write rejected: key '{driverKey}' (full: '{key}') is not writable"); return Task.FromResult(false); }

            try
            {
                writer.Write(conn, device, driverKey, value);
                Log.Info($"[Dashboard] Manual write success: {key} = {value}");

                // Immediately update LatestValues with the correctly formatted
                // display value (state text resolved) via the converter.
                object displayVal = value;
                if (drv is Pulswerk.Drivers.BACnet.BacnetDriver bacDrv)
                {
                    var cachedObj = bacDrv.FindCachedObject(key);
                    if (cachedObj != null)
                    {
                        double internalVal = BacnetValueConverter.FromDisplayValue(cachedObj, value);
                        displayVal = BacnetValueConverter.FormatValue(
                            cachedObj, BACnet.BacnetPropertyIds.PROP_PRESENT_VALUE, internalVal);
                    }
                }
                lock (LatestValues)
                {
                    LatestValues[key] = displayVal;
                    LatestTimestamps[key] = DateTime.UtcNow;
                }

                // Also update InfluxDB immediately so charts show the change
                DataStore.Insert(key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), value);
                DataStore.Flush();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> WriteComplexValueAsync(string key, object value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null) return Task.FromResult(false);

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) return Task.FromResult(false);

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null || !writer.IsWritable(driverKey)) return Task.FromResult(false);

            try
            {
                writer.WriteComplex(conn, device, driverKey, value);
                Log.Info($"[Dashboard] Complex write success: {key}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Complex write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Returns all available data point keys with metadata for the dashboard widget key picker.
        /// Flattens the asset tree into a list of selectable keys.
        /// </summary>
        public List<AvailableTelemetryDto> GetAvailableTelemetries(bool includeLiveValues = false)
        {
            var keys = new List<AvailableTelemetryDto>();
            var trees = GetAssetTrees(includeLiveValues);

            void ExtractKeys(List<AssetNodeDto> nodes, string pathPrefix)
            {
                foreach (var node in nodes)
                {
                    string currentPath = string.IsNullOrEmpty(pathPrefix)
                        ? node.Name
                        : $"{pathPrefix} › {node.Name}";

                    foreach (var dp in node.Telemetries)
                    {
                        var dev = IdentifyDeviceFromTelemetryKey(dp.Key);
                        keys.Add(new AvailableTelemetryDto
                        {
                            Key = dp.Key,
                            Name = dp.Name,
                            FullName = dp.FullName,
                            Units = dp.Units,
                            Type = dp.Type,
                            Path = currentPath,
                            Value = dp.Value,
                            LastUpdate = FormatLastUpdate(dp.Key),
                            ParentId = dp.ParentId,
                            ParentPath = dp.ParentPath,
                            Device = dev?.Name ?? "System",
                            Connection = dev?.ConnectionId ?? "-",
                            IsWritable = dp.IsWritable,
                            EnumValues = dp.EnumValues
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
        /// Fetches data point history for multiple keys within a time range.
        /// Supports virtual keys: "key:consumption:1h" or "pathsum('Path', 'Unit'):consumption:1d".
        /// </summary>
        public async Task<Dictionary<string, List<TsPoint>>> GetTelemetryHistoryForWidgetAsync(
            List<string> telemetryKeys, long startTs, long endTs)
        {
            var result = new Dictionary<string, List<TsPoint>>();
            var realKeys = new List<string>();
            var realKeyMap = new Dictionary<string, string>(); // Requested Key -> Expanded Key

            foreach (var key in telemetryKeys)
            {
                // Resolve persistent virtual point ID to formula
                string keyWithoutModifier = key;
                string? extraModifier = null;
                if (key.Contains(":consumption:"))
                {
                    var parts = key.Split(":consumption:");
                    keyWithoutModifier = parts[0];
                    extraModifier = parts[1];
                }

                // Resolve calculated point within any device (physical or virtual)
                var vdev = Config.Devices.FirstOrDefault(d =>
                    keyWithoutModifier.StartsWith(d.Id + "_") &&
                    d.Telemetries != null &&
                    d.Telemetries.Any(p => p.Id == keyWithoutModifier.Substring(d.Id.Length + 1))
                );
                string effectiveKey = keyWithoutModifier;

                if (vdev != null && vdev.Telemetries != null)
                {
                    string pointId = keyWithoutModifier.Substring(vdev.Id.Length + 1);
                    var dp = vdev.Telemetries.FirstOrDefault(p => p.Id == pointId);
                    if (dp != null) effectiveKey = ExpandFormula(dp.Formula, vdev);
                }

                effectiveKey = ExpandFormula(effectiveKey, vdev);

                string baseKey = effectiveKey;
                string? consumptionInterval = extraModifier;

                if (effectiveKey.Contains(":consumption:"))
                {
                    var parts = effectiveKey.Split(":consumption:");
                    baseKey = parts[0];
                    consumptionInterval ??= parts[1];
                }

                if (baseKey.StartsWith("pathsum("))
                {
                    var resolvedKeys = ResolvePathSumKeys(baseKey);
                    result[key] = await DataStore.QuerySumAsync(resolvedKeys, startTs, endTs, consumptionInterval);
                }
                else if (consumptionInterval != null)
                {
                    result[key] = await DataStore.QueryConsumptionAsync(baseKey, consumptionInterval, startTs, endTs);
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(baseKey, @"^[a-zA-Z0-9_\-\.:]+$"))
                {
                    result[key] = await GetVirtualTelemetryHistoryAsync(baseKey, vdev!, startTs, endTs);
                }
                else
                {
                    realKeys.Add(effectiveKey);
                    realKeyMap[key] = effectiveKey;
                }
            }

            if (realKeys.Count > 0)
            {
                var realData = await DataStore.QueryMultipleAsync(realKeys, startTs, endTs);
                foreach (var entry in realKeyMap)
                {
                    if (realData.TryGetValue(entry.Value, out var data))
                        result[entry.Key] = data;
                }
            }

            return result;
        }

        private List<string> ResolvePathSumKeys(string pathSumExpr)
        {
            var match = System.Text.RegularExpressions.Regex.Match(pathSumExpr,
                @"pathsum\s*\(\s*['""](.+?)['""]\s*,\s*['""](.+?)['""]\s*\)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (!match.Success) return new List<string>();

            var fullPathExpr = match.Groups[1].Value.Replace("/", " › ");
            var unitPattern = match.Groups[2].Value.ToLowerInvariant();

            // If the path expression ends with a wildcard segment (e.g. .../*_energy_import), 
            // we split it into a base path and a key pattern.
            string pathPattern = fullPathExpr;
            string? keySuffixPattern = null;

            int lastIdx = fullPathExpr.LastIndexOf(" › ");
            if (lastIdx >= 0)
            {
                string tail = fullPathExpr.Substring(lastIdx + 3);
                if (tail.Contains("*"))
                {
                    pathPattern = fullPathExpr.Substring(0, lastIdx);
                    keySuffixPattern = tail;
                }
            }
            else if (fullPathExpr.Contains("*"))
            {
                pathPattern = "*";
                keySuffixPattern = fullPathExpr;
            }

            string ToRegex(string glob) => "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*") + "$";

            bool IsMatch(string pattern, string value)
            {
                if (pattern == "*" || pattern == "") return true;
                if (!pattern.Contains("*")) return (value ?? "").Contains(pattern, StringComparison.OrdinalIgnoreCase);
                return System.Text.RegularExpressions.Regex.IsMatch(value ?? "", ToRegex(pattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            var resolved = new List<string>();
            var allKeys = GetAvailableTelemetries();
            foreach (var ak in allKeys)
            {
                // Match the path
                if (IsMatch(pathPattern, ak.Path ?? ""))
                {
                    // Match the key (if provided in path) and the unit
                    bool keyMatch = keySuffixPattern == null || IsMatch(keySuffixPattern, ak.Key ?? "");
                    bool unitMatch = IsMatch(unitPattern, ak.Units ?? "") || IsMatch(unitPattern, ak.Key ?? "");

                    if (keyMatch && unitMatch)
                        resolved.Add(ak.Key ?? "");
                }
            }
            return resolved;
        }

        /// <summary>
        /// Gets current values for a list of data point keys. Used by latest-values and single-value widgets.
        /// </summary>
        public Dictionary<string, string> GetCurrentValues(List<string> keys)
        {
            var result = new Dictionary<string, string>();
            lock (LatestValues)
            {
                foreach (var key in keys)
                {
                    if (LatestValues.TryGetValue(key, out var val))
                    {
                        result[key] = val?.ToString() ?? "---";
                        continue;
                    }

                    // Check if it's a virtual telemetry point
                    var device = IdentifyDeviceFromTelemetryKey(key);
                    if (device != null && device.DeviceType == "virtual" && device.Telemetries != null)
                    {
                        string pointKey = key.Substring(device.Id.Length + 1);
                        var dp = device.Telemetries.FirstOrDefault(t => t.Id == pointKey);
                        if (dp != null && !string.IsNullOrWhiteSpace(dp.Formula))
                        {
                            result[key] = GetLiveValueForFormula(dp.Formula, dp.Units, device);
                            continue;
                        }
                    }

                    // Handle direct consumption, pathsum, or inline math formulas requested by widgets
                    if (key.Contains(":consumption:") || !System.Text.RegularExpressions.Regex.IsMatch(key, @"^[a-zA-Z0-9_\-\.:]+$"))
                    {
                        result[key] = GetLiveValueForFormula(key, null, null);
                        continue;
                    }

                    result[key] = "---";
                }
            }
            return result;
        }

        public async Task<List<PropertyDto>> GetPropertiesAsync(string key)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
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

        private DeviceConfig? IdentifyDeviceFromTelemetryKey(string key)
        {
            // The key format is {DeviceId}_{PointKey}. 
            // We search for the longest matching DeviceId to handle underscores in IDs correctly.
            return Config.Devices
                .Where(d => key.StartsWith(d.Id + "_"))
                .OrderByDescending(d => d.Id.Length)
                .FirstOrDefault();
        }

        private string ExpandFormula(string formula, DeviceConfig? device)
        {
            if (device == null || string.IsNullOrWhiteSpace(formula)) return formula;
            if (formula.Contains("pathsum(", StringComparison.OrdinalIgnoreCase)) return formula;

            // Prepend device ID to any token that looks like a data point key and doesn't already have a device prefix.
            // We use a regex that matches identifiers starting with a letter.
            return System.Text.RegularExpressions.Regex.Replace(formula, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
            {
                string token = match.Value;

                // Skip known keywords and modifiers
                if (token.Equals("consumption", StringComparison.OrdinalIgnoreCase)) return token;
                if (token.Equals("pathsum", StringComparison.OrdinalIgnoreCase)) return token;

                string baseToken = token;
                if (token.Contains(":"))
                {
                    baseToken = token.Split(':')[0];
                }

                // If it already starts with a known device ID + underscore, it's already expanded.
                if (Config.Devices.Any(d => baseToken.StartsWith(d.Id + "_")))
                    return token;

                // Otherwise, assume it's a local key and prepend the device ID.
                return $"{device.Id}_{token}";
            });
        }

        public Task<HeartbeatStatsDto> GetHeartbeatStatsAsync()
        {
            // Read cached values from the latest health snapshot (sampled every 5 min)
            HealthSnapshot? latest = null;
            lock (_healthLock)
            {
                if (_healthHistory.Count > 0)
                    latest = _healthHistory.Last();
            }

            // ── Device health breakdown (cheap — just in-memory maps) ────
            var now = DateTime.UtcNow;
            int staleCount = 0;
            int offlineCount = OfflineDevices.Count;
            DateTime oldestSeen = now;
            var connSummaries = new List<ConnectionHealthDto>();

            foreach (var conn in Config.Connections)
            {
                var connDevices = Config.Devices.Where(d => d.ConnectionId == conn.Id).ToList();
                int connOnline = 0, connStale = 0, connOffline = 0;

                foreach (var d in connDevices)
                {
                    if (OfflineDevices.ContainsKey(d.Name))
                    {
                        connOffline++;
                        continue;
                    }

                    if (LastPolledAtMap.TryGetValue(d.Name, out var polledAt) && polledAt != default)
                    {
                        if ((now - polledAt).TotalMinutes > 5)
                        {
                            connStale++;
                            staleCount++;
                        }
                        else
                        {
                            connOnline++;
                        }
                        if (polledAt < oldestSeen) oldestSeen = polledAt;
                    }
                    else
                    {
                        connStale++;
                        staleCount++;
                    }
                }

                connSummaries.Add(new ConnectionHealthDto
                {
                    Id = conn.Id,
                    Name = conn.EffectiveName,
                    Type = conn.Type,
                    Online = connOnline,
                    Stale = connStale,
                    Offline = connOffline,
                    Total = connDevices.Count
                });
            }

            // ── Memory stats (live — cheap) ──────────────────────────────
            var process = System.Diagnostics.Process.GetCurrentProcess();
            long workingSetMb = process.WorkingSet64 / (1024 * 1024);
            long gcHeapMb = GC.GetTotalMemory(false) / (1024 * 1024);

            return Task.FromResult(new HeartbeatStatsDto
            {
                UptimeSeconds = (long)Uptime.Elapsed.TotalSeconds,
                Version = Version,
                IsScanning = Drivers.Values.Any(d => d.IsBusy),
                TotalDevices = Config.Devices.Count,
                OnlineDevices = Config.Devices.Count - OfflineDevices.Count - staleCount,
                StaleDevices = staleCount,
                OfflineDevices = offlineCount,
                TotalTelemetryKeys = latest?.TelemetryKeys ?? 0,
                TotalTelemetries = latest?.TotalTelemetries ?? 0,
                UpdatesPerMinute = GetUpdatesPerMinute(),
                TotalUpdates = _totalUpdates,
                TotalPushUpdates = _totalPushUpdates,
                TotalPullUpdates = _totalPullUpdates,
                DatabaseSizeBytes = (latest?.DbSizeMb ?? 0) * 1024 * 1024,
                WorkingSetMb = workingSetMb,
                GcHeapMb = gcHeapMb,
                OldestDeviceSeenUtc = oldestSeen == now ? null : oldestSeen.ToString("yyyy-MM-dd HH:mm:ss"),
                TcpConnections = Pulswerk.Drivers.Modbus.ModbusConnection.ActiveConnectionCount,
                Connections = connSummaries
            });
        }

    }

    public class HeartbeatStatsDto
    {
        [JsonPropertyName("uptimeSeconds")] public long UptimeSeconds { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("isScanning")] public bool IsScanning { get; set; }
        [JsonPropertyName("totalDevices")] public int TotalDevices { get; set; }
        [JsonPropertyName("onlineDevices")] public int OnlineDevices { get; set; }
        [JsonPropertyName("staleDevices")] public int StaleDevices { get; set; }
        [JsonPropertyName("offlineDevices")] public int OfflineDevices { get; set; }
        [JsonPropertyName("totalTelemetryKeys")] public long TotalTelemetryKeys { get; set; }
        [JsonPropertyName("totalTelemetries")] public long TotalTelemetries { get; set; }
        [JsonPropertyName("updatesPerMinute")] public double UpdatesPerMinute { get; set; }
        [JsonPropertyName("totalUpdates")] public long TotalUpdates { get; set; }
        [JsonPropertyName("totalPushUpdates")] public long TotalPushUpdates { get; set; }
        [JsonPropertyName("totalPullUpdates")] public long TotalPullUpdates { get; set; }
        [JsonPropertyName("databaseSizeBytes")] public long DatabaseSizeBytes { get; set; }
        [JsonPropertyName("workingSetMb")] public long WorkingSetMb { get; set; }
        [JsonPropertyName("gcHeapMb")] public long GcHeapMb { get; set; }
        [JsonPropertyName("oldestDeviceSeenUtc")] public string? OldestDeviceSeenUtc { get; set; }
        [JsonPropertyName("tcpConnections")] public int TcpConnections { get; set; }
        [JsonPropertyName("connections")] public List<ConnectionHealthDto> Connections { get; set; } = new();
    }

    public class ConnectionHealthDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("online")] public int Online { get; set; }
        [JsonPropertyName("stale")] public int Stale { get; set; }
        [JsonPropertyName("offline")] public int Offline { get; set; }
        [JsonPropertyName("total")] public int Total { get; set; }
    }
}
