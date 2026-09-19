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

    /// <summary>
    /// Fixed sign convention used internally by the EMS for all power flows.
    /// <para>
    /// The EMS is a pool model: all sources feed a shared power pool and all consumers
    /// draw from it. There is exactly one rule, applied uniformly to every power key:
    /// <b>positive = into the pool (supply), negative = out of the pool (sink)</b>.
    /// </para>
    /// <para>
    /// Every raw telemetry value is normalized into this convention at the read boundary
    /// (see <see cref="EnergySourcesConfig.NormalizeGridPower"/> etc.) so that all
    /// downstream calculations (dispatch, surplus, residual load, energy integration)
    /// operate on a single, unambiguous sign rule.
    /// </para>
    /// </summary>
    public static class PowerSignConvention
    {
        /// <summary>
        /// The single unifying rule: positive = into the pool (supply), negative = out of the pool (sink).
        /// </summary>
        public const string Pool = "+ = into pool, - = out of pool";

        /// <summary>True when the value represents power flowing into the pool (supply).</summary>
        public static bool IsSupply(double normalizedKw) => normalizedKw > 0.0;

        /// <summary>True when the value represents power flowing out of the pool (sink).</summary>
        public static bool IsSink(double normalizedKw) => normalizedKw < 0.0;

        /// <summary>
        /// Returns the supply (into-pool) portion of a normalized value, or 0.
        /// Values below <paramref name="deadbandKw"/> are treated as zero to suppress sensor noise.
        /// </summary>
        public static double SupplyPart(double normalizedKw, double deadbandKw = 0.0)
            => normalizedKw > deadbandKw ? normalizedKw : 0.0;

        /// <summary>
        /// Returns the sink (out-of-pool) portion of a normalized value as a positive magnitude, or 0.
        /// Values below <paramref name="deadbandKw"/> are treated as zero to suppress sensor noise.
        /// </summary>
        public static double SinkPart(double normalizedKw, double deadbandKw = 0.0)
            => normalizedKw < -deadbandKw ? -normalizedKw : 0.0;
    }

    public class EnergySourcesConfig
    {
        public string GridMeterKey { get; set; } = "meter-main-a_power";
        public double GridMaxImportKw { get; set; } = 8.0;
        public string PvMeterKey { get; set; } = "pv-rooftop_power";
        public bool HasBattery { get; set; } = true;
        public string BatteryPowerKey { get; set; } = "solis-battery_power";
        public string BatterySocKey { get; set; } = "solis-battery_battery_soc";

        // ── Sign Convention Rules ─────────────────────────────────────────────
        // One rule (see PowerSignConvention.Pool): + = into pool, - = out of pool.
        // If a meter reports the opposite direction, set the matching Invert* flag.
        // All EMS calculations consume normalized values only.

        /// <summary>
        /// Negates the pool rule for the grid key. Enable when the meter reports export as
        /// positive and import as negative (i.e. the meter's sign points out of the pool).
        /// </summary>
        public bool InvertGridPowerSign { get; set; } = false;

        /// <summary>
        /// Negates the pool rule for the PV key. Enable when the meter reports generation as
        /// negative (e.g. feed-in meter convention) instead of feeding the pool positively.
        /// </summary>
        public bool InvertPvPowerSign { get; set; } = false;

        /// <summary>
        /// Negates the pool rule for the battery key. Enable when the meter reports charging as
        /// positive and discharging as negative (i.e. the meter's sign points out of the pool).
        /// </summary>
        public bool InvertBatteryPowerSign { get; set; } = false;

        /// <summary>Applies the pool sign rule to a raw value, optionally negating it.</summary>
        private static double Normalize(double raw, bool invert) => invert ? -raw : raw;

        /// <summary>Applies the pool sign rule to a raw grid telemetry value.</summary>
        public double NormalizeGridPower(double raw) => Normalize(raw, InvertGridPowerSign);

        /// <summary>Applies the pool sign rule to a raw PV telemetry value.</summary>
        public double NormalizePvPower(double raw) => Normalize(raw, InvertPvPowerSign);

        /// <summary>Applies the pool sign rule to a raw battery telemetry value.</summary>
        public double NormalizeBatteryPower(double raw) => Normalize(raw, InvertBatteryPowerSign);

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
        /// Percentage of the optional tier added per dispatch cycle while the consumer is running.
        /// The default reaches full power in about three minutes at the ten-second dispatch interval.
        /// </summary>
        public double RampPercentPerCycle { get; set; } = 100.0 / 18.0;

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
        public double PvPowerKw { get; set; }
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

        /// <summary>
        /// Total site consumption (sink side of the pool balance) in kW.
        /// </summary>
        public double TotalConsumptionKw { get; set; }

        /// <summary>
        /// Autarky / self-sufficiency: share of consumption not covered by grid import, in percent (0-100).
        /// </summary>
        public double AutarkyPct { get; set; }

        // ── Rolling 24-Hour Calculated Energy (kWh) ───────────────────────────
        public double GridImport24hKwh { get; set; }
        public double GridExport24hKwh { get; set; }
        public double PvGeneration24hKwh { get; set; }
        public double BatteryCharged24hKwh { get; set; }
        public double BatteryDischarged24hKwh { get; set; }
        public double Uncontrollable24hKwh { get; set; }
        public double TotalSurplus24hKwh { get; set; }
        public double TotalControllable24hKwh { get; set; }

        /// <summary>
        /// Total site consumption over the rolling 24-hour window in kWh.
        /// </summary>
        public double TotalConsumption24hKwh { get; set; }

        /// <summary>
        /// Autarky / self-sufficiency over the rolling 24-hour window, in percent (0-100).
        /// </summary>
        public double Autarky24hPct { get; set; }

        public List<EnergyConsumer> Consumers { get; set; } = new();
        public List<EmsLogEntry> Logs { get; set; } = new();
        public EnergySourcesConfig Sources { get; set; } = new();
    }

    // ── Pure Dispatch Engine ──────────────────────────────────────────────────

    public static class EnergyDispatchEngine
    {
        /// <summary>
        /// Grid-import deadband in kW. Imports below this are treated as zero
        /// (measurement noise / rounding) and do not trigger curtailment.
        /// </summary>
        public const double GridImportDeadbandKw = 0.2;

        /// <summary>
        /// Autarky dispatch: consumers may draw available power as long as the site does
        /// not import from the grid. PV generation and battery discharge headroom limit
        /// the optional tier; once both are exhausted, no extra load is allocated.
        /// <para>
        /// All power arguments are expected to be <b>already normalized</b> to the pool sign
        /// rule (see <see cref="PowerSignConvention"/>): positive = into the pool (supply),
        /// negative = out of the pool (sink). PV/battery only matter insofar as they keep
        /// the grid import at zero — no explicit surplus calculation is needed.
        /// </para>
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

            // Pool rule: grid positive = import (into pool).
            double gridImportKw = PowerSignConvention.SupplyPart(gridPowerKw);
            bool curtail = gridImportKw > GridImportDeadbandKw;

            double availableSourceHeadroomKw = double.PositiveInfinity;
            if (sources?.HasBattery == true)
            {
                bool aboveReserve = batterySocPct > sources.BatteryMinSocPct;
                double currentDischargeKw = PowerSignConvention.SupplyPart(batteryPowerKw, GridImportDeadbandKw);
                double batteryHeadroomKw = aboveReserve
                    ? Math.Max(0.0, sources.BatteryMaxDischargeKw - currentDischargeKw)
                    : 0.0;
                availableSourceHeadroomKw = PowerSignConvention.SupplyPart(pvPowerKw, GridImportDeadbandKw) + batteryHeadroomKw;
            }

            // Classify consumers.
            var controllable = new List<EnergyConsumer>();
            foreach (var consumer in consumers)
            {
                if (!consumer.HasOptionalTier || consumer.MaxOptionalKw <= 0.0)
                {
                    // Uncontrollable loads cannot be dispatched; report measured/base draw.
                    consumer.AllocatedOptionalKw = 0.0;
                    consumer.AllocatedPowerKw = Math.Max(consumer.BasePowerKw, consumer.ActualPowerKw);
                    consumer.UnusedPowerKw = 0.0;
                    consumer.IsActivelyDemanding = false;
                    consumer.Status = "Uncontrollable Load";
                }
                else
                {
                    controllable.Add(consumer);
                }
            }

            double remainingSourceHeadroomKw = availableSourceHeadroomKw;
            foreach (var consumer in controllable.OrderBy(c => c.Priority))
            {
                double actualOpt = Math.Max(0.0, consumer.ActualPowerKw - consumer.BasePowerKw);
                consumer.IsActivelyDemanding = actualOpt > Math.Max(0.1, consumer.StandbyOptionalKw);

                // Guaranteed minimum optional tier (e.g. 1.38 kW = 6A 1-phase EV charging).
                // Kept alive even during grid-import curtailment so consumers do not drop
                // out entirely; only consumers without a minimum fall to zero.
                double guaranteedOpt = Math.Min(
                    Math.Max(0.0, consumer.MinOptionalKw),
                    Math.Max(0.0, consumer.MaxOptionalKw));

                if (curtail)
                {
                    // Grid import: controllable consumers drop to their guaranteed minimum
                    // tier (0 if none configured). The uncontrollable base tier keeps running.
                    consumer.AllocatedOptionalKw = Math.Round(guaranteedOpt, 2);
                    consumer.AllocatedPowerKw = Math.Round(consumer.BasePowerKw + guaranteedOpt, 2);
                    consumer.UnusedPowerKw = 0.0;
                    consumer.Status = guaranteedOpt > 0.0
                        ? $"Grid Import Curtailment (Min {guaranteedOpt:F2} kW)"
                        : "Grid Import Curtailment";
                }
                else
                {
                    // Autarky: consume one shared source budget in priority order.
                    double physicalMaxOpt = Math.Min(
                        consumer.MaxOptionalKw,
                        Math.Max(0.0, consumer.MaxPowerKw - consumer.BasePowerKw));
                    double requestedOpt = guaranteedOpt;
                    if (consumer.IsActivelyDemanding)
                    {
                        double step = physicalMaxOpt * Math.Max(0.0, consumer.RampPercentPerCycle) / 100.0;
                        requestedOpt = consumer.AllocatedOptionalKw >= guaranteedOpt &&
                            (consumer.AllocatedOptionalKw > 0.0 || guaranteedOpt > 0.0)
                            ? consumer.AllocatedOptionalKw + step
                            : guaranteedOpt > 0.0
                                ? guaranteedOpt
                                : step;
                    }

                    double sourceAvailableKw = remainingSourceHeadroomKw;
                    double maxOpt = Math.Min(physicalMaxOpt, requestedOpt);
                    maxOpt = Math.Min(maxOpt, remainingSourceHeadroomKw);
                    if (!double.IsPositiveInfinity(remainingSourceHeadroomKw))
                    {
                        remainingSourceHeadroomKw = Math.Max(0.0, remainingSourceHeadroomKw - maxOpt);
                    }
                    consumer.AllocatedOptionalKw = Math.Round(maxOpt, 2);
                    consumer.AllocatedPowerKw = Math.Round(consumer.BasePowerKw + maxOpt, 2);
                    consumer.UnusedPowerKw = 0.0;
                    bool sourceLimited = maxOpt < physicalMaxOpt && sourceAvailableKw < physicalMaxOpt;
                    consumer.Status = sourceLimited
                        ? "Autarky (Source Limited)"
                        : consumer.IsActivelyDemanding
                            ? "Autarky (Unrestricted)"
                            : "Ready (Autarky)";
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

        public double BatteryPowerKw => LiveBatteryKw;
        public double BatteryChargeKw => LiveBatteryChargeKw;
        public double BatterySocPct => LiveBatterySocPct;
        public string BatteryPowerKey => SourcesConfig.BatteryPowerKey;
        public string BatterySocKey => SourcesConfig.BatterySocKey;
        public double BatteryReserveKw => SourcesConfig.BatteryMinReserveKw;
        public double BatteryMaxPowerKw => SourcesConfig.BatteryMaxPowerKw;
        public double BatteryMaxChargeKw => SourcesConfig.BatteryMaxChargeKw;
        public double BatteryMaxDischargeKw => SourcesConfig.BatteryMaxDischargeKw;

        public event Action<Dictionary<string, object>>? OnTelemetryUpdated;

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            if (_billingStore != null)
            {
                _billingStore.SetTariff("energy_control_enabled", enabled ? 1 : 0);
            }
            PublishTelemetries();
        }

        public void SetLiveTelemetryForTesting(double gridKw, double pvKw, double batteryKw = 0.0, double batterySocPct = 100.0)
        {
            // Raw values are normalized through the configured sign rules, exactly like the live read boundary.
            double gridNorm = SourcesConfig.NormalizeGridPower(gridKw);
            double pvNorm = SourcesConfig.NormalizePvPower(pvKw);
            double battNorm = SourcesConfig.NormalizeBatteryPower(batteryKw);

            LiveGridKw = Math.Round(gridNorm, 2);
            LivePvKw = Math.Round(pvNorm, 2);
            LiveBatteryKw = SourcesConfig.HasBattery ? Math.Round(battNorm, 2) : 0.0;
            LiveBatterySocPct = SourcesConfig.HasBattery ? Math.Round(batterySocPct, 1) : 0.0;
            // Pool rule: battery charging is the sink (out-of-pool) portion of the battery value.
            double battCharge = SourcesConfig.HasBattery ? PowerSignConvention.SinkPart(battNorm, 0.2) : 0.0;
            LiveBatteryChargeKw = Math.Round(battCharge, 2);
            IsBatteryCharging = SourcesConfig.HasBattery && battCharge > 0.3 && batterySocPct < SourcesConfig.BatteryFullSocPct;
        }

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
            PublishTelemetries();
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
                    HasBattery = !string.IsNullOrWhiteSpace(_billingStore.GetSetting("energy_control_battery_power_key", "solis-battery_power")),
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
                        // Ignore legacy demo base loads so they do not obstruct calculated base loads
                        valid = valid.Where(c => c.Id != "base-building-infrastructure" && c.Id != "base-server-room").ToList();
                        if (valid.Count > 0) Consumers = valid;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"[EnergyControl] Failed to parse consumers config: {ex.Message}");
                }
            }

            // No demo fallback consumers: an empty consumer list is a valid state.
            // Consumers are created explicitly via the dashboard; uncontrollable base
            // loads are calculated dynamically from the site energy balance.

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

        public void Configure(EnergySourcesConfig sources)
        {
            SourcesConfig = sources;
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

            }

            PublishTelemetries();
        }

        public EnergySystemSnapshot GetSnapshot()
        {
            double totalControllable = Consumers.Where(c => c.HasOptionalTier && c.MaxOptionalKw > 0.0).Sum(c => c.ActualPowerKw);
            double totalUncontrollable = Consumers.Where(c => !c.HasOptionalTier || c.MaxOptionalKw <= 0.0).Sum(c => c.ActualPowerKw);
            double totalBaseTier = Consumers.Sum(c => c.BasePowerKw);
            double totalOptional = Consumers.Sum(c => c.AllocatedOptionalKw);
            double totalReclaimed = Consumers.Sum(c => c.UnusedPowerKw);

            // Uncontrollable base load is calculated dynamically from the site energy balance by default.
            // Pool balance: everything flowing into the pool (supply) minus everything drawn from it (sink).
            //   Supply = Grid import + PV generation + Battery discharge
            //   Sink   = Grid export + Battery charge + Controllable consumers + Uncontrollable base load
            // Solving for the residual uncontrollable base load:
            //   Residual = (Grid + PV + Battery) - Controllable
            // All values already follow the pool sign rule (positive = into the pool).
            double effectivePv = LivePvKw;
            double effectiveBatt = SourcesConfig.HasBattery ? LiveBatteryKw : 0.0;
            double balanceLoad = (LiveGridKw + effectivePv + effectiveBatt) - totalControllable;
            double calculatedBaseLoad = Math.Max(0.0, Math.Round(balanceLoad, 2));

            // If sub-metered uncontrollable consumers are explicitly configured, ensure total reflects measured sub-meters or balance
            totalUncontrollable = totalUncontrollable > 0.0
                ? Math.Max(totalUncontrollable, calculatedBaseLoad)
                : calculatedBaseLoad;

            double rawCharge = SourcesConfig.HasBattery ? PowerSignConvention.SinkPart(LiveBatteryKw, 0.2) : 0.0;
            double battCharge = Math.Min(rawCharge, SourcesConfig.BatteryMaxChargeKw);
            double battSurplus = (SourcesConfig.HasBattery && IsBatteryCharging) 
                ? Math.Max(0.0, battCharge - SourcesConfig.BatteryMinReserveKw) 
                : 0.0;
            double exportSurplus = PowerSignConvention.SinkPart(LiveGridKw, 0.2);
            double totalSurplus = battSurplus + exportSurplus;

            // ── Consumption & Autarky ─────────────────────────────────────────
            // Consumption is the site's own load: controllable + uncontrollable consumers.
            double gridImportKw = PowerSignConvention.SupplyPart(LiveGridKw);
            double totalConsumption = totalControllable + totalUncontrollable;

            // Autarky = 1 - (grid import / consumption), i.e. the share of consumption not covered by the grid.
            double autarkyPct = totalConsumption > 0.01
                ? Math.Round(Math.Clamp((1.0 - gridImportKw / totalConsumption) * 100.0, 0.0, 100.0), 1)
                : 100.0;

            // Compute rolling 24-hour energy totals
            var energyTotals = _energyCalc.Get24hTotals(DateTime.UtcNow);
            foreach (var consumer in Consumers)
            {
                if (energyTotals.ConsumerKwh.TryGetValue(consumer.Id, out double ckwh))
                    consumer.Energy24hKwh = ckwh;
                else
                    consumer.Energy24hKwh = 0.0;
            }

            // 24h consumption = controllable + uncontrollable
            double consumption24h = energyTotals.ConsumerKwh.Values.Sum() + energyTotals.UncontrollableKwh;
            double autarky24hPct = consumption24h > 0.01
                ? Math.Round(Math.Clamp((1.0 - energyTotals.GridImportKwh / consumption24h) * 100.0, 0.0, 100.0), 1)
                : 100.0;

            return new EnergySystemSnapshot
            {
                Enabled = Enabled,
                GridImportKw = LiveGridKw,
                GridMaxImportKw = SourcesConfig.GridMaxImportKw,
                PvPowerKw = LivePvKw,
                // Pool rule: generation is the into-pool (positive) part.
                PvGenerationKw = PowerSignConvention.SupplyPart(LivePvKw),
                BatteryPowerKw = SourcesConfig.HasBattery ? LiveBatteryKw : 0.0,
                BatteryChargeKw = SourcesConfig.HasBattery ? LiveBatteryChargeKw : 0.0,
                BatterySocPct = SourcesConfig.HasBattery ? LiveBatterySocPct : 0.0,
                BatteryMaxPowerKw = SourcesConfig.HasBattery ? SourcesConfig.BatteryMaxPowerKw : 0.0,
                BatteryMaxChargeKw = SourcesConfig.HasBattery ? SourcesConfig.BatteryMaxChargeKw : 0.0,
                BatteryMaxDischargeKw = SourcesConfig.HasBattery ? SourcesConfig.BatteryMaxDischargeKw : 0.0,
                IsBatteryCharging = SourcesConfig.HasBattery && IsBatteryCharging,
                TotalSurplusAvailableKw = Math.Round(totalSurplus, 2),
                TotalBaseLoadKw = Math.Round(totalBaseTier, 2),
                TotalOptionalLoadKw = Math.Round(totalOptional, 2),
                TotalReclaimedPowerKw = Math.Round(totalReclaimed, 2),
                UncontrollableLoadKw = Math.Round(totalUncontrollable, 2),
                TotalControllableLoadKw = Math.Round(totalControllable, 2),
                TotalConsumptionKw = Math.Round(totalConsumption, 2),
                AutarkyPct = autarkyPct,

                // 24h Rolling Day Calculated Energy (in kWh)
                GridImport24hKwh = energyTotals.GridImportKwh,
                GridExport24hKwh = energyTotals.GridExportKwh,
                PvGeneration24hKwh = energyTotals.PvGenerationKwh,
                BatteryCharged24hKwh = SourcesConfig.HasBattery ? energyTotals.BatteryChargedKwh : 0.0,
                BatteryDischarged24hKwh = SourcesConfig.HasBattery ? energyTotals.BatteryDischargedKwh : 0.0,
                Uncontrollable24hKwh = energyTotals.UncontrollableKwh,
                TotalSurplus24hKwh = energyTotals.TotalSurplusKwh,
                TotalControllable24hKwh = Math.Round(energyTotals.ConsumerKwh.Values.Sum(), 2),
                TotalConsumption24hKwh = Math.Round(consumption24h, 2),
                Autarky24hPct = autarky24hPct,

                Consumers = Consumers.ToList(),
                Logs = GetLogs(),
                Sources = SourcesConfig
            };
        }

        public static readonly string[] StandardTelemetryKeys = new[]
        {
            "grid_import",
            "grid_export",
            "grid_power",
            "pv_power",
            "pv_generation",
            "battery_power",
            "battery_charge",
            "battery_soc",
            "surplus_power",
            "uncontrollable_load",
            "controllable_load",
            "total_consumption",
            "autarky",
            "total_base_load",
            "total_optional_load",
            "reclaimed_power",
            "enabled",
            "grid_max_import",

            "grid_import_24h",
            "grid_export_24h",
            "pv_generation_24h",
            "pv_consumption_24h",
            "battery_charged_24h",
            "battery_discharged_24h",
            "uncontrollable_24h",
            "controllable_24h",
            "total_surplus_24h",
            "total_consumption_24h",
            "autarky_24h"
        };

        public Dictionary<string, object> GetTelemetryValues()
        {
            var snap = GetSnapshot();
            var dict = new Dictionary<string, object>
            {
                ["grid_import"] = PowerSignConvention.SupplyPart(snap.GridImportKw),
                ["grid_export"] = PowerSignConvention.SinkPart(snap.GridImportKw),
                ["grid_power"] = snap.GridImportKw,
                ["pv_power"] = snap.PvPowerKw,
                ["pv_generation"] = snap.PvGenerationKw,
                ["battery_power"] = snap.BatteryPowerKw,
                ["battery_charge"] = snap.BatteryChargeKw,
                ["battery_soc"] = snap.BatterySocPct,
                ["surplus_power"] = snap.TotalSurplusAvailableKw,
                ["uncontrollable_load"] = snap.UncontrollableLoadKw,
                ["controllable_load"] = snap.TotalControllableLoadKw,
                ["total_consumption"] = snap.TotalConsumptionKw,
                ["autarky"] = snap.AutarkyPct,
                ["total_base_load"] = snap.TotalBaseLoadKw,
                ["total_optional_load"] = snap.TotalOptionalLoadKw,
                ["reclaimed_power"] = snap.TotalReclaimedPowerKw,
                ["enabled"] = snap.Enabled ? 1.0 : 0.0,
                ["grid_max_import"] = snap.GridMaxImportKw,

                ["grid_import_24h"] = snap.GridImport24hKwh,
                ["grid_export_24h"] = snap.GridExport24hKwh,
                ["pv_generation_24h"] = snap.PvGeneration24hKwh,
                ["battery_charged_24h"] = snap.BatteryCharged24hKwh,
                ["battery_discharged_24h"] = snap.BatteryDischarged24hKwh,
                ["uncontrollable_24h"] = snap.Uncontrollable24hKwh,
                ["controllable_24h"] = snap.TotalControllable24hKwh,
                ["total_surplus_24h"] = snap.TotalSurplus24hKwh,
                ["total_consumption_24h"] = snap.TotalConsumption24hKwh,
                ["autarky_24h"] = snap.Autarky24hPct
            };

            foreach (var consumer in snap.Consumers)
            {
                if (string.IsNullOrWhiteSpace(consumer.Id)) continue;
                string cleanId = consumer.Id.ToLowerInvariant().Replace('-', '_');
                dict[$"{cleanId}_actual_power"] = consumer.ActualPowerKw;
                dict[$"{cleanId}_allocated_power"] = consumer.AllocatedPowerKw;
                dict[$"{cleanId}_unused_power"] = consumer.UnusedPowerKw;
                dict[$"{cleanId}_energy_24h"] = consumer.Energy24hKwh;
            }

            return dict;
        }

        public IEnumerable<string> GetTelemetryKeys()
        {
            var list = new List<string>(StandardTelemetryKeys);
            foreach (var c in Consumers)
            {
                if (string.IsNullOrWhiteSpace(c.Id)) continue;
                string cleanId = c.Id.ToLowerInvariant().Replace('-', '_');
                list.Add($"{cleanId}_actual_power");
                list.Add($"{cleanId}_allocated_power");
                list.Add($"{cleanId}_unused_power");
                list.Add($"{cleanId}_energy_24h");
            }
            return list;
        }

        public IReadOnlyDictionary<string, string> GetTelemetryUnits()
        {
            var map = new Dictionary<string, string>
            {
                ["grid_import"] = Units.Kilowatt,
                ["grid_export"] = Units.Kilowatt,
                ["grid_power"] = Units.Kilowatt,
                ["pv_power"] = Units.Kilowatt,
                ["pv_generation"] = Units.Kilowatt,
                ["battery_power"] = Units.Kilowatt,
                ["battery_charge"] = Units.Kilowatt,
                ["battery_soc"] = Units.Percent,
                ["surplus_power"] = Units.Kilowatt,
                ["uncontrollable_load"] = Units.Kilowatt,
                ["controllable_load"] = Units.Kilowatt,
                ["total_consumption"] = Units.Kilowatt,
                ["autarky"] = Units.Percent,
                ["total_base_load"] = Units.Kilowatt,
                ["total_optional_load"] = Units.Kilowatt,
                ["reclaimed_power"] = Units.Kilowatt,
                ["enabled"] = "",
                ["grid_max_import"] = Units.Kilowatt,

                ["grid_import_24h"] = Units.KilowattHour,
                ["grid_export_24h"] = Units.KilowattHour,
                ["pv_generation_24h"] = Units.KilowattHour,
                ["battery_charged_24h"] = Units.KilowattHour,
                ["battery_discharged_24h"] = Units.KilowattHour,
                ["uncontrollable_24h"] = Units.KilowattHour,
                ["controllable_24h"] = Units.KilowattHour,
                ["total_surplus_24h"] = Units.KilowattHour,
                ["total_consumption_24h"] = Units.KilowattHour,
                ["autarky_24h"] = Units.Percent
            };

            foreach (var c in Consumers)
            {
                if (string.IsNullOrWhiteSpace(c.Id)) continue;
                string cleanId = c.Id.ToLowerInvariant().Replace('-', '_');
                map[$"{cleanId}_actual_power"] = Units.Kilowatt;
                map[$"{cleanId}_allocated_power"] = Units.Kilowatt;
                map[$"{cleanId}_unused_power"] = Units.Kilowatt;
                map[$"{cleanId}_energy_24h"] = Units.KilowattHour;
            }

            return map;
        }

        public string GetTelemetryFriendlyName(string key)
        {
            return key switch
            {
                "grid_import" => "Grid Import Power",
                "grid_export" => "Grid Export Power",
                "grid_power" => "Net Grid Power",
                "pv_power" => "Solar PV Power",
                "pv_generation" => "Solar PV Generation",
                "battery_power" => "Battery Power",
                "battery_charge" => "Battery Charging Power",
                "battery_soc" => "Battery State of Charge",
                "surplus_power" => "Available Solar Surplus Pool",
                "uncontrollable_load" => "Uncontrollable Base Load",
                "controllable_load" => "Total Controllable Consumers Load",
                "total_consumption" => "Total Site Consumption",
                "autarky" => "Autarky (Self-Sufficiency)",
                "total_base_load" => "Total Base Tier Quota",
                "total_optional_load" => "Allocated Optional Load",
                "reclaimed_power" => "Reclaimed Idle Power",
                "enabled" => "EMS Master Enable",
                "grid_max_import" => "Grid Import Limit",

                "grid_import_24h" => "Rolling 24h Grid Import",
                "grid_export_24h" => "Rolling 24h Grid Export",
                "pv_generation_24h" => "Rolling 24h PV Generation",
                "battery_charged_24h" => "Rolling 24h Battery Charged",
                "battery_discharged_24h" => "Rolling 24h Battery Discharged",
                "uncontrollable_24h" => "Rolling 24h Uncontrollable Base Energy",
                "controllable_24h" => "Rolling 24h Controllable Load Energy",
                "total_surplus_24h" => "Rolling 24h Solar Surplus Energy",
                "total_consumption_24h" => "Rolling 24h Total Consumption",
                "autarky_24h" => "Rolling 24h Autarky (Self-Sufficiency)",
                _ => FormatConsumerKeyName(key)
            };
        }

        private string FormatConsumerKeyName(string key)
        {
            foreach (var c in Consumers)
            {
                if (string.IsNullOrWhiteSpace(c.Id)) continue;
                string cleanId = c.Id.ToLowerInvariant().Replace('-', '_');
                if (key.StartsWith(cleanId + "_"))
                {
                    string suffix = key.Substring(cleanId.Length + 1);
                    string suffixName = suffix switch
                    {
                        "actual_power" => "Actual Power",
                        "allocated_power" => "Allocated Setpoint",
                        "unused_power" => "Unused / Shared Power",
                        "energy_24h" => "Rolling 24h Consumed Energy",
                        _ => suffix.Replace('_', ' ')
                    };
                    return $"{c.Name} {suffixName}";
                }
            }
            return key.Replace('_', ' ');
        }

        public void PublishTelemetries()
        {
            try
            {
                var dict = GetTelemetryValues();
                OnTelemetryUpdated?.Invoke(dict);
            }
            catch (Exception ex)
            {
                Log.Warning($"[EnergyControl] Failed publishing telemetries: {ex.Message}");
            }
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
                        foreach (var c in Consumers.Where(c => c.HasOptionalTier && c.MaxOptionalKw > 0.0))
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
            // Normalize raw values into the pool rule.
            double gridVal = SourcesConfig.NormalizeGridPower(gridValNullable ?? 0.0);

            double? pvValNullable = _liveValueReader(SourcesConfig.PvMeterKey);
            if (!pvValNullable.HasValue)
            {
                pvValNullable = _liveValueReader("pv-rooftop_power") ?? _liveValueReader("glueck-pv_power") ?? _liveValueReader("solis-pv_power");
            }
            double pvVal = SourcesConfig.NormalizePvPower(pvValNullable ?? 0.0);

            double battPowerVal = 0.0;
            double battSocVal = 0.0;
            if (SourcesConfig.HasBattery && !string.IsNullOrWhiteSpace(SourcesConfig.BatteryPowerKey))
            {
                battPowerVal = SourcesConfig.NormalizeBatteryPower(_liveValueReader(SourcesConfig.BatteryPowerKey) ?? 0.0);
                battSocVal = _liveValueReader(SourcesConfig.BatterySocKey) ?? 100.0;
            }

            LiveGridKw = Math.Round(gridVal, 2);
            LivePvKw = Math.Round(pvVal, 2);
            LiveBatteryKw = Math.Round(battPowerVal, 2);
            LiveBatterySocPct = Math.Round(battSocVal, 1);

            double battCharge = SourcesConfig.HasBattery ? PowerSignConvention.SinkPart(battPowerVal, 0.2) : 0.0;
            LiveBatteryChargeKw = Math.Round(battCharge, 2);
            IsBatteryCharging = SourcesConfig.HasBattery && battCharge > 0.3 && battSocVal < SourcesConfig.BatteryFullSocPct;

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

            // 4. Actuate controllable consumers using the dispatch allocation as the setpoint.
            var now = DateTime.UtcNow;
            foreach (var consumer in Consumers.Where(c => c.HasOptionalTier && c.MaxOptionalKw > 0.0 && !string.IsNullOrWhiteSpace(c.ForcePowerKey)))
            {
                double writeValue = Math.Max(0.0, consumer.AllocatedPowerKw);

                bool setpointChanged = Math.Abs(writeValue - consumer.LastWrittenSetpointKw) >= 0.3;
                bool refreshHeartbeat = (now - consumer.LastWrittenUtc).TotalSeconds >= 60;

                if (setpointChanged || refreshHeartbeat)
                {
                    consumer.LastWrittenSetpointKw = writeValue;
                    consumer.LastWrittenUtc = now;

                    try
                    {
                        bool ok = await _telemetryWriter(consumer.ForcePowerKey, writeValue);
                        if (ok)
                        {
                            AddLog($"[{consumer.Name}] Set force_power = {writeValue:F2} kW.", consumer.Status);
                            Log.Info($"[EnergyControl] [{consumer.Name}] Set {consumer.ForcePowerKey} = {writeValue} kW ({consumer.Status})");
                        }
                        else
                        {
                            AddLog($"[{consumer.Name}] Failed writing {writeValue} kW to '{consumer.ForcePowerKey}'.", "Write Error");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[EnergyControl] Write failed for {consumer.ForcePowerKey}: {ex.Message}");
                    }
                }
            }

            // 5. Update Rolling 24-hour Energy Calculation
            double totalControllablePower = Consumers.Where(c => c.HasOptionalTier && c.MaxOptionalKw > 0.0).Sum(c => c.ActualPowerKw);
            double uncPower = Consumers.Where(c => !c.HasOptionalTier || c.MaxOptionalKw <= 0.0).Sum(c => c.ActualPowerKw);
            // Pool balance: supply (Grid + PV + Battery, all positive = into the pool) minus controllable draw.
            double effectivePv = LivePvKw;
            double effectiveBatt = SourcesConfig.HasBattery ? LiveBatteryKw : 0.0;
            double bal = (LiveGridKw + effectivePv + effectiveBatt) - totalControllablePower;
            double calculatedBal = Math.Max(0.0, Math.Round(bal, 2));
            uncPower = uncPower > 0.0 ? Math.Max(uncPower, calculatedBal) : calculatedBal;

            double rawBattCharge = SourcesConfig.HasBattery ? PowerSignConvention.SinkPart(LiveBatteryKw, 0.2) : 0.0;
            double clampedCharge = Math.Min(rawBattCharge, SourcesConfig.BatteryMaxChargeKw);
            double surplusBatt = (SourcesConfig.HasBattery && IsBatteryCharging) ? Math.Max(0.0, clampedCharge - SourcesConfig.BatteryMinReserveKw) : 0.0;
            double expSurplus = PowerSignConvention.SinkPart(LiveGridKw, 0.2);
            double totalLiveSurplus = surplusBatt + expSurplus;

            _energyCalc.RecordSample(
                now,
                LiveGridKw,
                LivePvKw,
                SourcesConfig.HasBattery ? LiveBatteryKw : 0.0,
                uncPower,
                totalLiveSurplus,
                Consumers.Select(c => (c.Id, c.ActualPowerKw)));

            PublishTelemetries();

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
                if (SourcesConfig.HasBattery && !string.IsNullOrWhiteSpace(SourcesConfig.BatteryPowerKey)) keys.Add(SourcesConfig.BatteryPowerKey);
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
                                double vPrevN = SourcesConfig.NormalizeGridPower(vPrev);
                                double vCurrN = SourcesConfig.NormalizeGridPower(vCurr);
                                double imp = Math.Max(0.0, (Math.Max(0.0, vPrevN) + Math.Max(0.0, vCurrN)) * 0.5 * dtHours);
                                double exp = Math.Max(0.0, (Math.Max(0.0, -vPrevN) + Math.Max(0.0, -vCurrN)) * 0.5 * dtHours);
                                _energyCalc.AddEnergy(minKey, gridImportKwh: imp, gridExportKwh: exp);
                            }
                            else if (key == SourcesConfig.PvMeterKey)
                            {
                                double vPrevN = SourcesConfig.NormalizePvPower(vPrev);
                                double vCurrN = SourcesConfig.NormalizePvPower(vCurr);
                                double pv = Math.Max(0.0, (vPrevN + vCurrN) * 0.5 * dtHours);
                                _energyCalc.AddEnergy(minKey, pvGenKwh: pv);
                            }
                            else if (key == SourcesConfig.BatteryPowerKey)
                            {
                                double vPrevN = SourcesConfig.NormalizeBatteryPower(vPrev);
                                double vCurrN = SourcesConfig.NormalizeBatteryPower(vCurr);
                                double chg = Math.Max(0.0, (Math.Max(0.0, -vPrevN) + Math.Max(0.0, -vCurrN)) * 0.5 * dtHours);
                                double dch = Math.Max(0.0, (Math.Max(0.0, vPrevN) + Math.Max(0.0, vCurrN)) * 0.5 * dtHours);
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
}
