// SmaDatamanagerDriver.cs – SMA Data Manager M / Cluster Controller driver
//
//  Register map (Holding)
//
//    Address  Type    Telemetry key       Unit   Scaling  Notes
//    ───────  ──────  ──────────────────  ─────  ───────  ──────────────────
//    31243    uint32  utility_limit       %      0.01     Active power limitation
//    30775    uint32  power               W      1.0      Current active power
//    30513    uint64  energy_export       Wh     1.0      Total energy yield
//

using System;
using System.Collections.Generic;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using Telemetry = Dictionary<string, object>;

    class SmaDatamanagerDriver : IDeviceDriver
    {
        const ushort REG_LIMIT_PCT = 31243;
        const ushort REG_POWER_W = 30775;
        const ushort REG_ENERGY_WH = 30513;

        public string DriverName => "SMA_DataManager";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.PowerLimitPct,
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyExportKwh
        };

        public Telemetry Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = device.SlaveId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing slaveId.");

            return ModbusConnection.WithMaster(conn, master =>
            {
                // SMA uses Big-Endian (ABCD) by default for 32-bit values
                uint limitRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_LIMIT_PCT);
                uint powerRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_POWER_W);
                ulong energyRaw = ModbusConnection.ReadUInt64(master, slaveId, REG_ENERGY_WH);

                return new Telemetry
                {
                    [TelemetryKeys.PowerLimitPct] = Math.Round(limitRaw * 0.01, 2),
                    [TelemetryKeys.PowerKw] = Math.Round(powerRaw / 1000.0, 3),
                    [TelemetryKeys.EnergyExportKwh] = Math.Round(energyRaw / 1000.0, 3)
                };
            });
        }
    }
}
