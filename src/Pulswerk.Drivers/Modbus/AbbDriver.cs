// AbbDriver.cs – ABB energy meter driver (Modbus TCP)
//
//  Register map (based on provided device documentation)
//
//    Address  Data point key        Type    Objects  Divider
//    ───────  ───────────────────  ──────  ───────  ───────
//    23316    power_kw             32int   2        100000
//    20480    energy_import_kWh    64uint  4        100
//    20484    energy_export_kWh    64uint  4        100
//

using System;
using System.Collections.Generic;

using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using DataPointValues = Dictionary<string, object>;

    class AbbDriver : BaseModbusDriver
    {
        const ushort REG_POWER_KW = 23316;
        const ushort REG_ENERGY_IMPORT_KWH = 20480;
        const ushort REG_ENERGY_EXPORT_KWH = 20484;

        public override string DriverName => "ABB";
        // public bool IsBusy => false;

        public override IEnumerable<string> GetDataPointKeys() => new[] {
            DataPointKeys.PowerKw,
            DataPointKeys.EnergyImportKwh,
            DataPointKeys.EnergyExportKwh
        };

        public override DataPointValues Read(ConnectionConfig conn, DeviceConfig device)
        {
            byte slaveId = (byte)(device.DeviceId
                ?? throw new InvalidOperationException($"Device '{device.Name}' is missing deviceId."));

            return ModbusConnection.WithMaster(conn, master =>
            {
                // Read 32int (2 registers)
                var powerRegs = master.ReadHoldingRegisters(slaveId, REG_POWER_KW, 2);

                // Read 64uint (8 registers total: 4 for import, 4 for export)
                // These are contiguous: 20480-20483 and 20484-20487
                var energyRegs = master.ReadHoldingRegisters(slaveId, REG_ENERGY_IMPORT_KWH, 8);

                return new DataPointValues
                {
                    [DataPointKeys.PowerKw] = Math.Round(ModbusConnection.RegsToInt32(powerRegs, 0) / 100000.0, 3),
                    [DataPointKeys.EnergyImportKwh] = Math.Round((double)ModbusConnection.RegsToUInt64(energyRegs, 0) / 100.0, 3),
                    [DataPointKeys.EnergyExportKwh] = Math.Round((double)ModbusConnection.RegsToUInt64(energyRegs, 4) / 100.0, 3),
                };
            });
        }
    }
}
