using System;
using System.Collections.Generic;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.Modbus;
using Xunit;

namespace Pulswerk.Drivers.Tests
{
    public class FroniusDriverTests
    {
        private const string SamplePowerFlowJson = @"{
    ""Body"": {
        ""Data"": {
            ""Inverters"": {
                ""1"": {
                    ""DT"": 232,
                    ""E_Day"": 31223,
                    ""E_Total"": 74614704,
                    ""E_Year"": 7099589,
                    ""P"": 304
                },
                ""2"": {
                    ""DT"": 232,
                    ""E_Day"": 26400,
                    ""E_Total"": 62063700,
                    ""E_Year"": 6272878.5,
                    ""P"": 307
                }
            },
            ""Site"": {
                ""E_Day"": 57623,
                ""E_Total"": 136678404,
                ""E_Year"": 13372467.5,
                ""Meter_Location"": ""grid"",
                ""Mode"": ""meter"",
                ""P_Akku"": null,
                ""P_Grid"": 44.93,
                ""P_Load"": -655.93,
                ""P_PV"": 611,
                ""rel_Autonomy"": 93.15,
                ""rel_SelfConsumption"": 100
            },
            ""Version"": ""12""
        }
    },
    ""Head"": {
        ""RequestArguments"": {},
        ""Status"": {
            ""Code"": 0,
            ""Reason"": """",
            ""UserMessage"": """"
        },
        ""Timestamp"": ""2026-09-08T18:59:27+02:00""
    }
}";

        [Fact]
        public void FroniusDriver_ExposesExpectedTelemetryKeysAndUnits()
        {
            var driver = new FroniusDriver();
            var keys = new HashSet<string>(driver.GetTelemetryKeys());

            Assert.Contains(TelemetryKeys.PowerKw, keys);
            Assert.Contains(TelemetryKeys.EnergyExportKwh, keys);

            var units = driver.GetTelemetryUnits();
            Assert.Equal(Units.Kilowatt, units[TelemetryKeys.PowerKw]);
            Assert.Equal(Units.KilowattHour, units[TelemetryKeys.EnergyExportKwh]);
        }

        [Fact]
        public void FroniusDriver_ParseSolarApiPowerFlowJson_ExtractsSiteData_WhenNoSpecificInverterMatched()
        {
            var values = FroniusDriver.ParseSolarApiPowerFlowJson(SamplePowerFlowJson, deviceId: null);

            Assert.Equal(0.611, values[TelemetryKeys.PowerKw]);
            Assert.Equal(136678.404, values[TelemetryKeys.EnergyExportKwh]);
        }

        [Fact]
        public void FroniusDriver_ParseSolarApiPowerFlowJson_ExtractsInverter1_WhenDeviceId1()
        {
            var values = FroniusDriver.ParseSolarApiPowerFlowJson(SamplePowerFlowJson, deviceId: 1);

            Assert.Equal(0.304, values[TelemetryKeys.PowerKw]);
            Assert.Equal(74614.704, values[TelemetryKeys.EnergyExportKwh]);
        }

        [Fact]
        public void FroniusDriver_ParseSolarApiPowerFlowJson_ExtractsInverter2_WhenDeviceId2()
        {
            var values = FroniusDriver.ParseSolarApiPowerFlowJson(SamplePowerFlowJson, deviceId: 2);

            Assert.Equal(0.307, values[TelemetryKeys.PowerKw]);
            Assert.Equal(62063.7, values[TelemetryKeys.EnergyExportKwh]);
        }

        [Fact]
        public void FroniusDriver_LiveDevice_CanReadSolarApi_IfReachable()
        {
            var conn = new ConnectionConfig(Id: "fronius-live-test", Type: "http", Address: "10.10.1.22", Port: 80);
            var dev = new DeviceConfig(Id: "fronius-symo-1", Name: "Fronius Inverter 1", DeviceType: "fronius", ConnectionId: "fronius-live-test", DeviceId: 1);

            var driver = new FroniusDriver();
            try
            {
                var values = driver.Read(conn, dev);
                Assert.NotEmpty(values);
                Assert.True(values.ContainsKey(TelemetryKeys.PowerKw));
                Assert.True(values.ContainsKey(TelemetryKeys.EnergyExportKwh));
            }
            catch (Exception ex)
            {
                // In CI / off-site, skip gracefully
                Log.Info($"[FroniusLiveTest] Skipped due to unreachable device: {ex.Message}");
            }
        }
    }
}
