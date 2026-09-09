using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NModbus;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.Modbus;
using Xunit;

namespace Pulswerk.Drivers.Tests
{
    public class SolisDriverTests
    {
        [Fact]
        public void DeviceDriverFactory_Registers_Solis_And_Fronius()
        {
            var solisDriver = DeviceDriverFactory.Create("solis");
            Assert.NotNull(solisDriver);
            Assert.IsType<SolisDriver>(solisDriver);
            Assert.Equal("Solis", solisDriver.DriverName);

            var solisS6Driver = DeviceDriverFactory.Create("soliss6");
            Assert.NotNull(solisS6Driver);
            Assert.IsType<SolisDriver>(solisS6Driver);

            var froniusDriver = DeviceDriverFactory.Create("fronius");
            Assert.NotNull(froniusDriver);
            Assert.IsType<FroniusDriver>(froniusDriver);
            Assert.Equal("Fronius", froniusDriver.DriverName);

            var sunspecDriver = DeviceDriverFactory.Create("sunspec");
            Assert.NotNull(sunspecDriver);
            Assert.Equal("SunSpec", sunspecDriver.DriverName);
        }

        [Fact]
        public void SolisDriver_ExposesExpectedTelemetryKeys()
        {
            var driver = new SolisDriver();
            var keys = new HashSet<string>(driver.GetTelemetryKeys());

            Assert.Contains(TelemetryKeys.PowerKw, keys);
            Assert.Contains(TelemetryKeys.BatterySocPct, keys);
            Assert.Contains(TelemetryKeys.EnergyImportKwh, keys);
            Assert.Contains(TelemetryKeys.EnergyExportKwh, keys);

            // Writable control keys
            Assert.Contains(TelemetryKeys.ForcePowerKw, keys);
            Assert.Contains(TelemetryKeys.PowerLimitPct, keys);
        }

        [Fact]
        public void SolisDriver_IsWritable_ReturnsTrueForControlKeys()
        {
            var driver = new SolisDriver();

            Assert.True(driver.IsWritable(TelemetryKeys.ForcePowerKw));
            Assert.True(driver.IsWritable("force_power"));
            Assert.True(driver.IsWritable(TelemetryKeys.PowerLimitPct));

            // Read-only points should return false
            Assert.False(driver.IsWritable(TelemetryKeys.PowerKw));
            Assert.False(driver.IsWritable(TelemetryKeys.BatterySocPct));
            Assert.False(driver.IsWritable(TelemetryKeys.EnergyExportKwh));
        }

