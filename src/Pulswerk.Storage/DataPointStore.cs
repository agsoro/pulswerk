// DataPointStore.cs – InfluxDB-backed time-series persistence
//
//  Stores all data points (BACnet + Modbus) in InfluxDB 2.x.
//  Provides query methods for the dashboard (trend charts, widget data).
//
//  InfluxDB data model:
//    Measurement: "data_point"
//    Tag:   key   (the data point key, e.g. "dev10_ai_1_value")
//    Field: value (numeric) or value_str (string for enum labels)
//    Time:  nanosecond precision Unix timestamp

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using NodaTime;

using Pulswerk.Core;

namespace Pulswerk.Storage
{
    public sealed class DataPointStore : IDisposable
    {
        private readonly InfluxDBClient _client;
        private readonly WriteApi _writeApi;
        private readonly string _bucket;
        private readonly string _org;
        private readonly int _compactionAfterDays;
        private bool _disposed;

        /// <summary>
        /// Creates a new DataPointStore backed by InfluxDB 2.x.
        /// Auto-creates buckets and compaction task if they don't exist.
        /// </summary>
        /// <param name="url">InfluxDB URL, e.g. "http://localhost:8086"</param>
        /// <param name="token">Admin or write token</param>
        /// <param name="org">InfluxDB organization name</param>
        /// <param name="bucket">Bucket name for data</param>
        /// <param name="retentionDays">Data retention in days (0 = infinite)</param>
        /// <param name="compactionAfterDays">Downsample to 15-min intervals after this many days</param>
        public DataPointStore(string url, string token, string org, string bucket,
            int retentionDays = 730, int compactionAfterDays = 700)
        {
            _org = org;
            _bucket = bucket;
            _compactionAfterDays = compactionAfterDays;

            _client = new InfluxDBClient(url, token);

            // Use the non-blocking batching write API for high throughput
            _writeApi = _client.GetWriteApi(new WriteOptions
            {
                BatchSize = 500,
                FlushInterval = 2000,  // flush every 2 seconds
                JitterInterval = 500,
            });

            _writeApi.EventHandler += (sender, args) =>
            {
                if (args is WriteErrorEvent errorEvent)
                    Log.Error($"[InfluxDB] Write error: {errorEvent.Exception.Message}");
            };

            // Ensure bucket exists (best-effort on startup)
            _ = EnsureBucketAsync(retentionDays);
        }

        private async Task EnsureBucketAsync(int retentionDays)
        {
            try
            {
                var bucketsApi = _client.GetBucketsApi();
                var orgsApi = _client.GetOrganizationsApi();
                var orgList = await orgsApi.FindOrganizationsAsync(org: _org);
                var orgObj = orgList.FirstOrDefault();
                if (orgObj == null)
                {
                    Log.Error($"[InfluxDB] Organization '{_org}' not found. Buckets must be created manually.");
                    return;
                }

                // ── Raw bucket (full resolution, finite retention) ───────────
                var existing = await bucketsApi.FindBucketByNameAsync(_bucket);
                if (existing == null)
                {
                    var retention = new BucketRetentionRules(
                        type: BucketRetentionRules.TypeEnum.Expire,
                        everySeconds: retentionDays > 0 ? retentionDays * 86400 : 0);
                    await bucketsApi.CreateBucketAsync(_bucket, retention, orgObj.Id);
                    Log.Info($"[InfluxDB] Created bucket '{_bucket}' (retention: {retentionDays}d).");
                }
                else
                {
                    Log.Info($"[InfluxDB] Bucket '{_bucket}' exists.");
                }

                // ── Downsampled bucket (15-min averages, infinite retention) ─
                string dsName = _bucket + "_downsampled";
                var dsExisting = await bucketsApi.FindBucketByNameAsync(dsName);
                if (dsExisting == null)
                {
                    var dsRetention = new BucketRetentionRules(
                        type: BucketRetentionRules.TypeEnum.Expire,
                        everySeconds: 0);  // infinite
                    await bucketsApi.CreateBucketAsync(dsName, dsRetention, orgObj.Id);
                    Log.Info($"[InfluxDB] Created downsampled bucket '{dsName}' (infinite retention).");
                }

                // ── Compaction task (runs daily, downsamples data older than N days) ─
                await EnsureCompactionTaskAsync(orgObj.Id, _compactionAfterDays);
            }
            catch (Exception ex)
            {
                Log.Error($"[InfluxDB] Failed to ensure bucket: {ex.Message}");
            }
        }

