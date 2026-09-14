using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Storage;
using Pulswerk.Billing;

namespace Pulswerk.Ems
{
    // ── Domain Models for Two-Sided Energy Management ────────────────────────

    public class EnergySourcesConfig
    {
        public string GridMeterKey { get; set; } = "meter-main-a_power";
        public double GridMaxImportKw { get; set; } = 8.0;
        public string PvMeterKey { get; set; } = "pv-rooftop_power";
        public string BatteryPowerKey { get; set; } = "solis-battery_power";
        public string BatterySocKey { get; set; } = "solis-battery_battery_soc";

        private double _batteryMaxChargeKw = 10.0;
        private double _batteryMaxDischargeKw = 10.0;

        /// <summary>
        /// Maximum continuous charge/discharge power of the battery storage system in kW (e.g. 5.0 kW, 10.0 kW).
        /// </summary>
        public double BatteryMaxPowerKw
        {
            get => _batteryMaxChargeKw;
            set
            {
                _batteryMaxChargeKw = value > 0 ? value : 10.0;
                _batteryMaxDischargeKw = _batteryMaxChargeKw;
            }
        }

        /// <summary>
        /// Maximum continuous charging power in kW.
        /// </summary>
        public double BatteryMaxChargeKw
        {
            get => _batteryMaxChargeKw;
            set => _batteryMaxChargeKw = value > 0 ? value : 10.0;
        }

        /// <summary>
        /// Maximum continuous discharge power in kW.
        /// </summary>
        public double BatteryMaxDischargeKw
        {
            get => _batteryMaxDischargeKw;
            set => _batteryMaxDischargeKw = value > 0 ? value : 10.0;
        }

        public double BatteryMinReserveKw { get; set; } = 1.0;
        public double BatteryMinSocPct { get; set; } = 15.0;
        public double BatteryFullSocPct { get; set; } = 98.0;
    }

    public class EnergyConsumer
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        // ── Tier 1: Uncontrollable Base Tier ──────────────────────────────────
        /// <summary>
        /// Essential baseline load that is uncontrollable (standby electronics, base compressor, etc.).
        /// </summary>
        public double BasePowerKw { get; set; } = 0.0;

        // ── Tier 2: Optional Controllable Tier ────────────────────────────────
        /// <summary>
        /// Whether this consumer has an optional controllable tier (e.g. EV charging boost, heat pump boost).
        /// If false (or MaxOptionalKw <= 0), the consumer is purely uncontrollable.
        /// </summary>
        public bool HasOptionalTier { get; set; } = true;

        private double _maxOptionalKw = 8.0;
        /// <summary>
        /// Maximum optional tier power in kW (e.g. 8.0 kW default for wallboxes).
        /// </summary>
        public double MaxOptionalKw
        {
            get => _maxOptionalKw;
            set => _maxOptionalKw = value;
        }

        /// <summary>
        /// Minimum operational power required to activate the optional tier in kW (e.g. 1.38 kW for 6A 1-phase EV charging).
        /// </summary>
        public double MinOptionalKw { get; set; } = 0.0;

        /// <summary>
        /// Standby / readiness power allocation when consumer is idle / not demanding optional power (e.g. 0.0 kW, 0.2 kW, or 1.38 kW).
        /// Power above this standby amount is reclaimed and redistributed to active consumers.
        /// </summary>
        public double StandbyOptionalKw { get; set; } = 0.0;

        /// <summary>
        /// Physical/hardware upper limit (e.g. 22 kW for 3-phase 32A wallboxes, or 11 kW).
        /// Solar surplus can boost the consumer up to this ceiling.
        /// </summary>
        public double MaxPowerKw { get; set; } = 22.0;

        // ── Telemetry & Control Keys ──────────────────────────────────────────
        public string ActualPowerKey { get; set; } = "";
        public string ForcePowerKey { get; set; } = "";

        // ── Priority ──────────────────────────────────────────────────────────
        public int Priority { get; set; } = 1;

        // ── Backward Compatibility Properties ─────────────────────────────────
        public bool IsControllable
        {
            get => HasOptionalTier && _maxOptionalKw > 0;
            set => HasOptionalTier = value;
        }

        public double BaseLimitKw
        {
            get => _maxOptionalKw;
            set => _maxOptionalKw = value;
        }

        public double MinPowerKw
        {
            get => MinOptionalKw;
            set => MinOptionalKw = value;
        }

        // ── Runtime Dynamic State ─────────────────────────────────────────────
        [JsonIgnore]
        public double ActualPowerKw { get; set; }

        [JsonIgnore]
        public bool IsActivelyDemanding { get; set; }

        [JsonIgnore]
        public double AllocatedOptionalKw { get; set; }

        [JsonIgnore]
        public double AllocatedPowerKw { get; set; }

        [JsonIgnore]
        public double UnusedPowerKw { get; set; }

        [JsonIgnore]
        public string Status { get; set; } = "Idle";

        [JsonIgnore]
        public double LastWrittenSetpointKw { get; set; } = -1.0;

        [JsonIgnore]
        public DateTime LastWrittenUtc { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Calculated energy consumed over the trailing 24-hour rolling day window in kWh.
        /// </summary>
        public double Energy24hKwh { get; set; }
    }

