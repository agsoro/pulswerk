// JanitzaDriver.cs – Janitza energy meter driver (Modbus TCP)
//
//  Register map  (UMG 604/605-PRO, 0-based, float32 = 2 × 16-bit registers)
//
//    Address  Telemetry key    Unit   Conversion
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
    using Telemetry = Dictionary<string, object>;

    class JanitzaDriver : IDeviceDriver
    {
        const ushort REG_TOTAL_ACTIVE_POWER_W = 19020;
        const ushort REG_IMPORT_ENERGY_WH = 19060;
        const ushort REG_EXPORT_ENERGY_WH = 19062;

        public string DriverName => "Janitza";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[] {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.EnergyExportKwh
        };

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() =>
            new Dictionary<string, string>
            {
                [TelemetryKeys.PowerKw] = "kW",
                [TelemetryKeys.EnergyImportKwh] = "kWh",
                [TelemetryKeys.EnergyExportKwh] = "kWh",
            };

        public Telemetry Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = device.SlaveId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing slaveId.");

            return ModbusConnection.WithMaster(conn, master =>
            {
                var pwRegs = master.ReadHoldingRegisters(slaveId, REG_TOTAL_ACTIVE_POWER_W, 2);
                var enRegs = master.ReadHoldingRegisters(slaveId, REG_IMPORT_ENERGY_WH, 4);

                return new Telemetry
                {
                    [TelemetryKeys.PowerKw] = Math.Round(ModbusConnection.RegsToFloat(pwRegs, 0) / 1000.0, 3),
                    [TelemetryKeys.EnergyImportKwh] = Math.Round(ModbusConnection.RegsToFloat(enRegs, 0) / 1000.0, 3),
                    [TelemetryKeys.EnergyExportKwh] = Math.Round(ModbusConnection.RegsToFloat(enRegs, 2) / 1000.0, 3),
                };
            });
        }
    }
}
