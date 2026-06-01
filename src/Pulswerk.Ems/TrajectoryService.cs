using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;
using Pulswerk.Billing;

namespace Pulswerk.Ems
{
    public sealed class TrajectoryService
    {
        private static readonly Lazy<TrajectoryService> _instance = new(() => new TrajectoryService());
        public static TrajectoryService Instance => _instance.Value;

        private TelemetryStore? _telemetryStore;
        private BillingStore? _billingStore;
        private Func<string, double?>? _liveValueReader;
        private Func<string, double, Task<bool>>? _telemetryWriter;
        private CancellationTokenSource? _cts;
        
        // In-memory buffer of recent control decisions/events for UI display
        private readonly ConcurrentQueue<TrajectoryLogEntry> _controlLogs = new();
        private const int MaxLogEntries = 100;

        // Current status variables for UI polling
        public double TargetKwh { get; private set; }
        public double ActualKwh { get; private set; }
        public double DeviationPct { get; private set; }
        public bool IsCurtailmentActive { get; private set; }
        public string ControlState { get; private set; } = "Normal";

        private TrajectoryService() { }

        public void Initialize(TelemetryStore telemetryStore, BillingStore billingStore, Func<string, double?> liveValueReader, Func<string, double, Task<bool>> telemetryWriter)
        {
            _telemetryStore = telemetryStore;
            _billingStore = billingStore;
            _liveValueReader = liveValueReader;
            _telemetryWriter = telemetryWriter;
            Log.Info("[Trajectory] TrajectoryService initialized.");
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            Task.Run(() => LoopAsync(_cts.Token));
            Log.Info("[Trajectory] TrajectoryService background loop started.");
        }

        public void Stop()
        {
            _cts?.Cancel();
            Log.Info("[Trajectory] TrajectoryService background loop stopped.");
        }

        public List<TrajectoryLogEntry> GetLogs() => _controlLogs.ToList();

        private void AddLog(string message, string state)
        {
            var entry = new TrajectoryLogEntry(DateTime.UtcNow, message, state);
            _controlLogs.Enqueue(entry);
            while (_controlLogs.Count > MaxLogEntries)
            {
                _controlLogs.TryDequeue(out _);
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            // Initial delay
            await Task.Delay(5000, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool enabled = _billingStore?.GetTariff("trajectory_control_enabled", 0) == 1;
                    if (enabled)
                    {
                        await EvaluateTrajectoryAsync();
                    }
                    else
                    {
                        TargetKwh = 0;
                        ActualKwh = 0;
                        DeviationPct = 0;
                        IsCurtailmentActive = false;
                        ControlState = "Disabled";
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Trajectory] Error in control loop: {ex.Message}");
                }

                // Check every 15 seconds
                await Task.Delay(15000, ct);
            }
        }

        private async Task EvaluateTrajectoryAsync()
        {
            if (_billingStore == null || _telemetryStore == null || _liveValueReader == null || _telemetryWriter == null) return;

            // 1. Get Trajectory Settings
            string mainMeterKey = _billingStore.GetRfidMap().TryGetValue("trajectory_main_meter_key", out var key) ? key : "analytics-summary_daily-kwh";

            // 2. Query target trajectory at the current timestamp
            var now = DateTime.UtcNow;
            long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

            double targetKwh = _billingStore.GetTrajectoryTargetForTimestamp(nowMs);
            if (double.IsNaN(targetKwh))
            {
                // Linear monthly fallback if no 15-minute target points are defined
                double monthlyTargetKwh = _billingStore.GetTariff("trajectory_monthly_target_kwh", 3000.0);
                var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var endOfMonth = startOfMonth.AddMonths(1);

                double secondsInMonth = (endOfMonth - startOfMonth).TotalSeconds;
                double secondsElapsed = (now - startOfMonth).TotalSeconds;

                targetKwh = monthlyTargetKwh * (secondsElapsed / secondsInMonth);
            }

            TargetKwh = Math.Round(targetKwh, 2);

            // 3. Query actual monthly cumulative consumption
            var startOfMonthQuery = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            double actualKwh = await QueryActualConsumptionKwhAsync(mainMeterKey, startOfMonthQuery, now);
            ActualKwh = Math.Round(actualKwh, 2);

            // 4. Calculate deviation
            double devPct = 0.0;
            if (targetKwh > 0)
            {
                devPct = ((actualKwh - targetKwh) / targetKwh) * 100.0;
            }
            DeviationPct = Math.Round(devPct, 1);

            // 5. Curtailment Decision Logic
            string state = "Normal";
            bool curtail = false;

            if (devPct > 10.0) // Critical overload (> 10% above plan)
            {
                state = "Critical Curtailment";
                curtail = true;
            }
            else if (devPct > 2.0) // Warning level (> 2% above plan)
            {
                state = "Warning Curtailment";
                curtail = true;
            }
            else
            {
                state = "Normal";
                curtail = false;
            }

            // Trigger limits on active targets if states change
            if (curtail != IsCurtailmentActive || state != ControlState)
            {
                IsCurtailmentActive = curtail;
                ControlState = state;
                AddLog($"Control state changed to {state}. Applying limits to all generic curtailment targets.", state);
                await ApplyLimitsToTargetsAsync(state);
            }
        }

        private async Task<double> QueryActualConsumptionKwhAsync(string meterKey, DateTime start, DateTime end)
        {
            if (_telemetryStore == null) return 0.0;

            try
            {
                long startTs = new DateTimeOffset(start).ToUnixTimeMilliseconds();
                long endTs = new DateTimeOffset(end).ToUnixTimeMilliseconds();

                var points = await _telemetryStore.QueryAsync(meterKey, startTs, endTs, limit: 1000);
                if (points == null || points.Count == 0)
                {
                    if (_liveValueReader != null)
                    {
                        var liveVal = _liveValueReader(meterKey);
                        if (liveVal.HasValue)
                        {
                            return liveVal.Value;
                        }
                    }
                    return 0.0;
                }

                double earliest = Convert.ToDouble(points.Last().Value);
                double latest = Convert.ToDouble(points.First().Value);
                double diff = latest - earliest;
                
                return diff >= 0 ? diff : latest;
            }
            catch (Exception ex)
            {
                Log.Warning($"[Trajectory] Failed to query InfluxDB for key '{meterKey}': {ex.Message}");
                return 0.0;
            }
        }

        private async Task ApplyLimitsToTargetsAsync(string state)
        {
            if (_billingStore == null || _telemetryWriter == null) return;

            var targets = _billingStore.GetCurtailmentTargets();
            if (targets.Count == 0)
            {
                Log.Info("[Trajectory] No curtailment targets registered.");
                return;
            }

            foreach (var target in targets)
            {
                double valToWrite = state switch
                {
                    "Critical Curtailment" => target.CriticalValue,
                    "Warning Curtailment" => target.WarningValue,
                    _ => target.NormalValue
                };

                try
                {
                    Log.Info($"[Trajectory] Generic Curtailment: Writing {valToWrite} to '{target.TelemetryKey}' (State: {state})");
                    bool ok = await _telemetryWriter(target.TelemetryKey, valToWrite);
                    if (ok)
                    {
                        AddLog($"Successfully wrote {valToWrite} to '{target.TelemetryKey}' ({state}).", state);
                    }
                    else
                    {
                        AddLog($"Failed writing {valToWrite} to '{target.TelemetryKey}'. Check if point is writable.", state);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[Trajectory] Failed generic write to {target.TelemetryKey}: {ex.Message}");
                }
            }
        }
    }

    public record TrajectoryLogEntry(DateTime Timestamp, string Message, string State);
}
