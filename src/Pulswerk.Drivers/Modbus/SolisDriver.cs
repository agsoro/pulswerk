// SolisDriver.cs – Solis S6 hybrid / string inverter driver (Modbus TCP)
//
//  Register map (Function Code 0x04 - Read Input Registers):
//
//    Block 1 (33029..33038, 10 regs): PV Energy
//      33029..33030  uint32   energy_pv                    Total PV generation (kWh)
//      33035         uint16   energy_today_pv              Today PV generation (0.1 kWh)
//
//    Block 2 (33049..33058, 10 regs): PV Power
//      33057..33058  uint32   power_pv                     Total PV DC power (W -> kW)
//
//    Block 3 (33070..33080, 11 regs): Inverter AC Power
//      33079..33080  int32    power                        Inverter active power (W -> kW)
//
//    Block 4 (33126..33131, 6 regs): Grid Meter Power
//      33130..33131  int32    power_grid                   Grid power (+ import, - export) (W -> kW)
//
//    Block 5 (33132..33150, 19 regs): Storage / Battery Status
//      33135         uint16   (direction)                  0 = charging, 1 = discharging
//      33139         uint16   battery_soc                  Battery state of charge (%)
//      33149..33150  uint32   power_battery                Battery power (+ discharge, - charge) (W -> kW)
//
//    Block 6 (33161..33180, 20 regs): Energy Totals
//      33161..33162  uint32   energy_battery_import        Total battery charge energy (kWh)
//      33163         uint16   energy_today_battery_import  Today battery charge energy (0.1 kWh)
//      33165..33166  uint32   energy_battery_export        Total battery discharge energy (kWh)
//      33167         uint16   energy_today_battery_export  Today battery discharge energy (0.1 kWh)
//      33169..33170  uint32   energy_import                Total grid import energy (kWh)
//      33171         uint16   energy_today_import          Today grid import energy (0.1 kWh)
//      33173..33174  uint32   energy_export                Total grid export energy (kWh)
//      33175         uint16   energy_today_export          Today grid export energy (0.1 kWh)
//      33177..33178  uint32   energy_consumption           Total consumption energy (kWh)
//
//  Holding Registers (Function Code 0x03 - Read / 0x06 - Write Single Register):
//
//    Block 7 (43128..43137, 10 regs): Remote Power Control & Battery Limits
//      43128         int16    grid_active_power_limit      RC Inverter AC Grid Active Power (10W)
//      43129         uint16   force_discharge_power        RC Force Battery Discharge Power (10W)
//      43130         uint16   charge_power_limit           Battery Charge Limit Power (10W)
//      43131         uint16   discharge_power_limit        Battery Discharge Limit Power (10W)
//      43133         int16    rc_grid_active_power         RC Grid Active Power (10W)
//      43135         uint16   force_charge_mode            0 = Off, 1 = Force Charge, 2 = Force Discharge
//      43136         uint16   force_charge_power           RC Force Battery Charge Power (10W)
//
//    Block 8 (43074, 1 reg):
//      43074         uint16   grid_export_limit            Backflow / Feed-in Limitation (100W)
//
//    Block 9 (43195, 1 reg):
//      43195         int16    power_setpoint               Power Setpoint (-1000..+1000 W)
//
//  Safety Watchdog / Heartbeat:
//    Solis hybrid inverters implement a deadman safety timer: if register 43135 or remote control
//    commands are not refreshed within ~1-5 minutes, the inverter reverts to automatic self-consumption.
//    This driver automatically refreshes active control modes every 30 seconds during polling.
//

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NModbus;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using TelemetryValues = Dictionary<string, object>;

    public class SolisDriver : BaseModbusDriver, IDeviceWriter
    {
        public override string DriverName => "Solis";

        // Holding Register Addresses
        public const ushort REG_BACKFLOW_POWER = 43074;             // 100W scale
        public const ushort REG_POWER_SETPOINT = 43195;             // 1W scale (S16, range -1000..+1000W)
        public const ushort REG_EXPORT_CALIBRATION = REG_POWER_SETPOINT; // Legacy alias
        public const ushort REG_RC_INVERTER_ACTIVE_POWER = 43128;   // 10W scale (S16)
        public const ushort REG_RC_FORCE_DISCHARGE_POWER = 43129;   // 10W scale (U16)
        public const ushort REG_BATT_CHARGE_LIMIT_POWER = 43130;    // 10W scale (U16)
        public const ushort REG_BATT_DISCHARGE_LIMIT_POWER = 43131; // 10W scale (U16)
        public const ushort REG_RC_GRID_ACTIVE_POWER = 43133;       // 10W scale (S16)
        public const ushort REG_RC_FORCE_CHARGE_MODE = 43135;       // 0=Off, 1=Force Charge, 2=Force Discharge
        public const ushort REG_RC_FORCE_CHARGE_POWER = 43136;      // 10W scale (U16)

        /// <summary>Heartbeat keepalive interval in seconds (default: 30s).</summary>
        public double HeartbeatIntervalSeconds { get; set; } = 30.0;

        public class SolisControlState
        {
            public ushort ForceMode { get; set; } = 0;
            public ushort ForceChargePower { get; set; } = 0;
            public ushort ForceDischargePower { get; set; } = 0;
            public double ForcePowerKw { get; set; } = 0.0;
            public DateTime WrittenAtUtc { get; set; } = DateTime.MinValue;
            public double ValidityDurationSeconds { get; set; } = 60.0;
            public DateTime LastHeartbeatUtc { get; set; } = DateTime.MinValue;

            public bool IsExpired(DateTime now) =>
                ForceMode != 0 && WrittenAtUtc > DateTime.MinValue && (now - WrittenAtUtc).TotalSeconds >= ValidityDurationSeconds;
        }

        private static readonly ConcurrentDictionary<string, SolisControlState> _controlStates = new();

        public static SolisControlState GetControlState(string deviceKey) =>
            _controlStates.GetOrAdd(deviceKey, _ => new SolisControlState());

        public static void ClearControlStates() => _controlStates.Clear();

        public override IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.BatterySocPct,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh,

            // Writable Power Control & Holding Parameters
            TelemetryKeys.ForcePowerKw,
            TelemetryKeys.PowerLimitPct,
            TelemetryKeys.PowerSetpointW
        };

        public override IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.BatterySocPct] = Units.Percent,
            [TelemetryKeys.EnergyImportKwh] = Units.KilowattHour,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour,

            [TelemetryKeys.ForcePowerKw] = Units.Kilowatt,
            [TelemetryKeys.PowerLimitPct] = Units.Percent,
            [TelemetryKeys.PowerSetpointW] = Units.Watt
        };

        public class SolisRawData
        {
            public double? EnergyPvKwh { get; set; }
            public double? PowerPvKw { get; set; }
            public double? PowerKw { get; set; }
            public double? PowerGridKw { get; set; }
            public double? BatterySocPct { get; set; }
            public double? PowerBatteryKw { get; set; }
            public double? EnergyBatteryImportKwh { get; set; }
            public double? EnergyBatteryExportKwh { get; set; }
            public double? EnergyImportKwh { get; set; }
            public double? EnergyExportKwh { get; set; }
            public double ForcePowerKw { get; set; }
            public double? PowerSetpointW { get; set; }
        }

        private static readonly ConcurrentDictionary<string, (DateTime CachedAt, SolisRawData Data)> _rawCache = new();

        public static void ClearRawCache() => _rawCache.Clear();

        public static SolisRawData GetOrReadRawData(IModbusMaster master, ConnectionConfig conn, byte slaveId, string deviceKey, bool forceRefresh = false)
        {
            string cacheKey = $"{conn.Address ?? conn.Id}:{conn.Port ?? 502}:{slaveId}";
            return GetOrReadRawData(master, slaveId, deviceKey, cacheKey, forceRefresh);
        }

        public static SolisRawData GetOrReadRawData(IModbusMaster master, byte slaveId, string deviceKey, string cacheKey = "", bool forceRefresh = false)
        {
            if (!string.IsNullOrEmpty(cacheKey) && !forceRefresh && _rawCache.TryGetValue(cacheKey, out var entry))
            {
                if ((DateTime.UtcNow - entry.CachedAt).TotalSeconds < 2.5)
                {
                    return entry.Data;
                }
            }

            var raw = ReadRawFromMaster(master, slaveId, deviceKey);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                _rawCache[cacheKey] = (DateTime.UtcNow, raw);
            }
            return raw;
        }

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;
            string cacheKey = $"{conn.Address ?? conn.Id}:{conn.Port ?? 502}:{slaveId}";

            return ModbusConnection.WithMaster(conn, master =>
            {
                var raw = GetOrReadRawData(master, slaveId, deviceKey, cacheKey);
                return MapRawToTelemetry(raw);
            });
        }

        public TelemetryValues ReadFromMaster(IModbusMaster master, byte slaveId, string deviceKey = "")
        {
            var raw = GetOrReadRawData(master, slaveId, deviceKey, deviceKey);
            return MapRawToTelemetry(raw);
        }

        public static TelemetryValues MapRawToTelemetry(SolisRawData raw)
        {
            var result = new TelemetryValues();
            if (raw.PowerKw.HasValue) result[TelemetryKeys.PowerKw] = raw.PowerKw.Value;
            if (raw.BatterySocPct.HasValue) result[TelemetryKeys.BatterySocPct] = raw.BatterySocPct.Value;
            if (raw.EnergyImportKwh.HasValue) result[TelemetryKeys.EnergyImportKwh] = raw.EnergyImportKwh.Value;
            if (raw.EnergyExportKwh.HasValue) result[TelemetryKeys.EnergyExportKwh] = raw.EnergyExportKwh.Value;
            if (raw.PowerSetpointW.HasValue) result[TelemetryKeys.PowerSetpointW] = raw.PowerSetpointW.Value;
            result[TelemetryKeys.ForcePowerKw] = raw.ForcePowerKw;
            return result;
        }

        public static SolisRawData ReadRawFromMaster(IModbusMaster master, byte slaveId, string deviceKey = "")
        {
            var raw = new SolisRawData();

            // Block 1: PV Energy Generation (33029..33038)
            try
            {
                var b1 = master.ReadInputRegisters(slaveId, 33029, 10);
                uint pvTotalKwh = ToUInt32(b1[0], b1[1]);
                raw.EnergyPvKwh = (double)pvTotalKwh;
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 1 (PV Energy) read failed: {ex.Message}");
            }

            // Block 2: PV Power (33049..33058)
            try
            {
                var b2 = master.ReadInputRegisters(slaveId, 33049, 10);
                uint pvPowerW = ToUInt32(b2[8], b2[9]);
                raw.PowerPvKw = Math.Round(pvPowerW / 1000.0, 3);
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 2 (PV Power) read failed: {ex.Message}");
            }

            // Block 3: Inverter AC Power (33070..33080)
            try
            {
                var b3 = master.ReadInputRegisters(slaveId, 33070, 11);
                int invPowerW = ToInt32(b3[9], b3[10]);
                raw.PowerKw = Math.Round(invPowerW / 1000.0, 3);
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 3 (Inverter Power) read failed: {ex.Message}");
            }

            // Block 4: Grid Meter Power (33126..33131)
            try
            {
                var b4 = master.ReadInputRegisters(slaveId, 33126, 6);
                int gridPowerW = ToInt32(b4[4], b4[5]);
                raw.PowerGridKw = Math.Round(gridPowerW / 1000.0, 3);
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 4 (Grid Meter Power) read failed: {ex.Message}");
            }

            // Block 5: Storage & Battery Status (33132..33150)
            try
            {
                var b5 = master.ReadInputRegisters(slaveId, 33132, 19);
                ushort direction = b5[3];       // 33135: 0 = charging, 1 = discharging
                ushort soc = b5[7];             // 33139: SOC in %
                uint battPowerMagW = ToUInt32(b5[17], b5[18]); // 33149, 33150

                // Discharging = positive (+), Charging = negative (-)
                double signedBattPowerW = direction == 0
                    ? -1.0 * battPowerMagW
                    : (double)battPowerMagW;

                raw.BatterySocPct = (double)soc;
                raw.PowerBatteryKw = Math.Round(signedBattPowerW / 1000.0, 3);
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 5 (Battery Status) read failed: {ex.Message}");
            }

            // Block 6: Energy Totals (33161..33180)
            try
            {
                var b6 = master.ReadInputRegisters(slaveId, 33161, 20);
                uint battChargeTotalKwh = ToUInt32(b6[0], b6[1]);       // 33161, 33162
                uint battDischargeTotalKwh = ToUInt32(b6[4], b6[5]);    // 33165, 33166
                uint gridImportTotalKwh = ToUInt32(b6[8], b6[9]);       // 33169, 33170
                uint gridExportTotalKwh = ToUInt32(b6[12], b6[13]);     // 33173, 33174

                raw.EnergyBatteryImportKwh = (double)battChargeTotalKwh;
                raw.EnergyBatteryExportKwh = (double)battDischargeTotalKwh;
                raw.EnergyImportKwh = (double)gridImportTotalKwh;
                raw.EnergyExportKwh = (double)gridExportTotalKwh;
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 6 (Energy Totals) read failed: {ex.Message}");
            }

            // Block 7: Holding Registers / Power Control Setpoints (43128..43137)
            try
            {
                var b7 = master.ReadHoldingRegisters(slaveId, 43128, 10);
                short rcAcPower = unchecked((short)b7[0]); // 43128: 10W
                ushort forceDischPower = b7[1];            // 43129: 10W
                ushort chargeLimit = b7[2];                // 43130: 10W
                ushort dischLimit = b7[3];                 // 43131: 10W
                ushort forceMode = b7[7];                  // 43135: 0=Off, 1=Charge, 2=Discharge
                ushort forceChargePower = b7[8];           // 43136: 10W

                // Wrapped single control setpoint: positive (+) = discharge, negative (-) = charge
                double forcePower = 0.0;
                if (forceMode == 2)
                {
                    forcePower = Math.Round(forceDischPower * 10.0 / 1000.0, 3);
                }
                else if (forceMode == 1)
                {
                    forcePower = -Math.Round(forceChargePower * 10.0 / 1000.0, 3);
                }

                // Check 60s validity expiration
                if (!string.IsNullOrEmpty(deviceKey) && _controlStates.TryGetValue(deviceKey, out var ctrlState))
                {
                    lock (ctrlState)
                    {
                        if (ctrlState.IsExpired(DateTime.UtcNow))
                        {
                            try
                            {
                                master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, 0);
                                ctrlState.ForceMode = 0;
                                ctrlState.ForceChargePower = 0;
                                ctrlState.ForceDischargePower = 0;
                                ctrlState.ForcePowerKw = 0.0;
                                ctrlState.WrittenAtUtc = DateTime.MinValue;
                                Log.Info($"[Solis] Force power setpoint expired after 60s on '{deviceKey}'. Reverted to normal self-consumption.");
                            }
                            catch (Exception ex)
                            {
                                Log.Warning($"[Solis] Failed to clear expired force mode on '{deviceKey}': {ex.Message}");
                            }
                            forcePower = 0.0;
                        }
                    }
                }

                raw.ForcePowerKw = forcePower;
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 7 (Holding Control Registers 43128..43137) read failed: {ex.Message}");
            }

            // Block 8: Power Setpoint (43195)
            try
            {
                var b8 = master.ReadHoldingRegisters(slaveId, REG_POWER_SETPOINT, 1);
                raw.PowerSetpointW = (double)unchecked((short)b8[0]);
            }
            catch (Exception ex)
            {
                Log.Debug($"[Solis] Block 8 (Power Setpoint 43195) read failed: {ex.Message}");
            }


            // Heartbeat keepalive for active remote control modes
            if (!string.IsNullOrEmpty(deviceKey))
            {
                PerformHeartbeatIfDueStatic(master, slaveId, deviceKey);
            }

            return raw;
        }

        // =====================================================================
        //  IDeviceWriter Implementation
        // =====================================================================

        public bool IsWritable(string key) => key switch
        {
            TelemetryKeys.ForcePowerKw => true,
            TelemetryKeys.PowerLimitPct => true,
            TelemetryKeys.PowerSetpointW => true,
            "force_charge_mode" => true,
            "force_charge_power" => true,
            "force_discharge_power" => true,
            _ => false
        };

        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (!IsWritable(key))
            {
                throw new InvalidOperationException($"Data point '{key}' is not writable on {DriverName} driver.");
            }

            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;

            ModbusConnection.WithMaster<int>(connection, master =>
            {
                WriteToMaster(master, slaveId, deviceKey, key, value);
                return 0;
            });
        }

        public void WriteToMaster(IModbusMaster master, byte slaveId, string deviceKey, string key, double value) =>
            WriteToMasterStatic(master, slaveId, deviceKey, key, value);

        public static void WriteToMasterStatic(IModbusMaster master, byte slaveId, string deviceKey, string key, double value)
        {
            var state = GetControlState(deviceKey);

            switch (key)
            {
                case TelemetryKeys.ForcePowerKw:
                {
                    var now = DateTime.UtcNow;
                    lock (state)
                    {
                        if (value > 0)
                        {
                            // Positive (+) = Force Discharge
                            ushort regVal = (ushort)Math.Clamp(Math.Round(value * 100.0), 0, 65535);
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_DISCHARGE_POWER, regVal);
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, 2);

                            state.ForceMode = 2;
                            state.ForceDischargePower = regVal;
                            state.ForceChargePower = 0;
                            state.ForcePowerKw = value;
                            state.WrittenAtUtc = now;
                            state.LastHeartbeatUtc = now;
                            state.ValidityDurationSeconds = 60.0;
                            Log.Info($"[Solis] Set Force Discharge on '{deviceKey}' to {value} kW (Reg {REG_RC_FORCE_DISCHARGE_POWER} = {regVal}, valid 60s)");
                        }
                        else if (value < 0)
                        {
                            // Negative (-) = Force Charge
                            double absVal = Math.Abs(value);
                            ushort regVal = (ushort)Math.Clamp(Math.Round(absVal * 100.0), 0, 65535);
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_POWER, regVal);
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, 1);

                            state.ForceMode = 1;
                            state.ForceChargePower = regVal;
                            state.ForceDischargePower = 0;
                            state.ForcePowerKw = value;
                            state.WrittenAtUtc = now;
                            state.LastHeartbeatUtc = now;
                            state.ValidityDurationSeconds = 60.0;
                            Log.Info($"[Solis] Set Force Charge on '{deviceKey}' to {absVal} kW (Reg {REG_RC_FORCE_CHARGE_POWER} = {regVal}, valid 60s)");
                        }
                        else
                        {
                            // 0 = Normal / Off
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, 0);

                            state.ForceMode = 0;
                            state.ForceChargePower = 0;
                            state.ForceDischargePower = 0;
                            state.ForcePowerKw = 0.0;
                            state.WrittenAtUtc = DateTime.MinValue;
                            state.LastHeartbeatUtc = now;
                            Log.Info($"[Solis] Set Force Charge/Discharge Mode on '{deviceKey}' to 0 (Normal)");
                        }
                    }
                    break;
                }

                case "force_charge_mode":
                {
                    ushort mode = (ushort)Math.Clamp((int)Math.Round(value), 0, 2);
                    master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, mode);
                    lock (state)
                    {
                        state.ForceMode = mode;
                        state.WrittenAtUtc = mode != 0 ? DateTime.UtcNow : DateTime.MinValue;
                        state.LastHeartbeatUtc = DateTime.UtcNow;
                    }
                    Log.Info($"[Solis] Set Force Charge/Discharge Mode on '{deviceKey}' to {mode} (0=Off, 1=Charge, 2=Discharge)");
                    break;
                }

                case "force_charge_power":
                {
                    // 1 kW = 1000 W = 100 * 10W units
                    ushort regVal = (ushort)Math.Clamp(Math.Round(value * 100.0), 0, 65535);
                    master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_POWER, regVal);
                    lock (state)
                    {
                        state.ForceChargePower = regVal;
                        state.LastHeartbeatUtc = DateTime.UtcNow;
                    }
                    Log.Info($"[Solis] Set Force Charge Power on '{deviceKey}' to {value} kW (Reg {REG_RC_FORCE_CHARGE_POWER} = {regVal})");
                    break;
                }

                case "force_discharge_power":
                {
                    // 1 kW = 1000 W = 100 * 10W units
                    ushort regVal = (ushort)Math.Clamp(Math.Round(value * 100.0), 0, 65535);
                    master.WriteSingleRegister(slaveId, REG_RC_FORCE_DISCHARGE_POWER, regVal);
                    lock (state)
                    {
                        state.ForceDischargePower = regVal;
                        state.LastHeartbeatUtc = DateTime.UtcNow;
                    }
                    Log.Info($"[Solis] Set Force Discharge Power on '{deviceKey}' to {value} kW (Reg {REG_RC_FORCE_DISCHARGE_POWER} = {regVal})");
                    break;
                }

                case TelemetryKeys.PowerLimitPct:
                {
                    // S16, 10W units
                    short regVal = (short)Math.Clamp(Math.Round(value * 100.0), -32768, 32767);
                    master.WriteSingleRegister(slaveId, REG_RC_INVERTER_ACTIVE_POWER, unchecked((ushort)regVal));
                    Log.Info($"[Solis] Set AC Active Power limit on '{deviceKey}' to {value} kW (Reg {REG_RC_INVERTER_ACTIVE_POWER} = {regVal})");
                    break;
                }

                case TelemetryKeys.PowerSetpointW:
                {
                    // S16, 1W units, range -1000 to +1000 W
                    short regVal = (short)Math.Clamp(Math.Round(value), -1000, 1000);
                    master.WriteSingleRegister(slaveId, REG_POWER_SETPOINT, unchecked((ushort)regVal));
                    Log.Info($"[Solis] Set Power Setpoint on '{deviceKey}' to {regVal} W (Reg {REG_POWER_SETPOINT} = {unchecked((ushort)regVal)})");
                    break;
                }

                default:
                    throw new NotSupportedException($"Writing to '{key}' is not implemented for Solis inverters.");
            }
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported for Solis Modbus devices.");
        }

        /// <summary>
        /// Periodic heartbeat keepalive: re-asserts active force charge/discharge modes to prevent
        /// the inverter's safety deadman timer from resetting the mode back to 0.
        /// </summary>
        public void PerformHeartbeatIfDue(IModbusMaster master, byte slaveId, string deviceKey) =>
            PerformHeartbeatIfDueStatic(master, slaveId, deviceKey, HeartbeatIntervalSeconds);

        public static void PerformHeartbeatIfDueStatic(IModbusMaster master, byte slaveId, string deviceKey, double heartbeatIntervalSeconds = 30.0)
        {
            if (!_controlStates.TryGetValue(deviceKey, out var state)) return;

            lock (state)
            {
                if (state.ForceMode == 0) return;

                var now = DateTime.UtcNow;

                // Check 60-second expiration
                if (state.IsExpired(now))
                {
                    try
                    {
                        master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, 0);
                        state.ForceMode = 0;
                        state.ForceChargePower = 0;
                        state.ForceDischargePower = 0;
                        state.ForcePowerKw = 0.0;
                        state.WrittenAtUtc = DateTime.MinValue;
                        Log.Info($"[Solis] Heartbeat: force power setpoint expired after 60s for '{deviceKey}'. Reverted to normal mode.");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Solis] Failed to revert expired force mode on heartbeat for '{deviceKey}': {ex.Message}");
                    }
                    return;
                }

                if ((now - state.LastHeartbeatUtc).TotalSeconds >= heartbeatIntervalSeconds)
                {
                    try
                    {
                        // 1. Re-assert force charge/discharge mode
                        master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_MODE, state.ForceMode);

                        // 2. Re-assert power register if set
                        if (state.ForceMode == 1 && state.ForceChargePower > 0)
                        {
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_CHARGE_POWER, state.ForceChargePower);
                        }
                        else if (state.ForceMode == 2 && state.ForceDischargePower > 0)
                        {
                            master.WriteSingleRegister(slaveId, REG_RC_FORCE_DISCHARGE_POWER, state.ForceDischargePower);
                        }

                        state.LastHeartbeatUtc = now;
                        Log.Debug($"[Solis] Heartbeat keepalive refreshed for '{deviceKey}' (Mode={state.ForceMode}, Setpoint={state.ForcePowerKw} kW)");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[Solis] Heartbeat keepalive failed for '{deviceKey}': {ex.Message}");
                    }
                }
            }
        }

        public static uint ToUInt32(ushort high, ushort low) => ((uint)high << 16) | low;
        public static int ToInt32(ushort high, ushort low) => unchecked((int)(((uint)high << 16) | low));
    }

    /// <summary>
    /// Solis S6 PV Inverter component.
    /// Exposes PV solar generation metrics (power, energy export total).
    /// </summary>
    public class SolisPvDriver : BaseModbusDriver
    {
        public override string DriverName => "Solis-PV";

        public override IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyExportKwh
        };

        public override IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour
        };

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;
            string cacheKey = $"{conn.Address ?? conn.Id}:{conn.Port ?? 502}:{slaveId}";

            return ModbusConnection.WithMaster(conn, master =>
            {
                var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, cacheKey);
                return MapRawToTelemetry(raw);
            });
        }

        public TelemetryValues ReadFromMaster(IModbusMaster master, byte slaveId, string deviceKey = "")
        {
            var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, deviceKey);
            return MapRawToTelemetry(raw);
        }

        public static TelemetryValues MapRawToTelemetry(SolisDriver.SolisRawData raw)
        {
            var result = new TelemetryValues();
            if (raw.PowerPvKw.HasValue)
            {
                result[TelemetryKeys.PowerKw] = raw.PowerPvKw.Value;
            }
            if (raw.EnergyPvKwh.HasValue)
            {
                result[TelemetryKeys.EnergyExportKwh] = raw.EnergyPvKwh.Value;
            }
            return result;
        }
    }

    /// <summary>
    /// Solis S6 Battery Storage component.
    /// Exposes battery telemetry (power [+=discharge, -=charge], SoC, energy import/export totals)
    /// and supports battery power control (force_power, charge/discharge limits).
    /// </summary>
    public class SolisBatteryDriver : BaseModbusDriver, IDeviceWriter
    {
        public override string DriverName => "Solis-Battery";

        public override IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.BatterySocPct,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh,
            TelemetryKeys.ForcePowerKw
        };

        public override IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.BatterySocPct] = Units.Percent,
            [TelemetryKeys.EnergyImportKwh] = Units.KilowattHour,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour,
            [TelemetryKeys.ForcePowerKw] = Units.Kilowatt
        };

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;
            string cacheKey = $"{conn.Address ?? conn.Id}:{conn.Port ?? 502}:{slaveId}";

            return ModbusConnection.WithMaster(conn, master =>
            {
                var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, cacheKey);
                return MapRawToTelemetry(raw);
            });
        }

        public TelemetryValues ReadFromMaster(IModbusMaster master, byte slaveId, string deviceKey = "")
        {
            var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, deviceKey);
            return MapRawToTelemetry(raw);
        }

        public static TelemetryValues MapRawToTelemetry(SolisDriver.SolisRawData raw)
        {
            var result = new TelemetryValues();
            if (raw.PowerBatteryKw.HasValue)
            {
                result[TelemetryKeys.PowerKw] = raw.PowerBatteryKw.Value;
            }
            if (raw.BatterySocPct.HasValue)
            {
                result[TelemetryKeys.BatterySocPct] = raw.BatterySocPct.Value;
            }
            if (raw.EnergyBatteryImportKwh.HasValue)
            {
                result[TelemetryKeys.EnergyImportKwh] = raw.EnergyBatteryImportKwh.Value;
            }
            if (raw.EnergyBatteryExportKwh.HasValue)
            {
                result[TelemetryKeys.EnergyExportKwh] = raw.EnergyBatteryExportKwh.Value;
            }
            result[TelemetryKeys.ForcePowerKw] = raw.ForcePowerKw;
            return result;
        }

        public bool IsWritable(string key) => key switch
        {
            TelemetryKeys.ForcePowerKw => true,
            "force_charge_mode" => true,
            "force_charge_power" => true,
            "force_discharge_power" => true,
            _ => false
        };

        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (!IsWritable(key))
            {
                throw new InvalidOperationException($"Data point '{key}' is not writable on {DriverName} driver.");
            }

            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;

            ModbusConnection.WithMaster<int>(connection, master =>
            {
                SolisDriver.WriteToMasterStatic(master, slaveId, deviceKey, key, value);
                return 0;
            });
        }

        public void WriteToMaster(IModbusMaster master, byte slaveId, string deviceKey, string key, double value) =>
            SolisDriver.WriteToMasterStatic(master, slaveId, deviceKey, key, value);

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported for Solis Modbus devices.");
        }
    }

    /// <summary>
    /// Solis S6 Grid Meter component.
    /// Exposes grid telemetry (power [+=import, -=export], meter energy totals)
    /// and supports grid active / export limits.
    /// </summary>
    public class SolisGridDriver : BaseModbusDriver, IDeviceWriter
    {
        public override string DriverName => "Solis-Grid";

        public override IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh,
            TelemetryKeys.PowerLimitPct,
            TelemetryKeys.PowerSetpointW
        };

        public override IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.EnergyImportKwh] = Units.KilowattHour,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour,
            [TelemetryKeys.PowerLimitPct] = Units.Percent,
            [TelemetryKeys.PowerSetpointW] = Units.Watt
        };

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;
            string cacheKey = $"{conn.Address ?? conn.Id}:{conn.Port ?? 502}:{slaveId}";

            return ModbusConnection.WithMaster(conn, master =>
            {
                var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, cacheKey);
                return MapRawToTelemetry(raw);
            });
        }

        public TelemetryValues ReadFromMaster(IModbusMaster master, byte slaveId, string deviceKey = "")
        {
            var raw = SolisDriver.GetOrReadRawData(master, slaveId, deviceKey, deviceKey);
            return MapRawToTelemetry(raw);
        }

        public static TelemetryValues MapRawToTelemetry(SolisDriver.SolisRawData raw)
        {
            var result = new TelemetryValues();
            if (raw.PowerGridKw.HasValue)
            {
                result[TelemetryKeys.PowerKw] = raw.PowerGridKw.Value;
            }
            if (raw.EnergyImportKwh.HasValue)
            {
                result[TelemetryKeys.EnergyImportKwh] = raw.EnergyImportKwh.Value;
            }
            if (raw.EnergyExportKwh.HasValue)
            {
                result[TelemetryKeys.EnergyExportKwh] = raw.EnergyExportKwh.Value;
            }
            if (raw.PowerSetpointW.HasValue)
            {
                result[TelemetryKeys.PowerSetpointW] = raw.PowerSetpointW.Value;
            }
            return result;
        }

        public bool IsWritable(string key) => key switch
        {
            TelemetryKeys.PowerLimitPct => true,
            TelemetryKeys.PowerSetpointW => true,
            _ => false
        };

        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (!IsWritable(key))
            {
                throw new InvalidOperationException($"Data point '{key}' is not writable on {DriverName} driver.");
            }

            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));
            string deviceKey = device.Id ?? device.Name;

            ModbusConnection.WithMaster<int>(connection, master =>
            {
                SolisDriver.WriteToMasterStatic(master, slaveId, deviceKey, key, value);
                return 0;
            });
        }

        public void WriteToMaster(IModbusMaster master, byte slaveId, string deviceKey, string key, double value) =>
            SolisDriver.WriteToMasterStatic(master, slaveId, deviceKey, key, value);

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported for Solis Modbus devices.");
        }
    }
}
