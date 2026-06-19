// Services/TelemetryService.cs
// UpdateTelemetries, RecordUpdate, GetUpdatesPerMinute, GetAvailableTelemetries,
// GetCurrentValues, GetTelemetryHistoryAsync, GetTelemetryHistoryForWidgetAsync,
// GetVirtualTelemetryHistoryAsync, GetLiveValueForFormula, BuildAvailableTelemetriesInternal.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;

namespace Pulswerk.Dashboard
{
    public partial class DashboardDataService
    {
        // ── Telemetry push / stats ────────────────────────────────────────────

        public Dictionary<string, (double val, DateTime ts)> UpdateTelemetries(
            Dictionary<string, object> values, bool isPush = false)
        {
            if (values == null) return new Dictionary<string, (double val, DateTime ts)>();

            var persistedResults = new Dictionary<string, (double val, DateTime ts)>();
            var changedValues = new Dictionary<string, string>();
            DateTime now = DateTime.UtcNow;

            lock (LatestValues)
            {
                foreach (var kvp in values)
                {
                    var vs = kvp.Value?.ToString();
                    if (vs != null && vs.Contains("ERROR_")) continue;
                    if (kvp.Value is null) continue;
                    LatestValues[kvp.Key] = kvp.Value;
                    LatestTimestamps[kvp.Key] = now;
                    changedValues[kvp.Key] = vs ?? "---";

                    if (TryToDouble(kvp.Value, out double d))
                    {
                        persistedResults[kvp.Key] = (d, now);

                        var (live, persisted) = _calc.Process(kvp.Key, kvp.Value, now);
                        foreach (var l in live)
                        {
                            LatestValues[l.Key] = l.Value!;
                            LatestTimestamps[l.Key] = now;
                            changedValues[l.Key] = l.Value?.ToString() ?? "---";
                        }
                        foreach (var p in persisted)
                            persistedResults[p.Key] = p.Value;
                    }
                }

                // Evaluate virtual telemetries using updated LatestValues
                foreach (var vt in _virtualTelemetries)
                {
                    try
                    {
                        var valStr = GetLiveValueForFormula(vt.Formula, vt.Units, vt.Device);
                        LatestValues.TryGetValue(vt.Key, out var oldVal);
                        if (valStr != oldVal?.ToString())
                        {
                            LatestValues[vt.Key] = valStr;
                            LatestTimestamps[vt.Key] = now;
                            changedValues[vt.Key] = valStr;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[Dashboard] Error evaluating virtual telemetry {vt.Key}: {ex.Message}");
                    }
                }
            }

            RecordUpdate(values.Count, isPush);
            if (changedValues.Count > 0)
                OnTelemetriesUpdated?.Invoke(changedValues);

            return persistedResults;
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

        // ── Available telemetry list (cached) ─────────────────────────────────

        /// <summary>
        /// Returns all available data point keys with metadata for the dashboard widget key picker.
        /// Uses a 10-second in-memory cache to prevent O(N²) tree-building on every call.
        /// </summary>
        public List<AvailableTelemetryDto> GetAvailableTelemetries(bool includeLiveValues = false)
        {
            List<AvailableTelemetryDto> baseList;
            lock (_cacheLock)
            {
                if (_cachedTelemetries == null || (DateTime.UtcNow - _cacheTimestamp).TotalSeconds > 10)
                {
                    _cachedTelemetries = BuildAvailableTelemetriesInternal(false);
                    _cacheTimestamp = DateTime.UtcNow;
                }
                baseList = _cachedTelemetries;
            }

            return baseList.Select(item =>
            {
                var clone = new AvailableTelemetryDto
                {
                    Key = item.Key,
                    Name = item.Name,
                    FullName = item.FullName,
                    Units = item.Units,
                    Type = item.Type,
                    Path = item.Path,
                    ParentId = item.ParentId,
                    ParentPath = item.ParentPath,
                    Device = item.Device,
                    Connection = item.Connection,
                    IsWritable = item.IsWritable,
                    EnumValues = item.EnumValues
                };

                if (item.Type == "Calculated")
                {
                    if (includeLiveValues)
                    {
                        string? cachedVal = null;
                        lock (LatestValues)
                        {
                            if (LatestValues.TryGetValue(item.Key, out var v))
                                cachedVal = v?.ToString();
                        }

                        if (cachedVal != null)
                        {
                            clone.Value = cachedVal;
                            clone.LastUpdate = "Live";
                            return clone;
                        }

                        var device = IdentifyDeviceFromTelemetryKey(item.Key);
                        if (device?.Telemetries != null)
                        {
                            string pointKey = item.Key.Substring(device.Id.Length + 1);
                            var dp = device.Telemetries.FirstOrDefault(t => t.Id == pointKey);
                            if (dp != null && !string.IsNullOrWhiteSpace(dp.Formula))
                            {
                                var computed = GetLiveValueForFormula(dp.Formula, dp.Units, device, baseList);
                                lock (LatestValues)
                                {
                                    LatestValues[item.Key] = computed;
                                    LatestTimestamps[item.Key] = DateTime.UtcNow;
                                }
                                clone.Value = computed;
                                clone.LastUpdate = "Live";
                                return clone;
                            }
                        }
                    }
                    clone.Value = "---";
                    clone.LastUpdate = "Live";
                }
                else
                {
                    clone.Value = GetLatestValue(item.Key);
                    clone.LastUpdate = FormatLastUpdate(item.Key);
                }

                return clone;
            }).ToList();
        }

        private List<AvailableTelemetryDto> BuildAvailableTelemetriesInternal(bool includeLiveValues = false)
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

        // ── Current values ────────────────────────────────────────────────────

        /// <summary>
        /// Gets current values for a list of data point keys. Used by latest-values and single-value widgets.
        /// </summary>
        public Dictionary<string, string> GetCurrentValues(List<string> keys)
        {
            var result = new Dictionary<string, string>();
            lock (LatestValues)
            {
                List<AvailableTelemetryDto>? allKeys = null;
                foreach (var key in keys)
                {
                    if (LatestValues.TryGetValue(key, out var val))
                    {
                        result[key] = val?.ToString() ?? "---";
                        continue;
                    }

                    var device = IdentifyDeviceFromTelemetryKey(key);
                    if (device?.DeviceType == "virtual" && device.Telemetries != null)
                    {
                        string pointKey = key.Substring(device.Id.Length + 1);
                        var dp = device.Telemetries.FirstOrDefault(t => t.Id == pointKey);
                        if (dp != null && !string.IsNullOrWhiteSpace(dp.Formula))
                        {
                            allKeys ??= GetAvailableTelemetries();
                            var computed = GetLiveValueForFormula(dp.Formula, dp.Units, device, allKeys);
                            LatestValues[key] = computed;
                            LatestTimestamps[key] = DateTime.UtcNow;
                            result[key] = computed;
                            continue;
                        }
                    }

                    if (key.Contains(":consumption:") ||
                        !System.Text.RegularExpressions.Regex.IsMatch(key, @"^[a-zA-Z0-9_\-\.:]+$"))
                    {
                        allKeys ??= GetAvailableTelemetries();
                        var computed = GetLiveValueForFormula(key, null, null, allKeys);
                        LatestValues[key] = computed;
                        LatestTimestamps[key] = DateTime.UtcNow;
                        result[key] = computed;
                        continue;
                    }

                    result[key] = "---";
                }
            }
            return result;
        }

        // ── History queries ───────────────────────────────────────────────────

        /// <summary>
        /// Returns data point history from InfluxDB for a single key within a specific time range.
        /// Intercepts virtual/formula keys and calculated consumption keys.
        /// </summary>
        public async Task<List<TsPoint>> GetTelemetryHistoryAsync(string key, long startTs, long endTs)
        {
            Log.Debug($"[Dashboard] History requested: {key}, range={startTs} to {endTs}");

            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device?.DeviceType == "virtual")
            {
                string pointKey = key.Substring(device.Id.Length + 1);
                var point = device.Telemetries?.FirstOrDefault(t => t.Id == pointKey);
                if (point != null && !string.IsNullOrWhiteSpace(point.Formula))
                    return await GetVirtualTelemetryHistoryAsync(point.Formula, device, startTs, endTs);
            }

            if (key.EndsWith("_hourly") || key.EndsWith("_daily") ||
                key.EndsWith("_monthly") || key.EndsWith("_yearly"))
            {
                string interval = key.Substring(key.LastIndexOf('_') + 1) switch
                {
                    "hourly" => "1h",
                    "daily" => "1d",
                    "monthly" => "1m",
                    "yearly" => "1y",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(interval))
                {
                    string baseKey = key.Substring(0, key.LastIndexOf('_'));
                    return await GetConsumptionHistoryAsync(baseKey, interval, key, startTs, endTs);
                }
            }

            return await DataStore.QueryAsync(key, startTs, endTs, limit: 5000);
        }

        /// <summary>
        /// Returns data point history looking back a number of days from now.
        /// </summary>
        public async Task<List<TsPoint>> GetTelemetryHistoryAsync(string key, double days = 1.0)
        {
            long endTs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000;
            long startTs = endTs - (long)(days * 24 * 60 * 60 * 1000.0) - 5000;
            return await GetTelemetryHistoryAsync(key, startTs, endTs);
        }

        /// <summary>
        /// Fetches data point history for multiple keys within a time range.
        /// Supports virtual keys: "key:consumption:1h" or "pathsum(...):consumption:1d".
        /// </summary>
        public async Task<Dictionary<string, List<TsPoint>>> GetTelemetryHistoryForWidgetAsync(
            List<string> telemetryKeys, long startTs, long endTs, string? barGranularity = null, string? barMode = null)
        {
            if (!string.IsNullOrEmpty(barGranularity))
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(startTs);
                if (barGranularity == "hour")
                {
                    var aligned = new DateTimeOffset(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Offset);
                    startTs = aligned.ToUnixTimeMilliseconds();
                }
                else if (barGranularity == "day")
                {
                    var aligned = new DateTimeOffset(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Offset);
                    startTs = aligned.ToUnixTimeMilliseconds();
                }
                else if (barGranularity == "month")
                {
                    var aligned = new DateTimeOffset(dt.Year, dt.Month, 1, 0, 0, 0, dt.Offset);
                    startTs = aligned.ToUnixTimeMilliseconds();
                }
                else if (barGranularity == "year")
                {
                    var aligned = new DateTimeOffset(dt.Year, 1, 1, 0, 0, 0, dt.Offset);
                    startTs = aligned.ToUnixTimeMilliseconds();
                }
            }
            var result = new Dictionary<string, List<TsPoint>>();
            var realKeys = new List<string>();
            var realKeyMap = new Dictionary<string, string>();
            List<AvailableTelemetryDto>? allKeys = null;

            foreach (var key in telemetryKeys)
            {
                string keyWithoutModifier = key;
                string? extraModifier = null;
                if (key.Contains(":consumption:"))
                {
                    var parts = key.Split(":consumption:");
                    keyWithoutModifier = parts[0];
                    extraModifier = parts[1];
                }

                var vdev = Config.Devices.FirstOrDefault(d =>
                    keyWithoutModifier.StartsWith(d.Id + "_") &&
                    d.Telemetries != null &&
                    d.Telemetries.Any(p => p.Id == keyWithoutModifier.Substring(d.Id.Length + 1)));

                string effectiveKey = keyWithoutModifier;
                if (vdev?.Telemetries != null)
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
                    allKeys ??= GetAvailableTelemetries();
                    result[key] = consumptionInterval != null
                        ? await GetConsumptionHistoryAsync(baseKey, consumptionInterval, key, startTs, endTs, allKeys, isConsumption: true)
                        : await GetConsumptionHistoryAsync(baseKey, "5m", key, startTs, endTs, allKeys, isConsumption: false);
                }
                else if (consumptionInterval != null)
                {
                    string suffix = consumptionInterval switch
                    { "1h" => "_hourly", "1d" => "_daily", "1m" => "_monthly", "1y" => "_yearly", _ => "" };
                    result[key] = await GetConsumptionHistoryAsync(
                        baseKey, consumptionInterval, baseKey + suffix, startTs, endTs, allKeys, isConsumption: true);
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(baseKey, @"^[a-zA-Z0-9_\-\.:]+$"))
                {
                    allKeys ??= GetAvailableTelemetries();
                    result[key] = await GetVirtualTelemetryHistoryAsync(baseKey, vdev!, startTs, endTs, allKeys);
                }
                else
                {
                    realKeys.Add(effectiveKey);
                    realKeyMap[key] = effectiveKey;
                }
            }

