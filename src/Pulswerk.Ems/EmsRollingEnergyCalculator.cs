using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulswerk.Ems
{
    public class EnergyMinuteBucket
    {
        [JsonPropertyName("min")]
        public long MinuteKey { get; set; } // Unix epoch minute

        [JsonPropertyName("g_in")]
        public double GridImportKwh { get; set; }

        [JsonPropertyName("g_out")]
        public double GridExportKwh { get; set; }

        [JsonPropertyName("pv")]
        public double PvGenKwh { get; set; }

        [JsonPropertyName("b_in")]
        public double BatteryChargeKwh { get; set; }

        [JsonPropertyName("b_out")]
        public double BatteryDischargeKwh { get; set; }

        [JsonPropertyName("unc")]
        public double UncontrollableKwh { get; set; }

        [JsonPropertyName("surp")]
        public double SurplusKwh { get; set; }

        [JsonPropertyName("cons")]
        public Dictionary<string, double> ConsumerKwh { get; set; } = new();
    }

    public record Rolling24hTotals(
        double GridImportKwh,
        double GridExportKwh,
        double PvGenerationKwh,
        double BatteryChargedKwh,
        double BatteryDischargedKwh,
        double UncontrollableKwh,
        double TotalSurplusKwh,
        Dictionary<string, double> ConsumerKwh);

    /// <summary>
    /// Retention tiers of the rolling energy calculator.
    /// </summary>
    [Flags]
    public enum EnergyTiers
    {
        None = 0,
        Minute = 1,
        Hour = 2,
        Day = 4,
        All = Minute | Hour | Day
    }

    /// <summary>
    /// Computes rolling energy (in/out) in kWh for all EMS channels by numerical
    /// trapezoidal integration of power telemetry. Three retention tiers are kept:
    /// 1-minute buckets for the rolling 24h window, 1-hour buckets for the rolling
    /// 30-day window and 1-day buckets for the rolling 1-year window.
    /// </summary>
    public class EmsRollingEnergyCalculator
    {
        private readonly object _lock = new();
        private readonly Dictionary<long, EnergyMinuteBucket> _buckets = new();
        private readonly Dictionary<long, EnergyMinuteBucket> _hourBuckets = new();
        private readonly Dictionary<long, EnergyMinuteBucket> _dayBuckets = new();

        private DateTime? _lastTimestamp;
        private double _lastGridKw;
        private double _lastPvKw;
        private double _lastBattKw;
        private double _lastUncontrollableKw;
        private double _lastSurplusKw;
        private readonly Dictionary<string, double> _lastConsumerKw = new();

        private const int RollingHours = 24;
        private const int PruneHours = 25; // Retain 25h to ensure rolling 24h is always complete
        private const int RollingDays = 30;
        private const int PruneDays = 31; // Retain 31d to ensure rolling 30d is always complete
        private const int RollingYearDays = 365;
        private const int PruneYearDays = 366; // Retain 366d to ensure rolling 1y is always complete

        private const long MinutesPerHour = 60;
        private const long MinutesPerDay = 1440;

        /// <summary>
        /// Version of the persisted state envelope. Version 3 introduced gap-only backfill;
        /// older payloads may contain energy that was integrated twice (restored state plus a
        /// full historical backfill), so they are discarded and rebuilt from the telemetry store.
        /// </summary>
        private const int CurrentStateVersion = 3;

        /// <summary>
        /// Ingests a live power telemetry sample and integrates energy into the current minute bucket.
        /// </summary>
        public void RecordSample(
            DateTime timestamp,
            double gridKw,
            double pvKw,
            double battKw,
            double uncontrollableKw,
            double surplusKw,
            IEnumerable<(string id, double powerKw)> consumers)
        {
            lock (_lock)
            {
                long currentMinute = new DateTimeOffset(timestamp).ToUnixTimeSeconds() / 60;

                if (_lastTimestamp.HasValue)
                {
                    double deltaSec = (timestamp - _lastTimestamp.Value).TotalSeconds;

                    // Only integrate if time has progressed normally (<= 180s to avoid stale jumps)
                    if (deltaSec > 0.05 && deltaSec <= 180.0)
                    {
                        double deltaHours = deltaSec / 3600.0;

                        // 1. Grid (In = Import, Out = Export)
                        double avgGridImport = (Math.Max(0.0, _lastGridKw) + Math.Max(0.0, gridKw)) * 0.5;
                        double avgGridExport = (Math.Max(0.0, -_lastGridKw) + Math.Max(0.0, -gridKw)) * 0.5;

                        // 2. Solar PV (Out = Generation; values are pre-normalized to the canonical
                        //    sign rule where positive = generation, so only positive power counts).
                        double avgPv = (Math.Max(0.0, _lastPvKw) + Math.Max(0.0, pvKw)) * 0.5;

                        // 3. Battery (In = Charge when P < 0, Out = Discharge when P > 0)
                        double avgBattCharge = (Math.Max(0.0, -_lastBattKw) + Math.Max(0.0, -battKw)) * 0.5;
                        double avgBattDischarge = (Math.Max(0.0, _lastBattKw) + Math.Max(0.0, battKw)) * 0.5;

                        // 4. Uncontrollable loads (In = Consumed)
                        double avgUnc = (Math.Max(0.0, _lastUncontrollableKw) + Math.Max(0.0, uncontrollableKw)) * 0.5;

                        // 5. Surplus pool
                        double avgSurplus = (Math.Max(0.0, _lastSurplusKw) + Math.Max(0.0, surplusKw)) * 0.5;

                        // 6. Consumers (In = Consumed)
                        var consumerDeltas = new Dictionary<string, double>();
                        foreach (var (id, power) in consumers)
                        {
                            _lastConsumerKw.TryGetValue(id, out double prevKw);
                            double avgCons = (Math.Max(0.0, prevKw) + Math.Max(0.0, power)) * 0.5;
                            consumerDeltas[id] = avgCons * deltaHours;
                        }

                        AddDelta(
                            currentMinute,
                            avgGridImport * deltaHours,
                            avgGridExport * deltaHours,
                            avgPv * deltaHours,
                            avgBattCharge * deltaHours,
                            avgBattDischarge * deltaHours,
                            avgUnc * deltaHours,
                            avgSurplus * deltaHours,
                            consumerDeltas);
                    }
                }

                // Update state
                _lastTimestamp = timestamp;
                _lastGridKw = gridKw;
                _lastPvKw = pvKw;
                _lastBattKw = battKw;
                _lastUncontrollableKw = uncontrollableKw;
                _lastSurplusKw = surplusKw;

                _lastConsumerKw.Clear();
                foreach (var (id, power) in consumers)
                {
                    _lastConsumerKw[id] = power;
                }

                // Prune buckets older than the retention windows
                long pruneCutoff = currentMinute - (PruneHours * 60);
                var staleKeys = _buckets.Keys.Where(k => k < pruneCutoff).ToList();
                foreach (var key in staleKeys)
                {
                    _buckets.Remove(key);
                }

                long hourPruneCutoff = currentMinute / MinutesPerHour - (PruneDays * 24);
                var staleHourKeys = _hourBuckets.Keys.Where(k => k < hourPruneCutoff).ToList();
                foreach (var key in staleHourKeys)
                {
                    _hourBuckets.Remove(key);
                }

                long dayPruneCutoff = currentMinute / MinutesPerDay - PruneYearDays;
                var staleDayKeys = _dayBuckets.Keys.Where(k => k < dayPruneCutoff).ToList();
                foreach (var key in staleDayKeys)
                {
                    _dayBuckets.Remove(key);
                }
            }
        }

        /// <summary>
        /// Adds an energy delta to the minute, hour and day bucket that contain <paramref name="minuteKey"/>.
        /// </summary>
        private void AddDelta(
            long minuteKey,
            double gridImportKwh,
            double gridExportKwh,
            double pvGenKwh,
            double battChargeKwh,
            double battDischargeKwh,
            double uncontrollableKwh,
            double surplusKwh,
            Dictionary<string, double>? consumerKwh,
            EnergyTiers tiers = EnergyTiers.All)
        {
            if (tiers.HasFlag(EnergyTiers.Minute))
                Accumulate(_buckets, minuteKey, gridImportKwh, gridExportKwh, pvGenKwh, battChargeKwh,
                    battDischargeKwh, uncontrollableKwh, surplusKwh, consumerKwh);
            if (tiers.HasFlag(EnergyTiers.Hour))
                Accumulate(_hourBuckets, minuteKey / MinutesPerHour, gridImportKwh, gridExportKwh, pvGenKwh,
                    battChargeKwh, battDischargeKwh, uncontrollableKwh, surplusKwh, consumerKwh);
            if (tiers.HasFlag(EnergyTiers.Day))
                Accumulate(_dayBuckets, minuteKey / MinutesPerDay, gridImportKwh, gridExportKwh, pvGenKwh,
                    battChargeKwh, battDischargeKwh, uncontrollableKwh, surplusKwh, consumerKwh);
        }

        private static void Accumulate(
            Dictionary<long, EnergyMinuteBucket> target,
            long key,
            double gridImportKwh,
            double gridExportKwh,
            double pvGenKwh,
            double battChargeKwh,
            double battDischargeKwh,
            double uncontrollableKwh,
            double surplusKwh,
            Dictionary<string, double>? consumerKwh)
        {
            if (!target.TryGetValue(key, out var bucket))
            {
                bucket = new EnergyMinuteBucket { MinuteKey = key };
                target[key] = bucket;
            }

            bucket.GridImportKwh += gridImportKwh;
            bucket.GridExportKwh += gridExportKwh;
            bucket.PvGenKwh += pvGenKwh;
            bucket.BatteryChargeKwh += battChargeKwh;
            bucket.BatteryDischargeKwh += battDischargeKwh;
            bucket.UncontrollableKwh += uncontrollableKwh;
            bucket.SurplusKwh += surplusKwh;

            if (consumerKwh != null)
            {
                foreach (var (cid, kwh) in consumerKwh)
                {
                    if (!bucket.ConsumerKwh.TryGetValue(cid, out double existing))
                        bucket.ConsumerKwh[cid] = kwh;
                    else
                        bucket.ConsumerKwh[cid] = existing + kwh;
                }
            }
        }

        /// <summary>
        /// Calculates the total energy for the trailing 24-hour rolling window.
        /// </summary>
        public Rolling24hTotals Get24hTotals(DateTime asOf)
        {
            lock (_lock)
            {
                long currentMinute = new DateTimeOffset(asOf).ToUnixTimeSeconds() / 60;
                return SumWindow(_buckets, currentMinute - (RollingHours * 60), currentMinute);
            }
        }

        /// <summary>
        /// Calculates the total energy for the trailing 30-day rolling window.
        /// </summary>
        public Rolling24hTotals Get30dTotals(DateTime asOf)
        {
            lock (_lock)
            {
                long currentHour = new DateTimeOffset(asOf).ToUnixTimeSeconds() / 60 / MinutesPerHour;
                return SumWindow(_hourBuckets, currentHour - (RollingDays * 24), currentHour);
            }
        }

        /// <summary>
        /// Calculates the total energy for the trailing 1-year rolling window.
        /// </summary>
        public Rolling24hTotals Get1yTotals(DateTime asOf)
        {
            lock (_lock)
            {
                long currentDay = new DateTimeOffset(asOf).ToUnixTimeSeconds() / 60 / MinutesPerDay;
                return SumWindow(_dayBuckets, currentDay - RollingYearDays, currentDay);
            }
        }

        private static Rolling24hTotals SumWindow(
            Dictionary<long, EnergyMinuteBucket> source,
            long windowStartKey,
            long windowEndKey)
        {
            double sumGridImport = 0.0;
            double sumGridExport = 0.0;
            double sumPv = 0.0;
            double sumBattCharge = 0.0;
            double sumBattDischarge = 0.0;
            double sumUnc = 0.0;
            double sumSurplus = 0.0;
            var consumerTotals = new Dictionary<string, double>();

            foreach (var (key, bucket) in source)
            {
                if (key >= windowStartKey && key <= windowEndKey)
                {
                    sumGridImport += bucket.GridImportKwh;
                    sumGridExport += bucket.GridExportKwh;
                    sumPv += bucket.PvGenKwh;
                    sumBattCharge += bucket.BatteryChargeKwh;
                    sumBattDischarge += bucket.BatteryDischargeKwh;
                    sumUnc += bucket.UncontrollableKwh;
                    sumSurplus += bucket.SurplusKwh;

                    foreach (var (cid, ckwh) in bucket.ConsumerKwh)
                    {
                        if (!consumerTotals.TryGetValue(cid, out double csum))
                            consumerTotals[cid] = ckwh;
                        else
                            consumerTotals[cid] = csum + ckwh;
                    }
                }
            }

            return new Rolling24hTotals(
                Math.Round(sumGridImport, 2),
                Math.Round(sumGridExport, 2),
                Math.Round(sumPv, 2),
                Math.Round(sumBattCharge, 2),
                Math.Round(sumBattDischarge, 2),
                Math.Round(sumUnc, 2),
                Math.Round(sumSurplus, 2),
                consumerTotals.ToDictionary(k => k.Key, v => Math.Round(v.Value, 2)));
        }

        /// <summary>
        /// Manually integrates a discrete block of energy into a specific minute bucket.
        /// Useful for historical bootstrapping from InfluxDB or test fixtures.
        /// </summary>
        /// <param name="tiers">
        /// Retention tiers to write. Backfill passes use a single tier so that coarse
        /// aggregates are not double-counted by overlapping passes.
        /// </param>
        public void AddEnergy(
            long minuteKey,
            double gridImportKwh = 0.0,
            double gridExportKwh = 0.0,
            double pvGenKwh = 0.0,
            double battChargeKwh = 0.0,
            double battDischargeKwh = 0.0,
            double uncontrollableKwh = 0.0,
            double surplusKwh = 0.0,
            Dictionary<string, double>? consumerKwh = null,
            EnergyTiers tiers = EnergyTiers.All)
        {
            lock (_lock)
            {
                AddDelta(
                    minuteKey,
                    gridImportKwh,
                    gridExportKwh,
                    pvGenKwh,
                    battChargeKwh,
                    battDischargeKwh,
                    uncontrollableKwh,
                    surplusKwh,
                    consumerKwh,
                    tiers);
            }
        }

        /// <summary>
        /// Returns the point in time where a historical backfill pass for <paramref name="tier"/>
        /// should start: the end of the newest bucket that already holds integrated energy,
        /// clamped to <paramref name="windowStart"/>. Backfill must resume here instead of
        /// re-integrating the whole window, otherwise restored state is counted twice.
        /// </summary>
        public DateTimeOffset GetBackfillStart(DateTimeOffset windowStart, EnergyTiers tier)
        {
            long? newestKey;
            TimeSpan bucketSize;

            lock (_lock)
            {
                switch (tier)
                {
                    case EnergyTiers.Minute:
                        newestKey = _buckets.Count == 0 ? null : _buckets.Keys.Max();
                        bucketSize = TimeSpan.FromMinutes(1);
                        break;
                    case EnergyTiers.Hour:
                        newestKey = _hourBuckets.Count == 0 ? null : _hourBuckets.Keys.Max();
                        bucketSize = TimeSpan.FromHours(1);
                        break;
                    case EnergyTiers.Day:
                        newestKey = _dayBuckets.Count == 0 ? null : _dayBuckets.Keys.Max();
                        bucketSize = TimeSpan.FromDays(1);
                        break;
                    default:
                        return windowStart;
                }
            }

            if (newestKey == null) return windowStart;

            var bucketEnd = DateTimeOffset.FromUnixTimeMilliseconds(
                (newestKey.Value + 1) * (long)bucketSize.TotalMilliseconds);
            return bucketEnd > windowStart ? bucketEnd : windowStart;
        }

        /// <summary>
        /// Serializes the in-memory buckets for persistence in SQLite.
        /// </summary>
        public string Serialize()
        {
            lock (_lock)
            {
                var state = new EnergyStateEnvelope
                {
                    Version = CurrentStateVersion,
                    MinuteBuckets = _buckets.Values.OrderBy(b => b.MinuteKey).ToList(),
                    HourBuckets = _hourBuckets.Values.OrderBy(b => b.MinuteKey).ToList(),
                    DayBuckets = _dayBuckets.Values.OrderBy(b => b.MinuteKey).ToList()
                };
                return JsonSerializer.Serialize(state);
            }
        }

        /// <summary>
        /// Restores buckets from serialized JSON state. Payloads written before version 3 are
        /// ignored, because they may contain energy that was integrated twice (restored state
        /// plus a full historical backfill). The backfill then rebuilds the buckets from the
        /// telemetry store.
        /// </summary>
        public void Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                // Legacy payloads (a bare array of minute buckets) predate the versioned
                // envelope and were written by builds that double-counted energy.
                if (json.TrimStart().StartsWith("[", StringComparison.Ordinal)) return;

                var state = JsonSerializer.Deserialize<EnergyStateEnvelope>(json);
                if (state == null) return;

                if (state.Version < CurrentStateVersion) return;

                lock (_lock)
                {
                    _buckets.Clear();
                    _hourBuckets.Clear();
                    _dayBuckets.Clear();

                    foreach (var bucket in state.MinuteBuckets ?? new List<EnergyMinuteBucket>())
                        _buckets[bucket.MinuteKey] = bucket;
                    foreach (var bucket in state.HourBuckets ?? new List<EnergyMinuteBucket>())
                        _hourBuckets[bucket.MinuteKey] = bucket;
                    foreach (var bucket in state.DayBuckets ?? new List<EnergyMinuteBucket>())
                        _dayBuckets[bucket.MinuteKey] = bucket;
                }
            }
            catch
            {
                // Silently ignore corrupted state
            }
        }

        /// <summary>
        /// Clears all buckets (used primarily in tests).
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _buckets.Clear();
                _hourBuckets.Clear();
                _dayBuckets.Clear();
                _lastTimestamp = null;
                _lastConsumerKw.Clear();
            }
        }

        private sealed class EnergyStateEnvelope
        {
            [JsonPropertyName("v")]
            public int Version { get; set; }

            [JsonPropertyName("min")]
            public List<EnergyMinuteBucket>? MinuteBuckets { get; set; }

            [JsonPropertyName("hour")]
            public List<EnergyMinuteBucket>? HourBuckets { get; set; }

            [JsonPropertyName("day")]
            public List<EnergyMinuteBucket>? DayBuckets { get; set; }
        }
    }
}
