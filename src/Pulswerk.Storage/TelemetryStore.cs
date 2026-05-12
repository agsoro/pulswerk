// TelemetryStore.cs – InfluxDB-backed time-series persistence
//
//  Stores all telemetry data points (BACnet + Modbus) in InfluxDB 2.x.
//  Provides query methods for the dashboard (trend charts, widget data).
//
//  InfluxDB data model:
//    Measurement: "telemetry"
//    Tag:   key   (the telemetry key, e.g. "dev10_ai_1_value")
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
    public sealed class TelemetryStore : IDisposable
    {
        private readonly InfluxDBClient _client;
        private readonly WriteApi _writeApi;
        private readonly string _bucket;
        private readonly string _org;
        private readonly int _compactionAfterDays;
        private bool _disposed;

        /// <summary>
        /// Creates a new TelemetryStore backed by InfluxDB 2.x.
        /// Auto-creates buckets and compaction task if they don't exist.
        /// </summary>
        /// <param name="url">InfluxDB URL, e.g. "http://localhost:8086"</param>
        /// <param name="token">Admin or write token</param>
        /// <param name="org">InfluxDB organization name</param>
        /// <param name="bucket">Bucket name for telemetry data</param>
        /// <param name="retentionDays">Data retention in days (0 = infinite)</param>
        /// <param name="compactionAfterDays">Downsample to 15-min intervals after this many days</param>
        public TelemetryStore(string url, string token, string org, string bucket,
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
                    Console.Error.WriteLine($"  [InfluxDB] Write error: {errorEvent.Exception.Message}");
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
                    Console.Error.WriteLine($"  [InfluxDB] Organization '{_org}' not found. Buckets must be created manually.");
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
                    Console.WriteLine($"  [InfluxDB] Created bucket '{_bucket}' (retention: {retentionDays}d).");
                }
                else
                {
                    Console.WriteLine($"  [InfluxDB] Bucket '{_bucket}' exists.");
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
                    Console.WriteLine($"  [InfluxDB] Created downsampled bucket '{dsName}' (infinite retention).");
                }

                // ── Compaction task (runs daily, downsamples data older than N days) ─
                await EnsureCompactionTaskAsync(orgObj.Id, _compactionAfterDays);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [InfluxDB] Failed to ensure bucket: {ex.Message}");
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
                    Console.WriteLine($"  [InfluxDB] Compaction task '{taskName}' exists.");
                    return;
                }

                // Flux script: downsample to 15-minute windows, write to the downsampled bucket
                string dsName = _bucket + "_downsampled";
                string flux =
                    $"from(bucket: \"{_bucket}\")\n" +
                    $"  |> range(start: -{afterDays + 1}d, stop: -{afterDays}d)\n" +
                    $"  |> filter(fn: (r) => r._measurement == \"telemetry\")\n" +
                    $"  |> aggregateWindow(every: 15m, fn: mean, createEmpty: false)\n" +
                    $"  |> to(bucket: \"{dsName}\", org: \"{_org}\")";

                var task = await tasksApi.CreateTaskEveryAsync(taskName, flux, "1d", orgId);
                Console.WriteLine($"  [InfluxDB] Created compaction task '{taskName}' (every 1d, data older than {afterDays}d → 15m avg).");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [InfluxDB] Failed to create compaction task: {ex.Message}");
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
            var point = PointData.Measurement("telemetry")
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
                      |> filter(fn: (r) => r._measurement == "telemetry" and r.key == "{EscapeFlux(key)}")
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
                      |> filter(fn: (r) => r._measurement == "telemetry" and r.key == "{EscapeFlux(key)}")
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

            // Build a Flux OR filter for all keys
            var keyFilter = string.Join(" or ",
                keys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            // ── Adaptive downsampling ──────────────────────────────────────
            // Compute a sensible aggregateWindow size so we get at most ~maxPointsPerKey
            // points per series.  For short ranges we skip aggregation entirely.
            long spanMs = endTs - startTs;
            long windowMs = spanMs / maxPointsPerKey;  // target interval

            // Only aggregate when window would be ≥ 10 seconds (i.e. span > ~50 min)
            bool downsample = windowMs >= 10_000;
            string aggregatePipeline = "";
            if (downsample)
            {
                // Round window to a clean Flux duration
                string windowDur = FormatFluxDuration(windowMs);
                aggregatePipeline = $"""
                      |> aggregateWindow(every: {windowDur}, fn: mean, createEmpty: false)
                    """;
            }

            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "telemetry" and ({keyFilter}))
                  |> filter(fn: (r) => r._field == "value")
                {aggregatePipeline}  |> sort(columns: ["_time"])
                  |> limit(n: {maxPointsPerKey})
                """;

            // Initialize result dict
            foreach (var key in keys)
                result[key] = new List<TsPoint>();

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
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  [InfluxDB] Query error: {ex.Message}");
            }

            return result;
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
                Console.Error.WriteLine($"  [InfluxDB] Query error: {ex.Message}");
            }

            return points;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>Convert epoch ms to RFC3339 string for Flux queries.</summary>
        private static string ToInfluxTime(long epochMs)
            => DateTimeOffset.FromUnixTimeMilliseconds(epochMs).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        /// <summary>Basic escaping for Flux string literals.</summary>
        private static string EscapeFlux(string s) => s.Replace("\"", "\\\"").Replace("\\", "\\\\");

        public async Task<TelemetryStats> GetStatsAsync()
        {
            var stats = new TelemetryStats();
            try
            {
                var queryApi = _client.GetQueryApi();

                // 1. Count unique keys (series)
                string keysFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: 0)
                      |> filter(fn: (r) => r._measurement == "telemetry")
                      |> keep(columns: ["key"])
                      |> group()
                      |> distinct(column: "key")
                      |> count()
                    """;
                var keyTables = await queryApi.QueryAsync(keysFlux, _org);
                if (keyTables.Count > 0 && keyTables[0].Records.Count > 0)
                    stats.KeyCount = Convert.ToInt64(keyTables[0].Records[0].GetValue());

                // 2. Count total points (approximate if range is large, but let's try)
                string pointsFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: 0)
                      |> filter(fn: (r) => r._measurement == "telemetry" and r._field == "value")
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
                Console.Error.WriteLine($"  [InfluxDB] Stats error: {ex.Message}");
            }
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

    public class TelemetryStats
    {
        [JsonPropertyName("keyCount")] public long KeyCount { get; set; }
        [JsonPropertyName("pointCount")] public long PointCount { get; set; }
        [JsonPropertyName("diskSizeBytes")] public long DiskSizeBytes { get; set; }
    }
}
