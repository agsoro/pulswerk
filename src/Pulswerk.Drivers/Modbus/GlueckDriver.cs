// GlueckDriver.cs – Glück controller driver (Modbus TCP)
//
//  Register map (0-based)
//
//    Address  Type     Telemetry key       Notes
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
    using Telemetry = Dictionary<string, object>;

    class GlueckDriver : IDeviceDriver, IDeviceWriter
    {
        const ushort REG_UTILITY_LIMIT_IN = 1901;
        const ushort REG_FEEDBACK_LIMIT_IN = 1902;
        const ushort REG_GENERATION_POWER_IN = 1904;

        const ushort REG_LIMIT_POWER_OUT = 401;
        const ushort REG_WATCHDOG_OUT = 404;

        private uint _watchdogCounter = 0;
        private DateTime _lastWatchdogWrite = DateTime.MinValue;

        public string DriverName => "Glueck";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.UtilityLimitPct,
            TelemetryKeys.PowerLimitPct,
            TelemetryKeys.PowerKw
        };

        // =====================================================================
        //  IDeviceDriver.Read
        // =====================================================================
        public Telemetry Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = device.SlaveId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing slaveId.");

            return ModbusConnection.WithMaster(conn, master =>
            {
                // Read inputs
                ushort utilLimitRaw = ModbusConnection.ReadUInt16(master, slaveId, REG_UTILITY_LIMIT_IN, input: true);
                uint feedbackLimitRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_FEEDBACK_LIMIT_IN, swapped: true, input: true);
                uint powerRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_GENERATION_POWER_IN, swapped: true, input: true);

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
                        Console.Error.WriteLine($"[Glueck] Watchdog write failed: {ex.Message}");
                    }
                }

                return new Telemetry
                {
                    [TelemetryKeys.UtilityLimitPct] = (double)utilLimitRaw,
                    [TelemetryKeys.PowerLimitPct] = (double)feedbackLimitRaw,
                    [TelemetryKeys.PowerKw] = Math.Round(powerRaw / 1000.0, 3) // Assuming Watts to kW
                };
            });
        }

        // =====================================================================
        //  IDeviceWriter.Write
        // =====================================================================
        public void Write(ConnectionConfig connection, DeviceConfig device,
                          string key, double value)
        {
            if (key != TelemetryKeys.PowerLimitPct) return;

            byte slaveId = device.SlaveId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing slaveId.");

            ModbusConnection.WithMaster<int>(connection, master =>
            {
                // Register 401 is 1 reg (percentage)
                ModbusConnection.WriteUInt16(master, slaveId, REG_LIMIT_POWER_OUT, (ushort)value);
                return 0;
            });
        }
    }
}