    public class EnergySystemSnapshot
    {
        public bool Enabled { get; set; }
        public double GridImportKw { get; set; }
        public double GridMaxImportKw { get; set; }
        public double PvGenerationKw { get; set; }
        public double BatteryPowerKw { get; set; }
        public double BatteryChargeKw { get; set; }
        public double BatterySocPct { get; set; }
        public double BatteryMaxPowerKw { get; set; }
        public double BatteryMaxChargeKw { get; set; }
        public double BatteryMaxDischargeKw { get; set; }
        public bool IsBatteryCharging { get; set; }
        public double TotalSurplusAvailableKw { get; set; }
        public double TotalBaseLoadKw { get; set; }
        public double TotalOptionalLoadKw { get; set; }
        public double TotalReclaimedPowerKw { get; set; }
        public double UncontrollableLoadKw { get; set; }
        public double TotalControllableLoadKw { get; set; }

        // ── Rolling 24-Hour Calculated Energy (kWh) ───────────────────────────
        public double GridImport24hKwh { get; set; }
        public double GridExport24hKwh { get; set; }
        public double PvGeneration24hKwh { get; set; }
        public double BatteryCharged24hKwh { get; set; }
        public double BatteryDischarged24hKwh { get; set; }
        public double Uncontrollable24hKwh { get; set; }
        public double TotalSurplus24hKwh { get; set; }
        public double TotalControllable24hKwh { get; set; }

        public List<EnergyConsumer> Consumers { get; set; } = new();
        public List<EmsLogEntry> Logs { get; set; } = new();
        public EnergySourcesConfig Sources { get; set; } = new();
    }

    // ── Pure Dispatch Engine ──────────────────────────────────────────────────