        private async Task EnsureCompactionTaskAsync(string orgId, int afterDays)
        {
            try
            {
                var tasksApi = _client.GetTasksApi();
                string taskName = $"compact_{_bucket}";

                // Check if task already exists
                var existing = await tasksApi.FindTasksAsync(name: taskName, orgId: orgId);
                if (existing?.Count > 0)
                {
                    Log.Info($"[InfluxDB] Compaction task '{taskName}' exists.");
                    return;
                }

                // Flux script: downsample to 15-minute windows, write to the downsampled bucket
                string dsName = _bucket + "_downsampled";
                string flux =
                    $"from(bucket: \"{_bucket}\")\n" +
                    $"  |> range(start: -{afterDays + 1}d, stop: -{afterDays}d)\n" +
                    $"  |> filter(fn: (r) => r._measurement == \"data point\")\n" +
                    $"  |> aggregateWindow(every: 15m, fn: mean, createEmpty: false)\n" +
                    $"  |> to(bucket: \"{dsName}\", org: \"{_org}\")";

                var task = await tasksApi.CreateTaskEveryAsync(taskName, flux, "1d", orgId);
                Log.Info($"[InfluxDB] Created compaction task '{taskName}' (every 1d, data older than {afterDays}d → 15m avg).");
            }
            catch (Exception ex)
            {
                Log.Error($"[InfluxDB] Failed to create compaction task: {ex.Message}");
            }
        }

        // ── Write ────────────────────────────────────────────────────────────

        public void Insert(string key, long tsMs, object value)
        {
            var point = BuildPoint(key, tsMs, value);
            _writeApi.WritePoint(point, _bucket, _org);
        }

        /// <summary>Forces a flush of the write buffer to InfluxDB.</summary>
        public void Flush()
        {
            _writeApi?.Flush();
        }

        /// <summary>Insert a batch of key-value pairs with the same timestamp.</summary>
        public void InsertBatch(Dictionary<string, object> values, long? tsMs = null)
        {
            if (values == null || values.Count == 0) return;
            long ts = tsMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var points = values
                .Select(kv => BuildPoint(kv.Key, ts, kv.Value))
                .ToList();

            _writeApi.WritePoints(points, _bucket, _org);
        }