            if (realKeys.Count > 0)
            {
                // Pre-warm calculated keys
                foreach (var rk in realKeys)
                {
                    if (rk.EndsWith("_hourly") || rk.EndsWith("_daily") ||
                        rk.EndsWith("_monthly") || rk.EndsWith("_yearly"))
                    {
                        string rkInterval = rk.Substring(rk.LastIndexOf('_') + 1) switch
                        { "hourly" => "1h", "daily" => "1d", "monthly" => "1m", "yearly" => "1y", _ => "" };
                        if (!string.IsNullOrEmpty(rkInterval))
                            await GetConsumptionHistoryAsync(
                                rk.Substring(0, rk.LastIndexOf('_')), rkInterval, rk, startTs, endTs, allKeys);
                    }
                }

                var realData = await DataStore.QueryMultipleAsync(realKeys, startTs, endTs, barGranularity, barMode);
                foreach (var entry in realKeyMap)
                {
                    if (realData.TryGetValue(entry.Value, out var data))
                        result[entry.Key] = data;
                }
            }

            return result;
        }

        // ── Virtual history helper ────────────────────────────────────────────

        private async Task<List<TsPoint>> GetVirtualTelemetryHistoryAsync(
            string formula, DeviceConfig device, long startTs, long endTs,
            List<AvailableTelemetryDto>? allKeys = null)
        {
            formula = ExpandFormula(formula, device);

            if (formula.Contains(":consumption:"))
            {
                var parts = formula.Split(":consumption:");
                var baseKey = parts[0];
                var interval = parts[1];
                string suffix = interval switch { "1h" => "_hourly", "1d" => "_daily", "1m" => "_monthly", "1y" => "_yearly", _ => "" };
                string persistedKey = baseKey + suffix;
                if (baseKey.StartsWith("pathsum("))
                {
                    var point = device.Telemetries?.FirstOrDefault(t => ExpandFormula(t.Formula, device) == formula);
                    if (point != null) persistedKey = $"{device.Id}_{point.Id}";
                }
                return await GetConsumptionHistoryAsync(baseKey, interval, persistedKey, startTs, endTs, allKeys, isConsumption: true);
            }

            if (formula.StartsWith("pathsum("))
            {
                var point = device.Telemetries?.FirstOrDefault(t => ExpandFormula(t.Formula, device) == formula);
                string persistedKey = point != null ? $"{device.Id}_{point.Id}" : "pathsum_history";
                return await GetConsumptionHistoryAsync(formula, "5m", persistedKey, startTs, endTs, allKeys, isConsumption: false);
            }

            var keys = new HashSet<string>();
            bool hasBrackets = false;
            System.Text.RegularExpressions.Regex.Replace(formula, @"\[([^\]]+)\]", match =>
            {
                hasBrackets = true;
                keys.Add(match.Groups[1].Value);
                return "";
            });

            if (!hasBrackets && (formula.Contains("+") || formula.Contains("-") ||
                formula.Contains("*") || formula.Contains("/")))
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

            var keyList = keys.ToList();
            if (keyList.Count == 0) return new List<TsPoint>();

            // Pre-warm calculated keys in keyList
            foreach (var k in keyList)
            {
                if (k.EndsWith("_hourly") || k.EndsWith("_daily") ||
                    k.EndsWith("_monthly") || k.EndsWith("_yearly"))
                {
                    string kInterval = k.Substring(k.LastIndexOf('_') + 1) switch
                    { "hourly" => "1h", "daily" => "1d", "monthly" => "1m", "yearly" => "1y", _ => "" };
                    if (!string.IsNullOrEmpty(kInterval))
                        await GetConsumptionHistoryAsync(
                            k.Substring(0, k.LastIndexOf('_')), kInterval, k, startTs, endTs, allKeys);
                }
            }

            if (keyList.Count == 1 && (formula == keyList[0] || formula == $"[{keyList[0]}]"))
                return await DataStore.QueryAsync(keyList[0], startTs, endTs, limit: 5000);

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

            using var dt = new DataTable();

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
                    if (formula.StartsWith("pathsum("))
                    {
                        double sum = 0;
                        foreach (var k in keyList) if (lastKnownValues.TryGetValue(k, out double v)) sum += v;
                        mergedPoints.Add(new TsPoint(ts, sum, null));
                        continue;
                    }

                    if (formula.Contains(":consumption:"))
                    {
                        string ck = keyList.First();
                        if (lastKnownValues.TryGetValue(ck, out double cv))
                            mergedPoints.Add(new TsPoint(ts, cv, null));
                        continue;
                    }

                    string expr = formula;
                    bool hasMathExpr = false;
                    expr = System.Text.RegularExpressions.Regex.Replace(expr, @"\[([^\]]+)\]", match =>
                    {
                        hasMathExpr = true;
                        string k = match.Groups[1].Value;
                        if (lastKnownValues.TryGetValue(k, out double num))
                            return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return "0";
                    });

                    if (!hasMathExpr && (expr.Contains("+") || expr.Contains("-") ||
                        expr.Contains("*") || expr.Contains("/")))
                    {
                        expr = System.Text.RegularExpressions.Regex.Replace(expr, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
                        {
                            string k = match.Value;
                            if (lastKnownValues.TryGetValue(k, out double num))
                                return num.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            return "0";
                        });
                        hasMathExpr = true;
                    }

                    if (hasMathExpr)
                    {
                        var result = dt.Compute(expr, "");
                        if (result != DBNull.Value && TryToDouble(result, out double d))
                            mergedPoints.Add(new TsPoint(ts, Math.Round(d, 2), null));
                    }
                }
                catch { /* ignore per-point compute errors */ }
            }

            return mergedPoints;
        }

        // ── Live formula evaluation ───────────────────────────────────────────

        private string GetLiveValueForFormula(string formula, string? units,
            DeviceConfig? device = null, List<AvailableTelemetryDto>? allKeys = null)
        {
            try
            {
                lock (LatestValues)
                {
                    formula = ExpandFormula(formula, device);

                    if (formula.StartsWith("pathsum("))
                    {
                        var keys = ResolvePathSumKeys(formula, allKeys);
                        double sum = 0;
                        foreach (var k in keys)
                            if (LatestValues.TryGetValue(k, out var v) && TryToDouble(v, out double d))
                                sum += d;
                        return Math.Round(sum, 2).ToString(CultureInfo.InvariantCulture);
                    }

                    if (formula.Contains(":consumption:"))
                    {
                        var parts = formula.Split(":consumption:");
                        var baseKey = parts[0];
                        var interval = parts[1];
                        string suffix = interval switch
                        { "1h" => "_hourly", "1d" => "_daily", "1m" => "_monthly", "1y" => "_yearly", _ => "" };
                        if (LatestValues.TryGetValue(baseKey + suffix, out var v))
                            return v?.ToString() ?? "0";
                    }

                    if (LatestValues.TryGetValue(formula, out var rawVal) && TryToDouble(rawVal, out double rawNum))
                        return Math.Round(rawNum, 2).ToString(CultureInfo.InvariantCulture);

                    // Mathematical evaluation
                    string expr = formula;
                    bool hasMath = false;
                    expr = System.Text.RegularExpressions.Regex.Replace(expr, @"\[([^\]]+)\]", match =>
                    {
                        hasMath = true;
                        string key = match.Groups[1].Value;
                        if (LatestValues.TryGetValue(key, out var val) && TryToDouble(val, out double num))
                            return num.ToString(CultureInfo.InvariantCulture);
                        return "0";
                    });

                    if (!hasMath && (expr.Contains("+") || expr.Contains("-") ||
                        expr.Contains("*") || expr.Contains("/")))
                    {
                        expr = System.Text.RegularExpressions.Regex.Replace(expr, @"[a-zA-Z][a-zA-Z0-9_\-:]*", match =>
                        {
                            string key = match.Value;
                            if (key == "pathsum" || key == "consumption") return key;
                            if (LatestValues.TryGetValue(key, out var val) && TryToDouble(val, out double num))
                                return num.ToString(CultureInfo.InvariantCulture);
                            return "0";
                        });
                        hasMath = true;
                    }

                    if (hasMath)
                    {
                        using var dt = new DataTable();
                        var result = dt.Compute(expr, "");
                        if (result != DBNull.Value && TryToDouble(result, out double d))
                            return Math.Round(d, 2).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
            catch { /* fallback to default */ }

            return "-";
        }
    }
}