    public static class EnergyDispatchEngine
    {
        /// <summary>
        /// Pure allocation algorithm for two-sided energy management with two-tier consumers
        /// and dynamic idle / underutilization power redistribution.
        /// </summary>
        public static void Dispatch(
            EnergySourcesConfig sources,
            double gridPowerKw,
            double pvPowerKw,
            double batteryPowerKw,
            double batterySocPct,
            IReadOnlyList<EnergyConsumer> consumers)
        {
            if (consumers == null || consumers.Count == 0) return;

            // 1. Inverter / Battery Surplus Calculation
            // Negative power = Charging (absorbing surplus), Positive power = Discharging.
            double rawCharge = batteryPowerKw < -0.2 ? Math.Abs(batteryPowerKw) : 0.0;
            double maxCharge = sources.BatteryMaxChargeKw > 0 ? sources.BatteryMaxChargeKw : 5.0;
            double battChargeKw = Math.Min(rawCharge, maxCharge);

            bool isCharging = battChargeKw > 0.3 
                && batterySocPct >= sources.BatteryMinSocPct 
                && batterySocPct < sources.BatteryFullSocPct;

            // Surplus power divertible from battery charging into controllable loads:
            double battSurplus = isCharging 
                ? Math.Max(0.0, battChargeKw - sources.BatteryMinReserveKw) 
                : 0.0;

            // Additional surplus: solar power exported to grid (e.g. PV generation exceeding battery charge capacity):
            double gridExportKw = gridPowerKw < -0.2 ? Math.Abs(gridPowerKw) : 0.0;

            double availableSurplus = battSurplus + gridExportKw;

            // 2. Classify Consumers into Uncontrollable vs Controllable
            foreach (var consumer in consumers)
            {
                if (!consumer.HasOptionalTier || consumer.MaxOptionalKw <= 0.0)
                {
                    consumer.AllocatedOptionalKw = 0.0;
                    double allocatedBase = Math.Max(consumer.BasePowerKw, consumer.ActualPowerKw);
                    consumer.AllocatedPowerKw = Math.Round(allocatedBase, 2);
                    consumer.UnusedPowerKw = 0.0;
                    consumer.IsActivelyDemanding = false;
                    consumer.Status = "Uncontrollable Load";
                }
            }

            // 3. Assess Active Demand vs Idle State for Controllable Consumers
            var controllable = consumers
                .Where(c => c.HasOptionalTier && c.MaxOptionalKw > 0.0)
                .OrderBy(c => c.Priority)
                .ToList();

            foreach (var consumer in controllable)
            {
                double actualOpt = Math.Max(0.0, consumer.ActualPowerKw - consumer.BasePowerKw);
                // Demand is determined solely and exclusively by currently drawn power
                double threshold = Math.Max(0.1, consumer.StandbyOptionalKw);
                consumer.IsActivelyDemanding = actualOpt > threshold;
            }

            // 4. Calculate Available Optional Pool
            // Base budget is strictly the site's GridMaxImportKw + any available solar surplus
            double availablePool = Math.Max(0.0, sources.GridMaxImportKw + availableSurplus);

            var activeConsumers = controllable.Where(c => c.IsActivelyDemanding).ToList();
            var idleConsumers = controllable.Where(c => !c.IsActivelyDemanding).ToList();

            if (activeConsumers.Count > 0)
            {
                // 5. Detect Idle Consumers & Reclaim Unused Capacity
                // Idle consumers drop to standby, freeing up the rest of their MaxOptionalKw for active loads
                foreach (var consumer in idleConsumers)
                {
                    double standby = Math.Min(consumer.StandbyOptionalKw, consumer.MaxOptionalKw);
                    consumer.AllocatedOptionalKw = Math.Round(standby, 2);
                    consumer.AllocatedPowerKw = Math.Round(consumer.BasePowerKw + standby, 2);
                    consumer.UnusedPowerKw = Math.Round(Math.Max(0.0, consumer.MaxOptionalKw - standby), 2);
                    consumer.Status = consumer.UnusedPowerKw > 0.1 
                        ? $"Idle ({consumer.UnusedPowerKw:F1} kW redistributed)" 
                        : "Idle";

                    // Deduct standby from pool
                    availablePool = Math.Max(0.0, availablePool - standby);
                }

                // 6. Water-Filling Allocation to Actively Demanding Consumers by Priority
                double totalActiveDesired = activeConsumers.Sum(c => c.MaxOptionalKw);
                bool isConstrained = activeConsumers.Count > 1 && availablePool < totalActiveDesired;

                foreach (var consumer in activeConsumers)
                {
                    double actualOpt = Math.Max(0.0, consumer.ActualPowerKw - consumer.BasePowerKw);

                    double targetOpt = consumer.MaxOptionalKw;
                    if (isCharging && availableSurplus > 0.1)
                    {
                        // Closed-loop virtual pool: actual consumption + surplus entering battery
                        double poolForThis = actualOpt + availableSurplus;
                        targetOpt = Math.Max(consumer.MaxOptionalKw, poolForThis);
                        targetOpt = Math.Min(targetOpt, consumer.MaxPowerKw - consumer.BasePowerKw);
                    }
                    else if (isConstrained && actualOpt > 0.2)
                    {
                        // Detect underutilization: competing consumers with constrained pool.
                        // Grant actual draw + 1.5 kW ramping headroom, freeing the unused capacity for other consumers.
                        double practicalDemand = actualOpt + 1.5;
                        if (practicalDemand < targetOpt)
                        {
                            targetOpt = Math.Max(consumer.MinOptionalKw, practicalDemand);
                        }
                    }

                    // Clamp to available pool and min threshold
                    double allocatedOpt = Math.Min(targetOpt, availablePool);
                    if (allocatedOpt < consumer.MinOptionalKw && availablePool < consumer.MinOptionalKw)
                    {
                        allocatedOpt = 0.0;
                    }

                    allocatedOpt = Math.Round(allocatedOpt, 1);
                    consumer.AllocatedOptionalKw = allocatedOpt;
                    consumer.AllocatedPowerKw = Math.Round(consumer.BasePowerKw + allocatedOpt, 1);
                    consumer.UnusedPowerKw = Math.Round(Math.Max(0.0, consumer.MaxOptionalKw - allocatedOpt), 1);

                    // Deduct from available pool
                    availablePool = Math.Max(0.0, availablePool - allocatedOpt);

                    // Deduct surplus used above MaxOptionalKw
                    if (allocatedOpt > consumer.MaxOptionalKw)
                    {
                        double surplusUsed = allocatedOpt - consumer.MaxOptionalKw;
                        availableSurplus = Math.Max(0.0, availableSurplus - surplusUsed);
                    }

                    // Update consumer Status
                    if (allocatedOpt > consumer.MaxOptionalKw + 0.1)
                    {
                        consumer.Status = $"Surplus Boost (+{Math.Round(allocatedOpt - consumer.MaxOptionalKw, 1)} kW)";
                    }
                    else if (consumer.UnusedPowerKw > 0.1)
                    {
                        consumer.Status = $"Active ({consumer.AllocatedPowerKw:F1} kW, {consumer.UnusedPowerKw:F1} kW redistributed)";
                    }
                    else
                    {
                        consumer.Status = consumer.BasePowerKw > 0
                            ? $"Active ({consumer.BasePowerKw:F1} kW base + {allocatedOpt:F1} kW opt)"
                            : $"Base Limit ({consumer.MaxOptionalKw:F1} kW)";
                    }
                }
            }
            else
            {
                // When ALL consumers are idle (no consumer currently drawing power),
                // allocate available pool by Priority so devices have readiness to start drawing.
                foreach (var consumer in controllable)
                {
                    double allocatedOpt = Math.Min(consumer.MaxOptionalKw, availablePool);
                    if (allocatedOpt < consumer.MinOptionalKw && availablePool < consumer.MinOptionalKw)
                    {
                        allocatedOpt = 0.0;
                    }

                    allocatedOpt = Math.Round(allocatedOpt, 1);
                    consumer.AllocatedOptionalKw = allocatedOpt;
                    consumer.AllocatedPowerKw = Math.Round(consumer.BasePowerKw + allocatedOpt, 1);
                    consumer.UnusedPowerKw = Math.Round(Math.Max(0.0, consumer.MaxOptionalKw - allocatedOpt), 1);

                    availablePool = Math.Max(0.0, availablePool - allocatedOpt);

                    consumer.Status = consumer.AllocatedPowerKw > 0.0
                        ? $"Ready ({consumer.AllocatedPowerKw:F1} kW)"
                        : "Idle";
                }
            }
        }
    }

    // ── Energy Management System (EMS) Service ───────────────────────────────

    public sealed class EmsService
    {
        private static readonly Lazy<EmsService> _instance = new(() => new EmsService());
        public static EmsService Instance => _instance.Value;

