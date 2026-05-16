// GlueckDriver.cs – Glück controller driver (Modbus TCP)
//
//  Register map (0-based)
//
//    Address  Type     Data point key       Notes
//    ───────  ───────  ──────────────────  ──────────────────
//    1901     Input    utility_limit_pct   1 reg (Netzbetreiber)
//    1902     Input    power_limit_pct     2 regs uint32s (Feedback)
//    1904     Input    power_kw            2 regs uint32s (Generation)
//
//    401      Holding  power_limit_pct     1 reg (Write Limit)
//    404      Holding  watchdog            2 regs (Watchdog increment)
//

using System;
using System.Collections.Generic;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using DataPointValues = Dictionary<string, object>;

    class GlueckDriver : BaseModbusDriver, IDeviceWriter
    {
        const ushort REG_UTILITY_LIMIT_IN = 1901;
        const ushort REG_FEEDBACK_LIMIT_IN = 1902;
        const ushort REG_GENERATION_POWER_IN = 1904;

        const ushort REG_LIMIT_POWER_OUT = 401;
        const ushort REG_WATCHDOG_OUT = 404;

        private uint _watchdogCounter = 0;
        private DateTime _lastWatchdogWrite = DateTime.MinValue;

        public override string DriverName => "Glueck";
        // public bool IsBusy => false;

        public override IEnumerable<string> GetDataPointKeys() => new[] {
            DataPointKeys.UtilityLimitPct,
            DataPointKeys.PowerLimitPct,
            DataPointKeys.PowerKw
        };



        // =====================================================================
        //  IDeviceDriver.Read
        // =====================================================================
        public override DataPointValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            return ModbusConnection.WithMaster(conn, master =>
            {
                // Read inputs
                ushort utilLimitRaw = ModbusConnection.ReadUInt16(master, slaveId, REG_UTILITY_LIMIT_IN, input: true);
                uint feedbackLimitRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_FEEDBACK_LIMIT_IN, swapped: true, input: true);
                int powerRaw = ModbusConnection.ReadInt32(master, slaveId, REG_GENERATION_POWER_IN, swapped: true, input: true);

                // Handle Watchdog (every minute)
                if ((DateTime.UtcNow - _lastWatchdogWrite).TotalMinutes >= 1.0)
                {
                    _watchdogCounter++;
                    try
                    {
                        ModbusConnection.WriteUInt32(master, slaveId, REG_WATCHDOG_OUT, _watchdogCounter, swapped: true);
                        _lastWatchdogWrite = DateTime.UtcNow;
                    }
                    catch (Exception ex)
                    {
                        Pulswerk.Core.Log.Error($"[Glueck] Watchdog write failed: {ex.Message}");
                    }
                }

                return new DataPointValues
                {
                    [DataPointKeys.UtilityLimitPct] = (double)utilLimitRaw,
                    [DataPointKeys.PowerLimitPct] = (double)feedbackLimitRaw,
                    [DataPointKeys.PowerKw] = (double)powerRaw
                };
            });
        }

        // =====================================================================
        //  IDeviceWriter.Write
        // =====================================================================
        public void Write(ConnectionConfig connection, DeviceConfig device,
                          string key, double value)
        {
            if (!IsWritable(key)) return;

            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            ModbusConnection.WithMaster<int>(connection, master =>
            {
                // Register 401 is 1 reg (percentage)
                ModbusConnection.WriteUInt16(master, slaveId, REG_LIMIT_POWER_OUT, (ushort)value);
                return 0;
            });
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported for Glueck Modbus devices.");
        }

        public bool IsWritable(string key) => key == DataPointKeys.PowerLimitPct;
    }
}
