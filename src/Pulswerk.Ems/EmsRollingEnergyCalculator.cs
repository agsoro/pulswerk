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
    /// Computes rolling 24-hour energy (in/out) in kWh for all EMS channels
    /// by numerical trapezoidal integration of power telemetry into 1-minute buckets.
    /// </summary>
    public class EmsRollingEnergyCalculator
    {
        private readonly object _lock = new();
        private readonly Dictionary<long, EnergyMinuteBucket> _buckets = new();

        private DateTime? _lastTimestamp;
        private double _lastGridKw;
        private double _lastPvKw;
        private double _lastBattKw;
        private double _lastUncontrollableKw;
        private double _lastSurplusKw;
        private readonly Dictionary<string, double> _lastConsumerKw = new();

        private const int RollingHours = 24;
        private const int PruneHours = 25; // Retain 25h to ensure rolling 24h is always complete

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

                        if (!_buckets.TryGetValue(currentMinute, out var bucket))
                        {
                            bucket = new EnergyMinuteBucket { MinuteKey = currentMinute };
                            _buckets[currentMinute] = bucket;
                        }

                        // 1. Grid (In = Import, Out = Export)
                        double avgGridImport = (Math.Max(0.0, _lastGridKw) + Math.Max(0.0, gridKw)) * 0.5;
                        double avgGridExport = (Math.Max(0.0, -_lastGridKw) + Math.Max(0.0, -gridKw)) * 0.5;
                        bucket.GridImportKwh += avgGridImport * deltaHours;
                        bucket.GridExportKwh += avgGridExport * deltaHours;

                        // 2. Solar PV (Out = Generation)
                        double avgPv = (Math.Max(0.0, _lastPvKw) + Math.Max(0.0, pvKw)) * 0.5;
                        bucket.PvGenKwh += avgPv * deltaHours;

                        // 3. Battery (In = Charge when P < 0, Out = Discharge when P > 0)
                        double avgBattCharge = (Math.Max(0.0, -_lastBattKw) + Math.Max(0.0, -battKw)) * 0.5;
                        double avgBattDischarge = (Math.Max(0.0, _lastBattKw) + Math.Max(0.0, battKw)) * 0.5;
                        bucket.BatteryChargeKwh += avgBattCharge * deltaHours;
                        bucket.BatteryDischargeKwh += avgBattDischarge * deltaHours;

                        // 4. Uncontrollable loads (In = Consumed)
                        double avgUnc = (Math.Max(0.0, _lastUncontrollableKw) + Math.Max(0.0, uncontrollableKw)) * 0.5;
                        bucket.UncontrollableKwh += avgUnc * deltaHours;

                        // 5. Surplus pool
                        double avgSurplus = (Math.Max(0.0, _lastSurplusKw) + Math.Max(0.0, surplusKw)) * 0.5;
                        bucket.SurplusKwh += avgSurplus * deltaHours;

                        // 6. Consumers (In = Consumed)
                        foreach (var (id, power) in consumers)
                        {
                            _lastConsumerKw.TryGetValue(id, out double prevKw);
                            double avgCons = (Math.Max(0.0, prevKw) + Math.Max(0.0, power)) * 0.5;
                            double kwh = avgCons * deltaHours;

                            if (!bucket.ConsumerKwh.TryGetValue(id, out double curKwh))
                                bucket.ConsumerKwh[id] = kwh;
                            else
                                bucket.ConsumerKwh[id] = curKwh + kwh;
                        }
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

                // Prune buckets older than PruneHours
                long pruneCutoff = currentMinute - (PruneHours * 60);
                var staleKeys = _buckets.Keys.Where(k => k < pruneCutoff).ToList();
                foreach (var key in staleKeys)
                {
                    _buckets.Remove(key);
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
                long windowStartMinute = currentMinute - (RollingHours * 60);

                double sumGridImport = 0.0;
                double sumGridExport = 0.0;
                double sumPv = 0.0;
                double sumBattCharge = 0.0;
                double sumBattDischarge = 0.0;
                double sumUnc = 0.0;
                double sumSurplus = 0.0;
                var consumerTotals = new Dictionary<string, double>();

                foreach (var (minuteKey, bucket) in _buckets)
                {
                    if (minuteKey >= windowStartMinute && minuteKey <= currentMinute)
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
        }

        /// <summary>
        /// Manually integrates a discrete block of energy into a specific minute bucket.
        /// Useful for historical bootstrapping from InfluxDB or test fixtures.
        /// </summary>
        public void AddEnergy(
            long minuteKey,
            double gridImportKwh = 0.0,
            double gridExportKwh = 0.0,
            double pvGenKwh = 0.0,
            double battChargeKwh = 0.0,
            double battDischargeKwh = 0.0,
            double uncontrollableKwh = 0.0,
            double surplusKwh = 0.0,
            Dictionary<string, double>? consumerKwh = null)
        {
            lock (_lock)
            {
                if (!_buckets.TryGetValue(minuteKey, out var bucket))
                {
                    bucket = new EnergyMinuteBucket { MinuteKey = minuteKey };
                    _buckets[minuteKey] = bucket;
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
        }

        /// <summary>
        /// Serializes the in-memory buckets for persistence in SQLite.
        /// </summary>
        public string Serialize()
        {
            lock (_lock)
            {
                var bucketList = _buckets.Values.OrderBy(b => b.MinuteKey).ToList();
                return JsonSerializer.Serialize(bucketList);
            }
        }

        /// <summary>
        /// Restores buckets from serialized JSON state.
        /// </summary>
        public void Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<EnergyMinuteBucket>>(json);
                if (list == null) return;

                lock (_lock)
                {
                    _buckets.Clear();
                    foreach (var bucket in list)
                    {
                        _buckets[bucket.MinuteKey] = bucket;
                    }
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
                _lastTimestamp = null;
                _lastConsumerKw.Clear();
            }
        }
    }
}