        private TelemetryStore? _telemetryStore;
        private BillingStore? _billingStore;
        private Func<string, double?>? _liveValueReader;
        private Func<string, double, Task<bool>>? _telemetryWriter;
        private CancellationTokenSource? _cts;

        private readonly ConcurrentQueue<EmsLogEntry> _controlLogs = new();
        private const int MaxLogEntries = 100;

        private readonly EmsRollingEnergyCalculator _energyCalc = new();
        public EmsRollingEnergyCalculator EnergyCalculator => _energyCalc;
        private int _cyclesSinceEnergySave = 0;

        public bool Enabled { get; private set; } = true;
        public EnergySourcesConfig SourcesConfig { get; private set; } = new();
        public List<EnergyConsumer> Consumers { get; private set; } = new();

        // Live telemetry cache for snapshot
        public double LiveGridKw { get; private set; }
        public double LivePvKw { get; private set; }
        public double LiveBatteryKw { get; private set; }
        public double LiveBatteryChargeKw { get; private set; }
        public double LiveBatterySocPct { get; private set; }
        public bool IsBatteryCharging { get; private set; }

        // Backward compatibility properties for UI/API
        public double BaseLimitKw => Consumers.FirstOrDefault(c => c.IsControllable)?.BaseLimitKw ?? SourcesConfig.GridMaxImportKw;
        public double EffectiveLimitKw => Consumers.FirstOrDefault(c => c.IsControllable)?.AllocatedPowerKw ?? BaseLimitKw;
        public double WbActualKw => Consumers.FirstOrDefault(c => c.IsControllable)?.ActualPowerKw ?? 0.0;
        public double BatteryPowerKw => LiveBatteryKw;
        public double BatteryChargeKw => LiveBatteryChargeKw;
        public double BatterySocPct => LiveBatterySocPct;
        public string ControlMode => Consumers.FirstOrDefault(c => c.IsControllable)?.Status ?? "Normal";
        public string WbForcePowerKey => Consumers.FirstOrDefault(c => c.IsControllable)?.ForcePowerKey ?? "ocpp-central_force_power";
        public string WbActualPowerKey => Consumers.FirstOrDefault(c => c.IsControllable)?.ActualPowerKey ?? "ocpp-central_power";
        public string BatteryPowerKey => SourcesConfig.BatteryPowerKey;
        public string BatterySocKey => SourcesConfig.BatterySocKey;
        public double BatteryReserveKw => SourcesConfig.BatteryMinReserveKw;
        public double BatteryMaxPowerKw => SourcesConfig.BatteryMaxPowerKw;
        public double BatteryMaxChargeKw => SourcesConfig.BatteryMaxChargeKw;
        public double BatteryMaxDischargeKw => SourcesConfig.BatteryMaxDischargeKw;

        public double TargetKwh => EffectiveLimitKw;
        public double ActualKwh => WbActualKw;
        public double DeviationPct => BaseLimitKw > 0 ? Math.Round(((EffectiveLimitKw - BaseLimitKw) / BaseLimitKw) * 100.0, 1) : 0.0;
        public bool IsCurtailmentActive => EffectiveLimitKw < BaseLimitKw;
        public string ControlState => ControlMode;

        private EmsService() { }

        public void Initialize(
            TelemetryStore telemetryStore,
            BillingStore billingStore,
            Func<string, double?> liveValueReader,
            Func<string, double, Task<bool>> telemetryWriter)
        {
            _telemetryStore = telemetryStore;
            _billingStore = billingStore;
            _liveValueReader = liveValueReader;
            _telemetryWriter = telemetryWriter;

            LoadConfiguration();
            Log.Info("[EnergyControl] Two-Sided Energy & Smart Charging Service initialized.");
        }

        public void Start()
        {
            var previous = _cts;
            if (previous != null)
            {
                try { previous.Cancel(); } catch { }
                try { previous.Dispose(); } catch { }
            }

            _cts = new CancellationTokenSource();
            Task.Run(() => LoopAsync(_cts.Token));
            _ = Task.Run(BootstrapHistoricalEnergyAsync);
            Log.Info("[EnergyControl] Energy dispatch loop started.");
        }

        public void Stop()
        {
            var cts = _cts;
            _cts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            if (_billingStore != null)
            {
                try { _billingStore.SetSetting("ems_energy_24h_state", _energyCalc.Serialize()); } catch { }
            }

            Log.Info("[EnergyControl] Energy dispatch loop stopped.");
        }

        public List<EmsLogEntry> GetLogs() => _controlLogs.ToList();

        public void AddLog(string message, string state)
        {
            var entry = new EmsLogEntry(DateTime.UtcNow, message, state);
            _controlLogs.Enqueue(entry);
            while (_controlLogs.Count > MaxLogEntries)
            {
                _controlLogs.TryDequeue(out _);
            }
        }