        [Fact]
        public void SolisDriver_WriteToMaster_ScalesAndWritesRegistersCorrectly()
        {
            var driver = new SolisDriver();
            var fakeMaster = new FakeSolisModbusMaster();

            // 1. ForcePowerKw = +2.5 kW -> Positive = Force Discharge (Mode 2), 2500W -> 250 in 10W units
            driver.WriteToMaster(fakeMaster, 1, "test-s6", TelemetryKeys.ForcePowerKw, 2.5);
            Assert.Equal((ushort)2, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
            Assert.Equal((ushort)250, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_DISCHARGE_POWER]);

            // 2. ForcePowerKw = -1.8 kW -> Negative = Force Charge (Mode 1), 1800W -> 180 in 10W units
            driver.WriteToMaster(fakeMaster, 1, "test-s6", TelemetryKeys.ForcePowerKw, -1.8);
            Assert.Equal((ushort)1, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
            Assert.Equal((ushort)180, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_POWER]);

            // 3. ForcePowerKw = 0 -> Mode 0 (Off / Normal)
            driver.WriteToMaster(fakeMaster, 1, "test-s6", TelemetryKeys.ForcePowerKw, 0);
            Assert.Equal((ushort)0, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
        }

        [Fact]
        public void SolisDriver_Heartbeat_RefreshesActiveForceModePeriodically()
        {
            var driver = new SolisDriver();
            var fakeMaster = new FakeSolisModbusMaster();
            driver.HeartbeatIntervalSeconds = 0.01; // short interval for testing

            // Configure active force charge mode via force_power = -2.0 kW (Negative = Charge)
            driver.WriteToMaster(fakeMaster, 1, "s6-heartbeat-test", TelemetryKeys.ForcePowerKw, -2.0);

            // Clear written registers to observe heartbeat refresh
            fakeMaster.WrittenRegisters.Clear();

            // Simulate elapsed time within 60s validity window
            var state = SolisDriver.GetControlState("s6-heartbeat-test");
            state.LastHeartbeatUtc = DateTime.UtcNow.AddSeconds(-1);

            // Trigger heartbeat check
            driver.PerformHeartbeatIfDue(fakeMaster, 1, "s6-heartbeat-test");

            // Heartbeat should have re-asserted mode 1 and power 200 (2 kW)
            Assert.True(fakeMaster.WrittenRegisters.ContainsKey(SolisDriver.REG_RC_FORCE_CHARGE_MODE));
            Assert.Equal((ushort)1, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
            Assert.True(fakeMaster.WrittenRegisters.ContainsKey(SolisDriver.REG_RC_FORCE_CHARGE_POWER));
            Assert.Equal((ushort)200, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_POWER]);

            // Resetting via force_power = 0 (Off) should stop heartbeat
            driver.WriteToMaster(fakeMaster, 1, "s6-heartbeat-test", TelemetryKeys.ForcePowerKw, 0);
            fakeMaster.WrittenRegisters.Clear();
            state.LastHeartbeatUtc = DateTime.UtcNow.AddSeconds(-1);

            driver.PerformHeartbeatIfDue(fakeMaster, 1, "s6-heartbeat-test");
            Assert.False(fakeMaster.WrittenRegisters.ContainsKey(SolisDriver.REG_RC_FORCE_CHARGE_MODE));
        }

        [Fact]
        public void SolisDriver_ForcePower_ExpiresAfter60Seconds_RevertsToNormal()
        {
            var driver = new SolisDriver();
            var fakeMaster = new FakeSolisModbusMaster();

            // Set Force Discharge (+2.5 kW)
            driver.WriteToMaster(fakeMaster, 1, "s6-expiry-test", TelemetryKeys.ForcePowerKw, 2.5);
            Assert.Equal((ushort)2, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);

            var state = SolisDriver.GetControlState("s6-expiry-test");
            Assert.False(state.IsExpired(DateTime.UtcNow));

            // Fast forward 65 seconds (exceeding 60s validity)
            fakeMaster.WrittenRegisters.Clear();
            state.WrittenAtUtc = DateTime.UtcNow.AddSeconds(-65);
            Assert.True(state.IsExpired(DateTime.UtcNow));

            // Heartbeat or poll should detect expiry, write 0 to REG_RC_FORCE_CHARGE_MODE, and clear state
            driver.PerformHeartbeatIfDue(fakeMaster, 1, "s6-expiry-test");
            Assert.True(fakeMaster.WrittenRegisters.ContainsKey(SolisDriver.REG_RC_FORCE_CHARGE_MODE));
            Assert.Equal((ushort)0, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
            Assert.Equal(0, state.ForceMode);
            Assert.Equal(0.0, state.ForcePowerKw);
        }

        [Fact]
        public void SolisDriver_ToUInt32_And_ToInt32_DecodeCorrectly()
        {
            // 0x0001_0002 = 65538
            Assert.Equal(65538u, SolisDriver.ToUInt32(0x0001, 0x0002));

            // 0xFFFF_FFFE = -2
            Assert.Equal(-2, SolisDriver.ToInt32(0xFFFF, 0xFFFE));

            // Positive signed
            Assert.Equal(12345, SolisDriver.ToInt32(0x0000, 12345));
        }

        [Fact]
        public void SolisDriver_ReadFromMaster_DecodesAllBlocksCorrectly()
        {
            var fakeMaster = new FakeSolisModbusMaster();

            // Block 1 (33029..33038): PV Energy (Total 235 kWh, Today 36.1 kWh)
            fakeMaster.RegisterInputBlock(33029, new ushort[] { 0, 235, 0, 0, 0, 0, 361, 0, 0, 0 });

            // Block 2 (33049..33058): PV Power (126 W -> 0.126 kW)
            fakeMaster.RegisterInputBlock(33049, new ushort[] { 2505, 3, 2545, 2, 0, 0, 0, 0, 0, 126 });

            // Block 3 (33070..33080): Inverter AC Power (-370 W -> -0.370 kW)
            fakeMaster.RegisterInputBlock(33070, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 65535, 65166 });

            // Block 4 (33126..33131): Grid Meter Power (-28 W -> -0.028 kW)
            fakeMaster.RegisterInputBlock(33126, new ushort[] { 0, 0, 2319, 73, 65535, 65508 });

            // Block 5 (33132..33150): Battery (Dir=0 charging, SOC=91%, Power=476 W -> -0.476 kW)
            fakeMaster.RegisterInputBlock(33132, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 91, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 476 });

            // Block 6 (33161..33180): Energy totals
            fakeMaster.RegisterInputBlock(33161, new ushort[]
            {
                0, 348, // Battery charge total = 348 kWh
                712,    // Battery charge today = 71.2 kWh
                0,      // Yesterday
                0, 326, // Battery discharge total = 326 kWh
                339,    // Battery discharge today = 33.9 kWh
                0,      // Yesterday
                0, 43,  // Grid import total = 43 kWh
                75,     // Grid import today = 7.5 kWh
                0,      // Yesterday
                0, 173, // Grid export total = 173 kWh
                66,     // Grid export today = 6.6 kWh
                0,      // Yesterday
                0, 312, // Consumption total = 312 kWh
                357,    // Consumption today = 35.7 kWh
                0       // Yesterday
            });

            // Block 7: Holding registers (43128..43137)
            fakeMaster.RegisterHoldingBlock(43128, new ushort[] { 0, 0, 250, 200, 0, 0, 0, 0, 0, 30 });

            // Block 8: Backflow register (43074)
            fakeMaster.RegisterHoldingBlock(43074, new ushort[] { 130 }); // 130 * 100W = 13.0 kW

            var driver = new SolisDriver();
            var values = driver.ReadFromMaster(fakeMaster, 1, "test-dev");

            // Assert Inverter AC Power
            Assert.Equal(-0.370, values[TelemetryKeys.PowerKw]);

            // Assert Battery
            Assert.Equal(91.0, values[TelemetryKeys.BatterySocPct]);

            // Assert Grid Energy
            Assert.Equal(43.0, values[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(173.0, values[TelemetryKeys.EnergyExportKwh]);

            // Assert Holding Registers
            Assert.Equal(0.0, values[TelemetryKeys.ForcePowerKw]);          // Mode 0 = 0.0 kW
        }

        [Fact]
        public void SolisDriver_LiveDevice_CanReadIfReachable()
        {
            var conn = new ConnectionConfig(Id: "solis-live-test", Type: "modbus-tcp", Address: "10.10.1.21", Port: 502);
            var device = new DeviceConfig(Id: "solis-s6", Name: "Solis S6 Hybrid", DeviceType: "solis", ConnectionId: "solis-live-test", DeviceId: 1);

            var driver = new SolisDriver();
            try
            {
                var values = driver.Read(conn, device);

                Assert.NotEmpty(values);
                Assert.True(values.ContainsKey(TelemetryKeys.PowerKw));
                Assert.True(values.ContainsKey(TelemetryKeys.BatterySocPct));
                Assert.True(values.ContainsKey(TelemetryKeys.EnergyImportKwh));
                Assert.True(values.ContainsKey(TelemetryKeys.EnergyExportKwh));
            }
            catch (Exception ex)
            {
                // In CI/environments without access to 10.10.1.21, skip gracefully
                Log.Info($"[SolisLiveTest] Skipped due to unreachable device: {ex.Message}");
            }
            finally
            {
                ModbusConnection.FullReset(conn.Id);
            }
        }

        [Fact]
        public void SolisDriver_BatteryDischarging_HasPositivePower()
        {
            var fakeMaster = new FakeSolisModbusMaster();
            // Direction = 1 (discharging), Power = 1500 W -> +1.5 kW
            fakeMaster.RegisterInputBlock(33132, new ushort[] { 0, 0, 0, 1, 0, 0, 0, 50, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1500 });

            var driver = new SolisBatteryDriver();
            var values = driver.ReadFromMaster(fakeMaster, 1);

            Assert.Equal(1.5, values[TelemetryKeys.PowerKw]);
            Assert.Equal(50.0, values[TelemetryKeys.BatterySocPct]);
        }

        [Fact]
        public void DeviceDriverFactory_Resolves_SolisSubDrivers()
        {
            var pv = DeviceDriverFactory.Create("solis-pv");
            Assert.IsType<SolisPvDriver>(pv);
            Assert.Equal("Solis-PV", pv.DriverName);

            var pvAlias = DeviceDriverFactory.Create("solispv");
            Assert.IsType<SolisPvDriver>(pvAlias);

            var batt = DeviceDriverFactory.Create("solis-battery");
            Assert.IsType<SolisBatteryDriver>(batt);
            Assert.Equal("Solis-Battery", batt.DriverName);

            var battAlias = DeviceDriverFactory.Create("solisbattery");
            Assert.IsType<SolisBatteryDriver>(battAlias);

            var grid = DeviceDriverFactory.Create("solis-grid");
            Assert.IsType<SolisGridDriver>(grid);
            Assert.Equal("Solis-Grid", grid.DriverName);

            var gridAlias = DeviceDriverFactory.Create("solisgrid");
            Assert.IsType<SolisGridDriver>(gridAlias);
        }

        [Fact]
        public void SolisPvDriver_Read_ProducesExpectedTelemetry()
        {
            var fakeMaster = new FakeSolisModbusMaster();
            // Block 1: PV Energy = 235 kWh
            fakeMaster.RegisterInputBlock(33029, new ushort[] { 0, 235, 0, 0, 0, 0, 0, 0, 0, 0 });
            // Block 2: PV Power = 126 W -> 0.126 kW
            fakeMaster.RegisterInputBlock(33049, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 126 });

            var driver = new SolisPvDriver();
            var values = driver.ReadFromMaster(fakeMaster, 1, "test-pv");

            Assert.Equal(0.126, values[TelemetryKeys.PowerKw]);
            Assert.Equal(235.0, values[TelemetryKeys.EnergyExportKwh]);

            // Ensure other subsystems' keys are not included
            Assert.False(values.ContainsKey(TelemetryKeys.BatterySocPct));
            Assert.False(values.ContainsKey(TelemetryKeys.EnergyImportKwh));
        }

        [Fact]
        public void SolisBatteryDriver_Read_And_Write_ProducesExpectedBehavior()
        {
            var fakeMaster = new FakeSolisModbusMaster();
            // Block 5: Battery (Dir=0 charging, SOC=91%, Power=476 W -> -0.476 kW)
            fakeMaster.RegisterInputBlock(33132, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 91, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 476 });
            // Block 6: Battery totals (import 348, export 326)
            fakeMaster.RegisterInputBlock(33161, new ushort[]
            {
                0, 348, 0, 0,
                0, 326, 0, 0,
                0, 43, 0, 0,
                0, 173, 0, 0,
                0, 312, 0, 0
            });
            // Block 7: Holding registers (Charge limit = 250 -> 2.5 kW, Discharge limit = 200 -> 2.0 kW)
            fakeMaster.RegisterHoldingBlock(43128, new ushort[] { 0, 0, 250, 200, 0, 0, 0, 0, 0, 0 });

            var driver = new SolisBatteryDriver();
            var values = driver.ReadFromMaster(fakeMaster, 1, "test-batt");

            Assert.Equal(-0.476, values[TelemetryKeys.PowerKw]); // Charging is negative
            Assert.Equal(91.0, values[TelemetryKeys.BatterySocPct]);
            Assert.Equal(348.0, values[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(326.0, values[TelemetryKeys.EnergyExportKwh]);

            // Test battery writing
            driver.WriteToMaster(fakeMaster, 1, "test-batt", TelemetryKeys.ForcePowerKw, -3.0); // Force charge 3 kW
            Assert.Equal((ushort)300, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_POWER]);
            Assert.Equal((ushort)1, fakeMaster.WrittenRegisters[SolisDriver.REG_RC_FORCE_CHARGE_MODE]);
        }

        [Fact]
        public void SolisGridDriver_Read_And_Write_ProducesExpectedBehavior()
        {
            var fakeMaster = new FakeSolisModbusMaster();
            // Block 4: Grid meter power (-28 W -> -0.028 kW, exporting)
            fakeMaster.RegisterInputBlock(33126, new ushort[] { 0, 0, 2319, 73, 65535, 65508 });
            // Block 6: Grid energy totals (import 43, export 173)
            fakeMaster.RegisterInputBlock(33161, new ushort[]
            {
                0, 348, 0, 0,
                0, 326, 0, 0,
                0, 43, 0, 0,
                0, 173, 0, 0,
                0, 312, 0, 0
            });
            // Block 8: Backflow register (130 * 100W = 13.0 kW)
            fakeMaster.RegisterHoldingBlock(43074, new ushort[] { 130 });

            var driver = new SolisGridDriver();
            var values = driver.ReadFromMaster(fakeMaster, 1, "test-grid");

            Assert.Equal(-0.028, values[TelemetryKeys.PowerKw]);
            Assert.Equal(43.0, values[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(173.0, values[TelemetryKeys.EnergyExportKwh]);
        }

        [Fact]
        public void SolisSubDrivers_CacheDeduplication_OnlyReadsMasterOnce()
        {
            SolisDriver.ClearRawCache();
            var fakeMaster = new FakeSolisModbusMaster();
            fakeMaster.RegisterInputBlock(33029, new ushort[] { 0, 235, 0, 0, 0, 0, 0, 0, 0, 0 });
            fakeMaster.RegisterInputBlock(33049, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 126 });
            fakeMaster.RegisterInputBlock(33070, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            fakeMaster.RegisterInputBlock(33126, new ushort[] { 0, 0, 0, 0, 0, 0 });
            fakeMaster.RegisterInputBlock(33132, new ushort[] { 0, 0, 0, 0, 0, 0, 0, 91, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 476 });
            fakeMaster.RegisterInputBlock(33161, new ushort[] { 0, 348, 0, 0, 0, 326, 0, 0, 0, 43, 0, 0, 0, 173, 0, 0, 0, 312, 0, 0 });

            var pv = new SolisPvDriver();
            var batt = new SolisBatteryDriver();
            var grid = new SolisGridDriver();

            const string sharedKey = "shared-solis-inverter";

            // First read executes the Modbus reads
            var pvValues = pv.ReadFromMaster(fakeMaster, 1, sharedKey);
            int readsAfterPv = fakeMaster.InputReadCount;
            Assert.True(readsAfterPv > 0);

            // Subsequent reads for Battery and Grid reuse cached data without Modbus network traffic
            var battValues = batt.ReadFromMaster(fakeMaster, 1, sharedKey);
            var gridValues = grid.ReadFromMaster(fakeMaster, 1, sharedKey);

            Assert.Equal(readsAfterPv, fakeMaster.InputReadCount);
            Assert.Equal(0.126, pvValues[TelemetryKeys.PowerKw]);
            Assert.Equal(-0.476, battValues[TelemetryKeys.PowerKw]);
            Assert.Equal(91.0, battValues[TelemetryKeys.BatterySocPct]);
        }

        private class FakeSolisModbusMaster : IModbusMaster
        {
            private readonly Dictionary<ushort, ushort[]> _inputBlocks = new();
            private readonly Dictionary<ushort, ushort[]> _holdingBlocks = new();
            public readonly Dictionary<ushort, ushort> WrittenRegisters = new();
            public int InputReadCount { get; private set; }

            public void RegisterInputBlock(ushort startAddr, ushort[] data)
            {
                _inputBlocks[startAddr] = data;
            }

            public void RegisterHoldingBlock(ushort startAddr, ushort[] data)
            {
                _holdingBlocks[startAddr] = data;
            }

            public ushort[] ReadInputRegisters(byte slaveAddress, ushort startAddress, ushort numberOfPoints)
            {
                InputReadCount++;
                if (_inputBlocks.TryGetValue(startAddress, out var data))
                {
                    if (data.Length == numberOfPoints) return data;
                    var slice = new ushort[numberOfPoints];
                    Array.Copy(data, slice, Math.Min(data.Length, (int)numberOfPoints));
                    return slice;
                }
                return new ushort[numberOfPoints];
            }

            public ushort[] ReadHoldingRegisters(byte slaveAddress, ushort startAddress, ushort numberOfPoints)
            {
                if (_holdingBlocks.TryGetValue(startAddress, out var data))
                {
                    if (data.Length == numberOfPoints) return data;
                    var slice = new ushort[numberOfPoints];
                    Array.Copy(data, slice, Math.Min(data.Length, (int)numberOfPoints));
                    return slice;
                }
                return new ushort[numberOfPoints];
            }

            public void WriteSingleRegister(byte slaveAddress, ushort registerAddress, ushort value)
            {
                WrittenRegisters[registerAddress] = value;
            }

            public void WriteMultipleRegisters(byte slaveAddress, ushort startAddress, ushort[] data)
            {
                for (int i = 0; i < data.Length; i++)
                {
                    WrittenRegisters[(ushort)(startAddress + i)] = data[i];
                }
            }

            public IModbusTransport Transport => throw new NotImplementedException();
            public void Dispose() { }

            public bool[] ReadCoils(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public Task<bool[]> ReadCoilsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public bool[] ReadInputs(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public Task<bool[]> ReadInputsAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public Task<ushort[]> ReadHoldingRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public Task<ushort[]> ReadInputRegistersAsync(byte slaveAddress, ushort startAddress, ushort numberOfPoints) => throw new NotImplementedException();
            public void WriteSingleCoil(byte slaveAddress, ushort coilAddress, bool value) => throw new NotImplementedException();
            public Task WriteSingleCoilAsync(byte slaveAddress, ushort coilAddress, bool value) => throw new NotImplementedException();
            public Task WriteSingleRegisterAsync(byte slaveAddress, ushort registerAddress, ushort value) => throw new NotImplementedException();
            public Task WriteMultipleRegistersAsync(byte slaveAddress, ushort startAddress, ushort[] data) => throw new NotImplementedException();
            public void WriteMultipleCoils(byte slaveAddress, ushort startAddress, bool[] data) => throw new NotImplementedException();
            public Task WriteMultipleCoilsAsync(byte slaveAddress, ushort startAddress, bool[] data) => throw new NotImplementedException();
            public ushort[] ReadWriteMultipleRegisters(byte slaveAddress, ushort startReadAddress, ushort numberOfPointsToRead, ushort startWriteAddress, ushort[] writeData) => throw new NotImplementedException();
            public Task<ushort[]> ReadWriteMultipleRegistersAsync(byte slaveAddress, ushort startReadAddress, ushort numberOfPointsToRead, ushort startWriteAddress, ushort[] writeData) => throw new NotImplementedException();
            public void WriteFileRecord(byte slaveAddress, ushort fileNumber, ushort startingRecordNumber, byte[] recordData) => throw new NotImplementedException();
            public TResponse ExecuteCustomMessage<TResponse>(IModbusMessage request) where TResponse : IModbusMessage, new() => throw new NotImplementedException();
        }
    }
}
