// TelemetryStore.cs – InfluxDB-backed time-series persistence
//
//  Stores all data points (BACnet + Modbus) in InfluxDB 2.x.
//  Provides query methods for the dashboard (trend charts, widget data).
//
//  InfluxDB data model:
//    Measurement: "telemetry"
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
    public class TelemetryStore : IDisposable
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
        /// <param name="bucket">Bucket name for data</param>
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

        public virtual void Insert(string key, long tsMs, object value)
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
        public virtual void InsertBatch(Dictionary<string, object> values, long? tsMs = null)
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
        public virtual async Task<List<TsPoint>> QueryAsync(string key, long startTs, long endTs, int limit = 1000, bool descending = false)
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
        public virtual async Task<Dictionary<string, List<TsPoint>>> QueryMultipleAsync(
            List<string> keys, long startTs, long endTs, string? granularity = null, string? mode = null, int maxPointsPerKey = 300)
        {
            var result = new Dictionary<string, List<TsPoint>>();
            if (keys == null || keys.Count == 0) return result;

            var keyFilter = string.Join(" or ",
                keys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            string aggregatePipeline = "";
            if (!string.IsNullOrEmpty(granularity))
            {
                string every = granularity switch
                {
                    "hour" => "1h",
                    "day" => "1d",
                    "month" => "1mo",
                    "year" => "1y",
                    _ => "1h"
                };

                if (mode == "diff")
                {
                    aggregatePipeline = $"""
                          |> difference(nonNegative: true)
                          |> aggregateWindow(every: {every}, fn: sum, timeSrc: "_start", createEmpty: false)
                        """;
                }
                else if (mode == "max")
                {
                    aggregatePipeline = $"""
                          |> aggregateWindow(every: {every}, fn: max, createEmpty: false)
                        """;
                }
                else
                {
                    aggregatePipeline = $"""
                          |> aggregateWindow(every: {every}, fn: mean, createEmpty: false)
                        """;
                }
            }
            else
            {
                long spanMs = endTs - startTs;
                long windowMs = spanMs / maxPointsPerKey;

                bool downsample = windowMs >= 10_000;
                if (downsample)
                {
                    string windowDur = FormatFluxDuration(windowMs);
                    aggregatePipeline = $"""
                          |> aggregateWindow(every: {windowDur}, fn: mean, createEmpty: false)
                        """;
                }
            }

            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "telemetry" and ({keyFilter}))
                  |> filter(fn: (r) => r._field == "value")
                {aggregatePipeline}  |> sort(columns: ["_time"])
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

            // Enum / boolean telemetry (e.g. KNX DPT 1.xxx state labels, or any other textual
            // state) is stored in the "value_str" field and is therefore excluded by the
            // numeric "value" filter above, leaving those series with no chartable points.
            // Pull those string points separately and categorically encode each distinct label
            // to a numeric ordinal so they render as a step series. The original label is kept
            // in ValueStr so the frontend can label the Y axis and tooltip.
            await MergeStringPointsAsync(keys, startTs, endTs, result);

            return result;
        }

        /// <summary>
        /// Queries the "value_str" field for the given keys and, for any series that did not
        /// already yield numeric points, categorically encodes the distinct string states into
        /// numeric ordinals so they can be charted. Recognized boolean states keep a stable
        /// 0/1 polarity (e.g. "off" → 0, "on" → 1); arbitrary enum labels are assigned ordinals
        /// in sorted order for determinism. Each returned <see cref="TsPoint"/> carries both the
        /// numeric ordinal (<c>Value</c>) and the original label (<c>ValueStr</c>).
        /// </summary>
        private async Task MergeStringPointsAsync(
            List<string> keys, long startTs, long endTs, Dictionary<string, List<TsPoint>> result)
        {
            // Only consider keys that produced no numeric points; numeric series take priority.
            var stringKeys = keys
                .Where(k => result.TryGetValue(k, out var pts) && pts.Count == 0)
                .ToList();
            if (stringKeys.Count == 0) return;

            var keyFilter = string.Join(" or ",
                stringKeys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "telemetry" and ({keyFilter}))
                  |> filter(fn: (r) => r._field == "value_str")
                  |> sort(columns: ["_time"])
                """;

            // Collect raw (ts, label) pairs per key first; ordinals are assigned afterwards
            // once all distinct labels for a key are known.
            var rawByKey = new Dictionary<string, List<(long Ts, string Label)>>();
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

                        string? label = record.GetValue()?.ToString();
                        if (string.IsNullOrEmpty(label)) continue;

                        var time = record.GetTime();
                        long ts = time.HasValue ? time.Value.ToUnixTimeMilliseconds() : 0;

                        if (!rawByKey.TryGetValue(recordKey, out var list))
                            rawByKey[recordKey] = list = new List<(long, string)>();
                        list.Add((ts, label));
                    }
                }
            }
            catch (Exception ex) { Log.Error($"[InfluxDB] String query error: {ex.Message}"); }

            foreach (var (key, points) in rawByKey)
            {
                if (points.Count == 0) continue;
                var encoding = BuildCategoryEncoding(points.Select(p => p.Label));
                foreach (var (ts, label) in points)
                {
                    if (encoding.TryGetValue(label, out double ordinal))
                        result[key].Add(new TsPoint(ts, ordinal, label));
                }
            }
        }

        /// <summary>
        /// Builds a deterministic label → numeric-ordinal map for a set of string states.
        /// If every distinct label is a recognized boolean state, the canonical 0/1 polarity is
        /// used (so "on"/"off" always map to 1/0 regardless of which appears first). Otherwise
        /// the distinct labels are sorted and assigned 0,1,2,… so the encoding is stable across
        /// queries and time ranges.
        /// </summary>
        private static Dictionary<string, double> BuildCategoryEncoding(IEnumerable<string> labels)
        {
            var distinct = labels
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => l.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Boolean fast-path: keep stable 0/1 polarity when all labels are boolean-ish.
            if (distinct.Count <= 2 && distinct.All(l => MapBooleanLabel(l) != null))
            {
                var boolMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in distinct)
                    boolMap[l] = MapBooleanLabel(l)!.Value;
                return boolMap;
            }

            // General enum encoding: sorted distinct labels → 0,1,2,…
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int idx = 0;
            foreach (var l in distinct.OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
                map[l] = idx++;
            return map;
        }

        // Recognized KNX DPT 1.xxx state labels (stored lower-cased) plus generic spellings.
        // "One" labels map to 1, "Zero" labels map to 0. Kept here (rather than referencing the
        // KNX driver) so the storage layer stays dependency-free.
        // Each complementary pair must map to *opposite* numbers so the step chart can
        // distinguish the two states; the exact polarity is cosmetic. Note "open"/"close" is
        // intentionally absent because its polarity is DPT-dependent (1.009 vs 1.019) and the
        // storage layer has no DPT context — those still chart via the "closed" label below.
        private static readonly HashSet<string> _boolTrueLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "1", "true", "on", "yes", "high", "active", "down", "enable",
            "alarm", "start", "increase", "ramp", "inverted", "calculated", "reset",
            "acknowledge", "occupied", "and", "scene b", "night", "heating", "open"
        };
        private static readonly HashSet<string> _boolFalseLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            "0", "false", "off", "no", "low", "inactive", "closed", "close", "up", "stop", "disable",
            "no alarm", "no action", "decrease", "no ramp", "not inverted", "fixed",
            "not occupied", "or", "scene a", "day", "cooling"
        };

        /// <summary>
        /// Maps a stored string state label to a numeric 0/1, or null when the label is not a
        /// recognized boolean state and therefore cannot be charted numerically.
        /// </summary>
        private static double? MapBooleanLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            string s = label.Trim();
            if (_boolTrueLabels.Contains(s)) return 1.0;
            if (_boolFalseLabels.Contains(s)) return 0.0;
            return null;
        }

        /// <summary>
        /// Queries multiple keys and returns a single time-series representing their sum.
        /// If isConsumption is true, it calculates the total consumption (deltas) across all keys.
        /// </summary>
        public virtual async Task<List<TsPoint>> QuerySumAsync(List<string> keys, long startTs, long endTs, string? interval = null, bool isConsumption = false, int maxPoints = 300)
        {
            if (keys == null || keys.Count == 0) return new List<TsPoint>();

            var keyFilter = string.Join(" or ",
                keys.Select(k => $"r.key == \"{EscapeFlux(k)}\""));

            long spanMs = endTs - startTs;
            string windowDur = interval ?? FormatFluxDuration(Math.Max(1000, spanMs / maxPoints));

            string flux;
            if (isConsumption)
            {
                // Unified: Sum of consumption (deltas)
                flux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                      |> filter(fn: (r) => r._measurement == "telemetry" and ({keyFilter}))
                      |> filter(fn: (r) => r._field == "value")
                      |> difference(nonNegative: true)
                      |> aggregateWindow(every: {windowDur}, fn: sum, timeSrc: "_start", createEmpty: true)
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
                      |> filter(fn: (r) => r._measurement == "telemetry" and ({keyFilter}))
                      |> filter(fn: (r) => r._field == "value")
                      |> aggregateWindow(every: {windowDur}, fn: mean, createEmpty: true)
                      |> fill(value: 0.0)
                      |> group(columns: ["_time"])
                      |> sum()
                      |> group()
                      |> sort(columns: ["_time"])
                    """;
            }

            return await ExecuteQueryAsync(flux);
        }

        /// <summary>
        /// Queries a meter key and calculates consumption (deltas) on-the-fly.
        /// Uses non_negative_difference() to handle counter resets.
        /// </summary>
        public virtual async Task<List<TsPoint>> QueryConsumptionAsync(string key, string interval, long startTs, long endTs, int maxPoints = 300)
        {
            var flux = $"""
                from(bucket: "{_bucket}")
                  |> range(start: {ToInfluxTime(startTs)}, stop: {ToInfluxTime(endTs)})
                  |> filter(fn: (r) => r._measurement == "telemetry" and r.key == "{EscapeFlux(key)}")
                  |> filter(fn: (r) => r._field == "value")
                  |> difference(nonNegative: true)
                  |> aggregateWindow(every: {interval}, fn: sum, timeSrc: "_start", createEmpty: false)
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

        private TelemetryStats? _cachedStats;
        private DateTime _cachedStatsExpiry;

        public async Task<TelemetryStats> GetStatsAsync()
        {
            // Cache stats for 5 minutes — the underlying Flux queries scan the entire bucket
            if (_cachedStats != null && DateTime.UtcNow < _cachedStatsExpiry)
                return _cachedStats;

            var stats = new TelemetryStats();
            try
            {
                var queryApi = _client.GetQueryApi();

                // 1. Count unique keys (series)
                string keysFlux = $"""
                    from(bucket: "{_bucket}")
                      |> range(start: -30d)
                      |> filter(fn: (r) => r._measurement == "telemetry")
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
                Log.Error($"[InfluxDB] Stats error: {ex.Message}");
            }

            _cachedStats = stats;
            _cachedStatsExpiry = DateTime.UtcNow.AddMinutes(5);
            return stats;
        }

        /// <summary>
        /// Deletes telemetry points for a specific key within a time range.
        /// </summary>
        public virtual async Task DeleteAsync(string key, long startTs, long endTs)
        {
            try
            {
                await Task.Run(() =>
                {
                    var start = DateTimeOffset.FromUnixTimeMilliseconds(startTs).UtcDateTime;
                    var stop = DateTimeOffset.FromUnixTimeMilliseconds(endTs).UtcDateTime;
                    var predicate = $"_measurement=\"telemetry\" AND key=\"{EscapeFlux(key)}\"";

                    var deleteApi = _client.GetDeleteApi();
                    deleteApi.Delete(start, stop, predicate, _bucket, _org);
                    deleteApi.Delete(start, stop, predicate, _bucket + "_downsampled", _org);
                    Log.Info($"[InfluxDB] Deleted points for key '{key}' from {start:o} to {stop:o}.");
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[InfluxDB] Delete error: {ex.Message}");
                throw;
            }
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
