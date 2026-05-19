// JanitzaDriver.cs – Janitza energy meter driver (Modbus TCP)
//
//  Register map  (UMG 604/605-PRO, 0-based, float32 = 2 × 16-bit registers)
//
//    Address  Data point key    Unit   Conversion
//    ───────  ───────────────  ─────  ──────────
//    19020    power_kw         W      ÷ 1000
//    19060    import_kwh       Wh     ÷ 1000
//    19062    export_kwh       Wh     ÷ 1000
//
//  ⚠  Verify addresses for your exact model:
//     https://www.janitza.com/en/downloads/modbus-address-list

using System;
using System.Collections.Generic;

using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using TelemetryValues = Dictionary<string, object>;

    class JanitzaDriver : BaseModbusDriver
    {
        const ushort REG_POWER_SUM_W = 19026;
        const ushort REG_IMPORT_SUM_WH = 19062;
        const ushort REG_EXPORT_SUM_WH = 19076;

        public override string DriverName => "Janitza";

        public override IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh
        };

        public override TelemetryValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            return ModbusConnection.WithMaster(conn, master =>
            {
                // Read individually since registers are non-contiguous in this model
                float powerW = ModbusConnection.ReadFloat32(master, slaveId, REG_POWER_SUM_W);
                float importWh = ModbusConnection.ReadFloat32(master, slaveId, REG_IMPORT_SUM_WH);
                float exportWh = ModbusConnection.ReadFloat32(master, slaveId, REG_EXPORT_SUM_WH);

                return new TelemetryValues
                {
                    [TelemetryKeys.PowerKw] = Math.Round(powerW / 1000.0, 3),
                    [TelemetryKeys.EnergyImportKwh] = Math.Round(importWh / 1000.0, 3),
                    [TelemetryKeys.EnergyExportKwh] = Math.Round(exportWh / 1000.0, 3),
                };
            });
        }
    }
}