        private static PointData BuildPoint(string key, long tsMs, object value)
        {
            var point = PointData.Measurement("data_point")
                .Tag("key", key)
                .Timestamp(DateTimeOffset.FromUnixTimeMilliseconds(tsMs), WritePrecision.Ms);

            // Store numeric values as floats, everything else as strings
            if (value is double d)
                point = point.Field("value", d);
            else if (value is float f)
                point = point.Field("value", (double)f);
            else if (value is int i)
                point = point.Field("value", (double)i);
            else if (value is long l)
                point = point.Field("value", (double)l);
            else if (value is decimal dec)
                point = point.Field("value", (double)dec);
            else
            {
                // Try to parse as double first (handles "21.5" strings)
                string s = value?.ToString() ?? "";
                if (double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                    point = point.Field("value", parsed);
                else
                    point = point.Field("value_str", s);
            }

            return point;
        }

        // ── Query ────────────────────────────────────────────────────────────

        /// <summary>
        /// Query time-series data for a single key within a time range.
        /// Transparently queries the downsampled bucket for data older than the compaction threshold.
        /// </summary>
        public async Task<List<TsPoint>> QueryAsync(string key, long startTs, long endTs, int limit = 1000, bool descending = false)
        {
            long compactionCutoff = DateTimeOffset.UtcNow.AddDays(-_compactionAfterDays).ToUnixTimeMilliseconds();
            var allPoints = new List<TsPoint>();
            string sortDir = descending ? "desc: true" : "desc: false";

            // Query downsampled bucket for the old portion
            if (startTs < compactionCutoff)
            {
                long dsEnd = Math.Min(endTs, compactionCutoff);
                string dsName = _bucket + "_downsampled";
                var dsFlux = $"""
                    from(bucket: "{dsName}")
                      |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(dsEnd)})
                      |> filter(fn: (r) => r._measurement == "data_point" and r.key == "{EscapeFlux(key)}")
                      |> sort(columns: ["_time"], {sortDir})
                      |> limit(n: {limit})
                    """;
                allPoints.AddRange(await ExecuteQueryAsync(dsFlux));
            }

            // Query raw bucket for the recent portion
            long rawStart = Math.Max(startTs, compactionCutoff);
            if (rawStart < endTs)
            {
                int remaining = Math.Max(1, limit - allPoints.Count);
                var rawFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: {ToInfluxTime(rawStart)}, stop: {ToInfluxTime(endTs)})
                      |> filter(fn: (r) => r._measurement == "data_point" and r.key == "{EscapeFlux(key)}")
                      |> sort(columns: ["_time"], {sortDir})
                      |> limit(n: {remaining})
                    """;
                allPoints.AddRange(await ExecuteQueryAsync(rawFlux));
            }

            if (descending)
                return allPoints.OrderByDescending(p => p.Ts).Take(limit).ToList();

            return allPoints;
        }

        /// <summary>Query time-series data for multiple keys within a time range.
        /// Automatically downsamples via aggregateWindow when the range exceeds ~15 minutes
        /// to keep chart payloads lean (~300 points per series max).</summary>
        public async Task<Dictionary<string, List<TsPoint>>> QueryMultipleAsync(
            List<string> keys, long startTs, long endTs, int maxPointsPerKey = 300)
        {
            var result = new Dictionary<string, List<TsPoint>>();
            if (keys == null || keys.Count == 0) return result;

            var keyFilter = string.Join(" or ",
                keys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            long spanMs = endTs - startTs;
            long windowMs = spanMs / maxPointsPerKey;

            bool downsample = windowMs >= 10_000;
            string aggregatePipeline = "";
            if (downsample)
            {
                string windowDur = FormatFluxDuration(windowMs);
                aggregatePipeline = $"""
                      |> aggregateWindow(every: {windowDur}, fn: mean, createEmpty: false)
                    """;
            }

            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "data_point" and ({keyFilter}))
                  |> filter(fn: (r) => r._field == "value")
                {aggregatePipeline}  |> sort(columns: ["_time"])
                  |> limit(n: {maxPointsPerKey})
                """;

            foreach (var key in keys) result[key] = new List<TsPoint>();

            try
            {
                var queryApi = _client.GetQueryApi();
                var tables = await queryApi.QueryAsync(flux, _org);
                foreach (var table in tables)
                {
                    foreach (var record in table.Records)
                    {
                        string? recordKey = record.GetValueByKey("key")?.ToString();
                        if (recordKey == null || !result.ContainsKey(recordKey)) continue;

                        var time = record.GetTime();
                        long ts = time.HasValue ? time.Value.ToUnixTimeMilliseconds() : 0;
                        string fieldName = record.GetField();
                        var rawValue = record.GetValue();

                        if (fieldName == "value" && rawValue is double dv)
                            result[recordKey].Add(new TsPoint(ts, dv, null));
                        else if (fieldName == "value_str")
                            result[recordKey].Add(new TsPoint(ts, null, rawValue?.ToString()));
                    }
                }
            }
            catch (Exception ex) { Log.Error($"[InfluxDB] Query error: {ex.Message}"); }
            return result;
        }

        /// <summary>
        /// Queries multiple keys and returns a single time-series representing their sum.
        /// If consumptionInterval is provided, it calculates the total consumption (deltas) across all keys.
        /// </summary>
        public async Task<List<TsPoint>> QuerySumAsync(List<string> keys, long startTs, long endTs, string? consumptionInterval = null, int maxPoints = 300)
        {
            if (keys == null || keys.Count == 0) return new List<TsPoint>();

            var keyFilter = string.Join(" or ",
                keys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            long spanMs = endTs - startTs;
            string windowDur = consumptionInterval ?? FormatFluxDuration(Math.Max(1000, spanMs / maxPoints));

            string flux;
            if (consumptionInterval != null)
            {
                // Unified: Sum of consumption (deltas)
                flux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                      |> filter(fn: (r) => r._measurement == "data_point" and ({keyFilter}))
                      |> filter(fn: (r) => r._field == "value")
                      |> difference(nonNegative: true)
                      |> aggregateWindow(every: {windowDur}, fn: sum, createEmpty: true)
                      |> fill(value: 0.0)
                      |> group(columns: ["_time"])
                      |> sum()
                      |> group()
                      |> sort(columns: ["_time"])
                    """;
            }
            else
            {
                // Just sum of latest values
                flux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                      |> filter(fn: (r) => r._measurement == "data_point" and ({keyFilter}))
                      |> filter(fn: (r) => r._field == "value")
                      |> aggregateWindow(every: {windowDur}, fn: mean, createEmpty: true)
                      |> fill(value: 0.0)
                      |> group(columns: ["_time"])
                      |> sum()
                      |> group()
                      |> sort(columns: ["_time"])
                      |> limit(n: {maxPoints})
                    """;
            }

            return await ExecuteQueryAsync(flux);
        }

        /// <summary>
        /// Queries a meter key and calculates consumption (deltas) on-the-fly.
        /// Uses non_negative_difference() to handle counter resets.
        /// </summary>
        public async Task<List<TsPoint>> QueryConsumptionAsync(string key, string interval, long startTs, long endTs, int maxPoints = 300)
        {
            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "data_point" and r.key == "{EscapeFlux(key)}")
                  |> filter(fn: (r) => r._field == "value")
                  |> difference(nonNegative: true)
                  |> aggregateWindow(every: {interval}, fn: sum, createEmpty: false)
                  |> yield(name: "consumption")
                """;

            return await ExecuteQueryAsync(flux);
        }

        /// <summary>Format milliseconds to a clean Flux duration string (e.g., "30s", "5m", "1h").</summary>
        private static string FormatFluxDuration(long ms)
        {
            if (ms >= 3_600_000)
                return $"{Math.Max(1, ms / 3_600_000)}h";
            if (ms >= 60_000)
                return $"{Math.Max(1, ms / 60_000)}m";
            return $"{Math.Max(10, ms / 1000)}s";
        }

        private async Task<List<TsPoint>> ExecuteQueryAsync(string flux)
        {
            var points = new List<TsPoint>();
            try
            {
                var queryApi = _client.GetQueryApi();
                var tables = await queryApi.QueryAsync(flux, _org);

                foreach (var table in tables)
                {
                    foreach (var record in table.Records)
                    {
                        var time = record.GetTime();
                        long ts = time.HasValue ? time.Value.ToUnixTimeMilliseconds() : 0;
                        string fieldName = record.GetField();
                        var rawValue = record.GetValue();

                        if (fieldName == "value" && rawValue is double dv)
                            points.Add(new TsPoint(ts, dv, null));
                        else if (fieldName == "value_str")
                            points.Add(new TsPoint(ts, null, rawValue?.ToString()));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfluxDB] Query error: {ex.Message}");
            }

            return points;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Convert epoch ms to RFC3339 string for Flux queries.</summary>
        private static string ToInfluxTime(long epochMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        /// <summary>Basic escaping for Flux string literals.</summary>
        private static string EscapeFlux(string s) => s.Replace("\"", "\\\"").Replace("\\", "\\\\");

        private DataPointStats? _cachedStats;
        private DateTime _cachedStatsExpiry;

        public async Task<DataPointStats> GetStatsAsync()
        {
            // Cache stats for 5 minutes — the underlying Flux queries scan the entire bucket
            if (_cachedStats != null && DateTime.UtcNow < _cachedStatsExpiry)
                return _cachedStats;

            var stats = new DataPointStats();
            try
            {
                var queryApi = _client.GetQueryApi();

                // 1. Count unique keys (series)
                string keysFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: -30d)
                      |> filter(fn: (r) => r._measurement == "data_point")
                      |> keep(columns: ["key"])
                      |> group()
                      |> distinct(column: "key")
                      |> count()
                    """;
                var keyTables = await queryApi.QueryAsync(keysFlux, _org);
                if (keyTables.Count > 0 && keyTables[0].Records.Count > 0)
                    stats.KeyCount = Convert.ToInt64(keyTables[0].Records[0].GetValue());

                // 2. Approximate total points (last 30 days to keep query fast)
                string pointsFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: -30d)
                      |> filter(fn: (r) => r._measurement == "data_point" and r._field == "value")
                      |> count()
                      |> group()
                      |> sum()
                    """;
                var pointTables = await queryApi.QueryAsync(pointsFlux, _org);
                if (pointTables.Count > 0 && pointTables[0].Records.Count > 0)
                    stats.PointCount = Convert.ToInt64(pointTables[0].Records[0].GetValue());

                // 3. Try to get disk size from _internal bucket (optional, may not exist in InfluxDB 2.x)
                try
                {
                    string diskFlux = """
                        from(bucket: "_internal") 
                          |> range(start: -2m) 
                          |> filter(fn: (r) => r._measurement == "storage_shard_disk_size") 
                          |> last() 
                          |> group() 
                          |> sum()
                        """;
                    var diskTables = await queryApi.QueryAsync(diskFlux, _org);
                    if (diskTables.Count > 0 && diskTables[0].Records.Count > 0)
                        stats.DiskSizeBytes = Convert.ToInt64(diskTables[0].Records[0].GetValue());
                }
                catch { /* ignore if _internal bucket is not enabled */ }
            }
            catch (Exception ex)
            {
                Log.Error($"[InfluxDB] Stats error: {ex.Message}");
            }

            _cachedStats = stats;
            _cachedStatsExpiry = DateTime.UtcNow.AddMinutes(5);
            return stats;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writeApi?.Dispose();
            _client?.Dispose();
        }
    }

    /// <summary>A single time-series data point returned from queries.</summary>
    public record TsPoint(
        [property: JsonPropertyName("ts")] long Ts,
        [property: JsonPropertyName("value")] double? Value,
        [property: JsonPropertyName("valueStr")] string? ValueStr);

    public class DataPointStats
    {
        [JsonPropertyName("keyCount")] public long KeyCount { get; set; }
        [JsonPropertyName("pointCount")] public long PointCount { get; set; }
        [JsonPropertyName("diskSizeBytes")] public long DiskSizeBytes { get; set; }
    }
}
