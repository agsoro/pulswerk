// SmaDatamanagerDriver.cs – SMA Data Manager M / Cluster Controller driver
//
//  Register map (Holding)
//
//    Address  Type    Telemetry key       Unit   Scaling  Notes
//    ───────  ──────  ──────────────────  ─────  ───────  ──────────────────
//    31243    uint32  utility_limit       %      0.01     Active power limitation
//    30775    int32   power               W      1.0      Current active power (signed, negative = feed-in)
//    30513    uint64  energy_export       Wh     1.0      Total energy yield
//
//  SMA NaN Sentinels:
//    uint32 → 0xFFFF_FFFF / 0x8000_0000    int32 → 0x8000_0000    uint64 → 0xFFFF_FFFF_FFFF_FFFF
//

using System;
using System.Collections.Generic;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using Telemetry = Dictionary<string, object>;

    class SmaDatamanagerDriver : BaseModbusDriver
    {
        const ushort REG_LIMIT_PCT = 31243;
        const ushort REG_POWER_W = 30775;
        const ushort REG_ENERGY_WH = 30513;

        // SMA NaN sentinel values
        const uint SMA_NAN_U32 = 0xFFFFFFFF;
        const uint SMA_NAN_U32_ALT = 0x80000000;
        const int SMA_NAN_S32 = unchecked((int)0x80000000);
        const ulong SMA_NAN_U64 = 0xFFFFFFFFFFFFFFFF;

        public override string DriverName => "SMADM";

        public override IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.PowerLimitPct,
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyExportKwh
        };

        public override Telemetry Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            return ModbusConnection.WithMaster(conn, master =>
            {
                // SMA uses Big-Endian (ABCD) by default
                uint limitRaw = ModbusConnection.ReadUInt32(master, slaveId, REG_LIMIT_PCT);
                int powerRaw = ModbusConnection.ReadInt32(master, slaveId, REG_POWER_W);
                ulong energyRaw = ModbusConnection.ReadUInt64(master, slaveId, REG_ENERGY_WH);

                // Filter SMA NaN sentinel values → return 0 when data is unavailable
                double limitPct = (limitRaw == SMA_NAN_U32 || limitRaw == SMA_NAN_U32_ALT) ? 0 : Math.Round(limitRaw * 0.01, 2);
                double powerKw = (powerRaw == SMA_NAN_S32) ? 0 : Math.Round(powerRaw / 1000.0, 3);
                double energyKwh = (energyRaw == SMA_NAN_U64) ? 0 : Math.Round(energyRaw / 1000.0, 3);

                return new Telemetry
                {
                    [TelemetryKeys.PowerLimitPct] = limitPct,
                    [TelemetryKeys.PowerKw] = powerKw,
                    [TelemetryKeys.EnergyExportKwh] = energyKwh
                };
            });
        }
    }
}
