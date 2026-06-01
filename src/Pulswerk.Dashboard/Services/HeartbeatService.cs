// Services/HeartbeatService.cs
// Health snapshots, GetHeartbeatStatsAsync, and the HealthSnapshot / ConnectionHealthDto DTOs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard
{
    // ── DTOs ─────────────────────────────────────────────────────────────────

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

    // ── Partial class ─────────────────────────────────────────────────────────

    public partial class DashboardDataService
    {
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

        // ── Snapshot sampling ─────────────────────────────────────────────────

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
                    var influxDir = new DirectoryInfo("/var/lib/influxdb2");
                    if (influxDir.Exists)
                        dbSizeBytes += influxDir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

                    var appDir = new DirectoryInfo(AppContext.BaseDirectory);
                    if (appDir.Exists)
                        dbSizeBytes += appDir.EnumerateFiles("*.db").Sum(f => f.Length);

                    string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
                    if (Directory.Exists(dataDir))
                        dbSizeBytes += new DirectoryInfo(dataDir)
                            .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                }
                catch { /* non-critical */ }

                // ── InfluxDB data point stats ─────────────────────────────────
                long telemetryKeys = 0, totalTelemetries = 0;
                try
                {
                    var dbStats = await DataStore.GetStatsAsync();
                    telemetryKeys = dbStats.KeyCount;
                    totalTelemetries = dbStats.PointCount;
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

        // ── Public query methods ──────────────────────────────────────────────

        /// <summary>Returns the full 24h health history.</summary>
        public List<HealthSnapshot> GetHealthHistory()
        {
            lock (_healthLock)
                return _healthHistory.ToList();
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

        public Task<HeartbeatStatsDto> GetHeartbeatStatsAsync()
        {
            HealthSnapshot? latest = null;
            lock (_healthLock)
            {
                if (_healthHistory.Count > 0)
                    latest = _healthHistory.Last();
            }

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
}