        public void LoadConfiguration()
        {
            if (_billingStore == null) return;

            Enabled = _billingStore.GetTariff("energy_control_enabled", 1) == 1;

            // 1. Sources config
            string sourcesJson = _billingStore.GetSetting("energy_sources_config", "");
            if (!string.IsNullOrWhiteSpace(sourcesJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<EnergySourcesConfig>(sourcesJson);
                    if (parsed != null) SourcesConfig = parsed;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EnergyControl] Failed to parse sources config: {ex.Message}");
                }
            }
            else
            {
                SourcesConfig = new EnergySourcesConfig
                {
                    GridMaxImportKw = _billingStore.GetTariff("energy_control_base_kw", 8.0),
                    GridMeterKey = _billingStore.GetSetting("energy_control_grid_key", "meter-main-a_power"),
                    PvMeterKey = _billingStore.GetSetting("energy_control_pv_key", "pv-rooftop_power"),
                    BatteryMinReserveKw = _billingStore.GetTariff("energy_control_reserve_kw", 1.0),
                    BatteryMaxPowerKw = _billingStore.GetTariff("energy_control_battery_max_kw", 5.0),
                    BatteryPowerKey = _billingStore.GetSetting("energy_control_battery_power_key", "solis-battery_power"),
                    BatterySocKey = _billingStore.GetSetting("energy_control_battery_soc_key", "solis-battery_battery_soc")
                };
            }

