// Sdm630Driver.cs – Eastron SDM630 energy meter driver
//
//  Register map (Input Registers, 0-based, float32 = 2 × 16-bit registers)
//
//    Address  Telemetry key    Unit   Notes
//    ───────  ───────────────  ─────  ──────────────────
//    52       power_kw         W      Total Active Power (divide by 1000 for kW)
//    72       import_kwh       kWh    Total Import Active Energy
//    74       export_kwh       kWh    Total Export Active Energy
//

using System;
using System.Collections.Generic;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using Telemetry = Dictionary<string, object>;

    class Sdm630Driver : IDeviceDriver
    {
        const ushort REG_POWER_W = 52;
        const ushort REG_IMPORT_KWH = 72;
        const ushort REG_EXPORT_KWH = 74;

        public string DriverName => "SDM630";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh
        };

        public Telemetry Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            return ModbusConnection.WithMaster(conn, master =>
            {
                // SDM630 uses IEEE 754 float32 in Input registers
                float powerW = ModbusConnection.ReadFloat32(master, slaveId, REG_POWER_W, input: true);
                float importKwh = ModbusConnection.ReadFloat32(master, slaveId, REG_IMPORT_KWH, input: true);
                float exportKwh = ModbusConnection.ReadFloat32(master, slaveId, REG_EXPORT_KWH, input: true);

                return new Telemetry
                {
                    [TelemetryKeys.PowerKw] = Math.Round(powerW / 1000.0, 3),
                    [TelemetryKeys.EnergyImportKwh] = Math.Round((double)importKwh, 3),
                    [TelemetryKeys.EnergyExportKwh] = Math.Round((double)exportKwh, 3)
                };
            });
        }
    }
}