            // 2. Consumers config
            string consumersJson = _billingStore.GetSetting("energy_consumers_config", "");
            if (!string.IsNullOrWhiteSpace(consumersJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<EnergyConsumer>>(consumersJson);
                    if (parsed != null)
                    {
                        var valid = parsed.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)).ToList();
                        if (valid.Count > 0) Consumers = valid;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EnergyControl] Failed to parse consumers config: {ex.Message}");
                }
            }

            // Default fallback consumers if list is empty or had only corrupted entries: realistic fleet of controllable + base loads
            if (Consumers.Count == 0)
            {
                Consumers = new List<EnergyConsumer>
                {
                    new EnergyConsumer
                    {
                        Id = "wb-fleet-01",
                        Name = "Garage Wallbox 1 (11 kW)",
                        BasePowerKw = 0.0,
                        HasOptionalTier = true,
                        MaxOptionalKw = 11.0,
                        MinOptionalKw = 1.38,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "wallbox-sim-01_power",
                        ForcePowerKey = "wallbox-sim-01_force_power",
                        Priority = 1,
                        MaxPowerKw = 11.0
                    },
                    new EnergyConsumer
                    {
                        Id = "wb-visitor-02",
                        Name = "Garage Wallbox 2 (11 kW)",
                        BasePowerKw = 0.0,
                        HasOptionalTier = true,
                        MaxOptionalKw = 11.0,
                        MinOptionalKw = 1.38,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "wallbox-sim-02_power",
                        ForcePowerKey = "wallbox-sim-02_force_power",
                        Priority = 2,
                        MaxPowerKw = 11.0
                    },
                    new EnergyConsumer
                    {
                        Id = "hvac-heat-pump",
                        Name = "HVAC Heat Pump Main",
                        BasePowerKw = 2.2,
                        HasOptionalTier = true,
                        MaxOptionalKw = 5.0,
                        MinOptionalKw = 1.0,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "meter-hvac_power",
                        ForcePowerKey = "",
                        Priority = 3,
                        MaxPowerKw = 7.5
                    },
                    new EnergyConsumer
                    {
                        Id = "heatpump-annex",
                        Name = "Annex Heat Pump",
                        BasePowerKw = 1.5,
                        HasOptionalTier = true,
                        MaxOptionalKw = 3.0,
                        MinOptionalKw = 0.8,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "heatpump-annex_power",
                        ForcePowerKey = "",
                        Priority = 4,
                        MaxPowerKw = 4.5
                    },
                    new EnergyConsumer
                    {
                        Id = "base-building-infrastructure",
                        Name = "Building Floor 2 Base Load",
                        BasePowerKw = 1.8,
                        HasOptionalTier = false,
                        MaxOptionalKw = 0.0,
                        MinOptionalKw = 0.0,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "meter-sub-f2_power",
                        ForcePowerKey = "",
                        Priority = 5,
                        MaxPowerKw = 5.0
                    },
                    new EnergyConsumer
                    {
                        Id = "base-server-room",
                        Name = "Building Floor 3 Base Load",
                        BasePowerKw = 1.5,
                        HasOptionalTier = false,
                        MaxOptionalKw = 0.0,
                        MinOptionalKw = 0.0,
                        StandbyOptionalKw = 0.0,
                        ActualPowerKey = "meter-sub-f3_power",
                        ForcePowerKey = "",
                        Priority = 6,
                        MaxPowerKw = 5.0
                    }
                };
            }

            // 3. Load rolling 24-hour energy state
            string energyJson = _billingStore.GetSetting("ems_energy_24h_state", "");
            if (!string.IsNullOrWhiteSpace(energyJson))
            {
                try
                {
                    _energyCalc.Deserialize(energyJson);
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EnergyControl] Failed to restore energy state: {ex.Message}");
                }
            }
        }

        public void SaveConfiguration(bool enabled, EnergySourcesConfig sources, List<EnergyConsumer> consumers)
        {
            Enabled = enabled;
            SourcesConfig = sources;
            Consumers = consumers.Where(c => !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name)).ToList();
            if (Consumers.Count == 0) Consumers = consumers;

            if (_billingStore != null)
            {
                _billingStore.SetTariff("energy_control_enabled", enabled ? 1 : 0);
                _billingStore.SetTariff("energy_control_base_kw", sources.GridMaxImportKw);
                _billingStore.SetTariff("energy_control_reserve_kw", sources.BatteryMinReserveKw);
                _billingStore.SetTariff("energy_control_battery_max_kw", sources.BatteryMaxPowerKw);

                _billingStore.SetSetting("energy_sources_config", JsonSerializer.Serialize(sources));
                _billingStore.SetSetting("energy_consumers_config", JsonSerializer.Serialize(consumers));
                _billingStore.SetSetting("ems_energy_24h_state", _energyCalc.Serialize());

                var primaryControllable = consumers.FirstOrDefault(c => c.IsControllable);
                if (primaryControllable != null)
                {
                    _billingStore.SetSetting("energy_control_wb_key", primaryControllable.ForcePowerKey);
                    _billingStore.SetSetting("energy_control_wb_actual_key", primaryControllable.ActualPowerKey);
                }
            }
        }

        public EnergySystemSnapshot GetSnapshot()
        {
            double totalControllable = Consumers.Where(c => c.IsControllable).Sum(c => c.ActualPowerKw);
            double totalUncontrollable = Consumers.Where(c => !c.IsControllable).Sum(c => c.ActualPowerKw);
            double totalBaseTier = Consumers.Sum(c => c.BasePowerKw);
            double totalOptional = Consumers.Sum(c => c.AllocatedOptionalKw);
            double totalReclaimed = Consumers.Sum(c => c.UnusedPowerKw);

            // If no uncontrollable loads configured, calculate from grid balance
            if (totalUncontrollable <= 0.0)
            {
                double balanceLoad = (LiveGridKw + LivePvKw + LiveBatteryKw) - totalControllable;
                totalUncontrollable = Math.Max(0.0, Math.Round(balanceLoad, 2));
            }

            double rawCharge = LiveBatteryKw < -0.2 ? Math.Abs(LiveBatteryKw) : 0.0;
            double battCharge = Math.Min(rawCharge, SourcesConfig.BatteryMaxChargeKw);
            double battSurplus = IsBatteryCharging 
                ? Math.Max(0.0, battCharge - SourcesConfig.BatteryMinReserveKw) 
                : 0.0;
            double exportSurplus = LiveGridKw < -0.2 ? Math.Abs(LiveGridKw) : 0.0;
            double totalSurplus = battSurplus + exportSurplus;

            // Compute rolling 24-hour energy totals
            var energyTotals = _energyCalc.Get24hTotals(DateTime.UtcNow);
            foreach (var consumer in Consumers)
            {
                if (energyTotals.ConsumerKwh.TryGetValue(consumer.Id, out double ckwh))
                    consumer.Energy24hKwh = ckwh;
                else
                    consumer.Energy24hKwh = 0.0;
            }

            return new EnergySystemSnapshot
            {
                Enabled = Enabled,
                GridImportKw = LiveGridKw,
                GridMaxImportKw = SourcesConfig.GridMaxImportKw,
                PvGenerationKw = LivePvKw,
                BatteryPowerKw = LiveBatteryKw,
                BatteryChargeKw = LiveBatteryChargeKw,
                BatterySocPct = LiveBatterySocPct,
                BatteryMaxPowerKw = SourcesConfig.BatteryMaxPowerKw,
                BatteryMaxChargeKw = SourcesConfig.BatteryMaxChargeKw,
                BatteryMaxDischargeKw = SourcesConfig.BatteryMaxDischargeKw,
                IsBatteryCharging = IsBatteryCharging,
                TotalSurplusAvailableKw = Math.Round(totalSurplus, 2),
                TotalBaseLoadKw = Math.Round(totalBaseTier, 2),
                TotalOptionalLoadKw = Math.Round(totalOptional, 2),
                TotalReclaimedPowerKw = Math.Round(totalReclaimed, 2),
                UncontrollableLoadKw = Math.Round(totalUncontrollable, 2),
                TotalControllableLoadKw = Math.Round(totalControllable, 2),

                // 24h Rolling Day Calculated Energy (in kWh)
                GridImport24hKwh = energyTotals.GridImportKwh,
                GridExport24hKwh = energyTotals.GridExportKwh,
                PvGeneration24hKwh = energyTotals.PvGenerationKwh,
                BatteryCharged24hKwh = energyTotals.BatteryChargedKwh,
                BatteryDischarged24hKwh = energyTotals.BatteryDischargedKwh,
                Uncontrollable24hKwh = energyTotals.UncontrollableKwh,
                TotalSurplus24hKwh = energyTotals.TotalSurplusKwh,
                TotalControllable24hKwh = Math.Round(energyTotals.ConsumerKwh.Values.Sum(), 2),

                Consumers = Consumers.ToList(),
                Logs = GetLogs(),
                Sources = SourcesConfig
            };
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            await Task.Delay(3000, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (Enabled)
                    {
                        await EvaluateDispatchAsync();
                    }
                    else
                    {
                        foreach (var c in Consumers.Where(c => c.IsControllable))
                        {
                            c.AllocatedOptionalKw = 0.0;
                            c.AllocatedPowerKw = 0.0; // 0 = Unrestricted in OCPP master
                            c.Status = "Disabled";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[EnergyControl] Error in dispatch loop: {ex.Message}");
                }

                await Task.Delay(10000, ct);
            }
        }

        private async Task EvaluateDispatchAsync()
        {
            if (_liveValueReader == null || _telemetryWriter == null) return;

            // 1. Read live sources
            double? gridValNullable = _liveValueReader(SourcesConfig.GridMeterKey);
            if (!gridValNullable.HasValue)
            {
                gridValNullable = _liveValueReader("meter-main-a_power") ?? _liveValueReader("meter-main_power");
            }
            double gridVal = gridValNullable ?? 0.0;

            double? pvValNullable = _liveValueReader(SourcesConfig.PvMeterKey);
            if (!pvValNullable.HasValue)
            {
                pvValNullable = _liveValueReader("pv-rooftop_power") ?? _liveValueReader("glueck-pv_power") ?? _liveValueReader("solis-pv_power");
            }
            double pvVal = pvValNullable ?? 0.0;

            double battPowerVal = _liveValueReader(SourcesConfig.BatteryPowerKey) ?? 0.0;
            double battSocVal = _liveValueReader(SourcesConfig.BatterySocKey) ?? 100.0;

            LiveGridKw = Math.Round(gridVal, 2);
            LivePvKw = Math.Round(pvVal, 2);
            LiveBatteryKw = Math.Round(battPowerVal, 2);
            LiveBatterySocPct = Math.Round(battSocVal, 1);

            double battCharge = battPowerVal < -0.2 ? Math.Abs(battPowerVal) : 0.0;
            LiveBatteryChargeKw = Math.Round(battCharge, 2);
            IsBatteryCharging = battCharge > 0.3 && battSocVal < SourcesConfig.BatteryFullSocPct;

            // 2. Read live consumers
            foreach (var consumer in Consumers)
            {
                double? telemetryVal = string.IsNullOrWhiteSpace(consumer.ActualPowerKey) 
                    ? null 
                    : _liveValueReader(consumer.ActualPowerKey);
                // If live telemetry is available, use it. Otherwise for uncontrollable loads use BasePowerKw
                double actual = telemetryVal ?? (consumer.HasOptionalTier ? 0.0 : consumer.BasePowerKw);
                consumer.ActualPowerKw = Math.Round(actual, 2);
            }

            // 3. Execute Dispatch Engine
            EnergyDispatchEngine.Dispatch(
                SourcesConfig,
                LiveGridKw,
                LivePvKw,
                LiveBatteryKw,
                LiveBatterySocPct,
                Consumers);

            // 4. Actuate controllable consumers
            var now = DateTime.UtcNow;
            foreach (var consumer in Consumers.Where(c => c.IsControllable && !string.IsNullOrWhiteSpace(c.ForcePowerKey)))
            {
                bool setpointChanged = Math.Abs(consumer.AllocatedPowerKw - consumer.LastWrittenSetpointKw) >= 0.3;
                bool refreshHeartbeat = (now - consumer.LastWrittenUtc).TotalSeconds >= 60;

                if (setpointChanged || refreshHeartbeat)
                {
                    consumer.LastWrittenSetpointKw = consumer.AllocatedPowerKw;
                    consumer.LastWrittenUtc = now;

                    try
                    {
                        bool ok = await _telemetryWriter(consumer.ForcePowerKey, consumer.AllocatedPowerKw);
                        if (ok)
                        {
                            string reason = consumer.AllocatedPowerKw > consumer.MaxOptionalKw
                                ? $"Battery charging at {LiveBatteryChargeKw:F1} kW (SoC {LiveBatterySocPct}%). Setpoint boosted to {consumer.AllocatedPowerKw:F1} kW."
                                : consumer.UnusedPowerKw > 0.1
                                    ? $"{consumer.Status}. Actual draw: {consumer.ActualPowerKw:F1} kW."
                                    : $"Setpoint {consumer.AllocatedPowerKw:F1} kW allocated. Actual draw: {consumer.ActualPowerKw:F1} kW.";

                            AddLog($"[{consumer.Name}] Set force_power = {consumer.AllocatedPowerKw:F1} kW. {reason}", consumer.Status);
                            Log.Info($"[EnergyControl] [{consumer.Name}] Set {consumer.ForcePowerKey} = {consumer.AllocatedPowerKw:F1} kW ({consumer.Status})");
                        }
                        else
                        {
                            AddLog($"[{consumer.Name}] Failed writing {consumer.AllocatedPowerKw:F1} kW to '{consumer.ForcePowerKey}'.", "Write Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[EnergyControl] Write failed for {consumer.ForcePowerKey}: {ex.Message}");
                    }
                }
            }

            // 5. Update Rolling 24-hour Energy Calculation
            double totalControllablePower = Consumers.Where(c => c.IsControllable).Sum(c => c.ActualPowerKw);
            double uncPower = Consumers.Where(c => !c.IsControllable).Sum(c => c.ActualPowerKw);
            if (uncPower <= 0.0)
            {
                double bal = (LiveGridKw + LivePvKw + LiveBatteryKw) - totalControllablePower;
                uncPower = Math.Max(0.0, Math.Round(bal, 2));
            }

            double rawBattCharge = LiveBatteryKw < -0.2 ? Math.Abs(LiveBatteryKw) : 0.0;
            double clampedCharge = Math.Min(rawBattCharge, SourcesConfig.BatteryMaxChargeKw);
            double surplusBatt = IsBatteryCharging ? Math.Max(0.0, clampedCharge - SourcesConfig.BatteryMinReserveKw) : 0.0;
            double expSurplus = LiveGridKw < -0.2 ? Math.Abs(LiveGridKw) : 0.0;
            double totalLiveSurplus = surplusBatt + expSurplus;

            _energyCalc.RecordSample(
                now,
                LiveGridKw,
                LivePvKw,
                LiveBatteryKw,
                uncPower,
                totalLiveSurplus,
                Consumers.Select(c => (c.Id, c.ActualPowerKw)));

            _cyclesSinceEnergySave++;
            if (_cyclesSinceEnergySave >= 30) // ~5 minutes
            {
                _cyclesSinceEnergySave = 0;
                if (_billingStore != null)
                {
                    try { _billingStore.SetSetting("ems_energy_24h_state", _energyCalc.Serialize()); } catch { }
                }
            }
        }

        private async Task BootstrapHistoricalEnergyAsync()
        {
            if (_telemetryStore == null) return;
            try
            {
                var now = DateTimeOffset.UtcNow;
                long startTs = now.AddHours(-24).ToUnixTimeMilliseconds();
                long endTs = now.ToUnixTimeMilliseconds();

                var keys = new List<string>();
                if (!string.IsNullOrWhiteSpace(SourcesConfig.GridMeterKey)) keys.Add(SourcesConfig.GridMeterKey);
                if (!string.IsNullOrWhiteSpace(SourcesConfig.PvMeterKey)) keys.Add(SourcesConfig.PvMeterKey);
                if (!string.IsNullOrWhiteSpace(SourcesConfig.BatteryPowerKey)) keys.Add(SourcesConfig.BatteryPowerKey);
                foreach (var c in Consumers)
                {
                    if (!string.IsNullOrWhiteSpace(c.ActualPowerKey))
                        keys.Add(c.ActualPowerKey);
                }

                if (keys.Count == 0) return;

                var pointsMap = await _telemetryStore.QueryMultipleAsync(keys, startTs, endTs, maxPointsPerKey: 3000);
                if (pointsMap != null && pointsMap.Count > 0)
                {
                    foreach (var (key, pts) in pointsMap)
                    {
                        if (pts == null || pts.Count < 2) continue;
                        var sorted = pts.Where(p => p.Value.HasValue).OrderBy(p => p.Ts).ToList();
                        for (int i = 1; i < sorted.Count; i++)
                        {
                            long dtMs = sorted[i].Ts - sorted[i - 1].Ts;
                            if (dtMs <= 0 || dtMs > 300_000) continue;
                            double dtHours = dtMs / 3600000.0;
                            double vPrev = sorted[i - 1].Value!.Value;
                            double vCurr = sorted[i].Value!.Value;
                            long minKey = sorted[i].Ts / 60000;

                            if (key == SourcesConfig.GridMeterKey)
                            {
                                double imp = Math.Max(0.0, (Math.Max(0.0, vPrev) + Math.Max(0.0, vCurr)) * 0.5 * dtHours);
                                double exp = Math.Max(0.0, (Math.Max(0.0, -vPrev) + Math.Max(0.0, -vCurr)) * 0.5 * dtHours);
                                _energyCalc.AddEnergy(minKey, gridImportKwh: imp, gridExportKwh: exp);
                            }
                            else if (key == SourcesConfig.PvMeterKey)
                            {
                                double pv = Math.Max(0.0, (Math.Max(0.0, vPrev) + Math.Max(0.0, vCurr)) * 0.5 * dtHours);
                                _energyCalc.AddEnergy(minKey, pvGenKwh: pv);
                            }
                            else if (key == SourcesConfig.BatteryPowerKey)
                            {
                                double chg = Math.Max(0.0, (Math.Max(0.0, -vPrev) + Math.Max(0.0, -vCurr)) * 0.5 * dtHours);
                                double dch = Math.Max(0.0, (Math.Max(0.0, vPrev) + Math.Max(0.0, vCurr)) * 0.5 * dtHours);
                                _energyCalc.AddEnergy(minKey, battChargeKwh: chg, battDischargeKwh: dch);
                            }
                            else
                            {
                                var consumer = Consumers.FirstOrDefault(c => c.ActualPowerKey == key);
                                if (consumer != null)
                                {
                                    double cons = Math.Max(0.0, (Math.Max(0.0, vPrev) + Math.Max(0.0, vCurr)) * 0.5 * dtHours);
                                    _energyCalc.AddEnergy(minKey, consumerKwh: new Dictionary<string, double> { [consumer.Id] = cons });
                                }
                            }
                        }
                    }
                    Log.Info("[EnergyControl] Bootstrapped rolling 24h energy from historical telemetry store.");
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[EnergyControl] Historical energy bootstrap skipped: {ex.Message}");
            }
        }
    }

    public record EmsLogEntry(DateTime Timestamp, string Message, string State);

    public record TrajectoryLogEntry(DateTime Timestamp, string Message, string State) : EmsLogEntry(Timestamp, Message, State);

    /// <summary>
    /// Backward-compatibility shim mapping legacy TrajectoryService calls to EmsService.
    /// </summary>
    public static class TrajectoryService
    {
        public static EmsService Instance => EmsService.Instance;
    }
}
