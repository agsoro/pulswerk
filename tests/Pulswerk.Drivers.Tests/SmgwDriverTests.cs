// SmgwDriverTests.cs – Unit tests for Smart Meter Gateway parser and driver
using System;
using System.Collections.Generic;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.Smgw;
using Xunit;

namespace Pulswerk.Drivers.Tests
{
    public class SmgwDriverTests
    {
        private const string SampleMeterFormHtml = @"
<!DOCTYPE html>
<html>
<head><title>SMGW</title></head>
<body>
<form id=""meterform"">
    <select id=""meterform_select_meter"">
        <option value=""1"">1 EFR 24 75081296</option>
        <option value=""2"">1 EMH 01 12345678</option>
    </select>
</form>
</body>
</html>";

        private const string SampleShowMeterProfileHtml = @"
<!DOCTYPE html>
<html>
<head><title>Zählerwerte</title></head>
<body>
<table id=""metervalue"">
    <tr>
        <th>Zeitstempel</th><th>OBIS</th><th>Wert</th><th>Einheit</th>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:1.8.0*255</td>
        <td id=""table_metervalues_col_wert"">15320,4500</td>
        <td id=""table_metervalues_col_einheit"">kWh</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:2.8.0*255</td>
        <td id=""table_metervalues_col_wert"">8412,1200</td>
        <td id=""table_metervalues_col_einheit"">kWh</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:16.7.0*255</td>
        <td id=""table_metervalues_col_wert"">3450,0</td>
        <td id=""table_metervalues_col_einheit"">W</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:32.7.0*255</td>
        <td id=""table_metervalues_col_wert"">230.4</td>
        <td id=""table_metervalues_col_einheit"">V</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:52.7.0*255</td>
        <td id=""table_metervalues_col_wert"">231.1</td>
        <td id=""table_metervalues_col_einheit"">V</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:72.7.0*255</td>
        <td id=""table_metervalues_col_wert"">229.8</td>
        <td id=""table_metervalues_col_einheit"">V</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:31.7.0*255</td>
        <td id=""table_metervalues_col_wert"">5.2</td>
        <td id=""table_metervalues_col_einheit"">A</td>
    </tr>
    <tr>
        <td id=""table_metervalues_col_timestamp"">2026-09-09 13:45:00</td>
        <td id=""table_metervalues_col_obis"">1-0:14.7.0*255</td>
        <td id=""table_metervalues_col_wert"">50.01</td>
        <td id=""table_metervalues_col_einheit"">Hz</td>
    </tr>
</table>
</body>
</html>";

        private const string SampleSwVersionsHtml = @"
<table>
    <tr><th>Component</th><th>Version</th><th>Checksum</th></tr>
    <tr><td>smgw-bootstream</td><td>1.2.3</td><td>abc1234</td></tr>
    <tr><td>smgw-services</td><td>4.5.6</td><td>def5678</td></tr>
</table>";

        [Fact]
        public void ParseMeters_ExtractsAllMetersFromSelect()
        {
            var meters = SmgwParser.ParseMeters(SampleMeterFormHtml);

            Assert.Equal(2, meters.Count);
            Assert.Equal("1", meters[0].Mid);
            Assert.Equal("1 EFR 24 75081296", meters[0].Name);
            Assert.Equal("2", meters[1].Mid);
            Assert.Equal("1 EMH 01 12345678", meters[1].Name);
        }

        [Fact]
        public void ParseMeterReadings_ExtractsAndNormalizesValues()
        {
            var values = SmgwParser.ParseMeterReadings(SampleShowMeterProfileHtml);

            Assert.Contains(TelemetryKeys.EnergyImportKwh, values.Keys);
            Assert.Contains(TelemetryKeys.EnergyExportKwh, values.Keys);
            Assert.Contains(TelemetryKeys.PowerKw, values.Keys);

            // 15320,45 kWh -> 15320.45
            Assert.Equal(15320.45, (double)values[TelemetryKeys.EnergyImportKwh]);
            // 8412,12 kWh -> 8412.12
            Assert.Equal(8412.12, (double)values[TelemetryKeys.EnergyExportKwh]);
            // 3450 W -> 3.45 kW
            Assert.Equal(3.45, (double)values[TelemetryKeys.PowerKw]);

            // Voltages
            Assert.Equal(230.4, (double)values["voltage_l1"]);
            Assert.Equal(231.1, (double)values["voltage_l2"]);
            Assert.Equal(229.8, (double)values["voltage_l3"]);

            // Current
            Assert.Equal(5.2, (double)values["current_l1"]);

            // Frequency
            Assert.Equal(50.01, (double)values["frequency"]);
        }

        [Fact]
        public void ParseDetailedReadings_CapturesRawAndNormalizedObis()
        {
            var detailed = SmgwParser.ParseDetailedReadings(SampleShowMeterProfileHtml);

            Assert.NotEmpty(detailed);
            var imp = detailed.Find(r => r.ObisNormalized == "1.8.0");
            Assert.NotNull(imp);
            Assert.Equal("1-0:1.8.0*255", imp.ObisRaw);
            Assert.Equal("kWh", imp.Unit);
            Assert.NotNull(imp.Timestamp);
        }

        [Fact]
        public void ParseFirmwareVersions_ExtractsComponentsAndVersions()
        {
            var versions = SmgwParser.ParseFirmwareVersions(SampleSwVersionsHtml);

            Assert.Equal(2, versions.Count);
            Assert.Equal("smgw-bootstream", versions[0].Component);
            Assert.Equal("1.2.3", versions[0].Version);
            Assert.Equal("smgw-services", versions[1].Component);
            Assert.Equal("4.5.6", versions[1].Version);
        }

        [Theory]
        [InlineData("1-0:1.8.0*255", "1.8.0")]
        [InlineData("1-1:2.8.0*255", "2.8.0")]
        [InlineData("16.7.0*255", "16.7.0")]
        [InlineData("1-0:16.7.0", "16.7.0")]
        [InlineData("1.8.0", "1.8.0")]
        public void NormalizeObis_StripsPrefixAndSuffix(string input, string expected)
        {
            Assert.Equal(expected, SmgwParser.NormalizeObis(input));
        }

        [Fact]
        public void SmgwDriver_ExposesCorrectKeysAndUnits()
        {
            var driver = new SmgwDriver();
            var keys = new HashSet<string>(driver.GetTelemetryKeys());

            Assert.Contains(TelemetryKeys.PowerKw, keys);
            Assert.Contains(TelemetryKeys.EnergyImportKwh, keys);
            Assert.Contains(TelemetryKeys.EnergyExportKwh, keys);
            Assert.Contains("voltage_l1", keys);
            Assert.Contains("current_l1", keys);
            Assert.Contains("frequency", keys);

            var units = driver.GetTelemetryUnits();
            Assert.Equal(Units.Kilowatt, units[TelemetryKeys.PowerKw]);
            Assert.Equal(Units.KilowattHour, units[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(Units.KilowattHour, units[TelemetryKeys.EnergyExportKwh]);
            Assert.Equal(Units.Volt, units["voltage_l1"]);
            Assert.Equal(Units.Ampere, units["current_l1"]);
            Assert.Equal(Units.Hertz, units["frequency"]);
        }

        [Fact]
        public void SmgwDriver_AssetHierarchy_BuildsCorrectNode()
        {
            var driver = new SmgwDriver();
            var dev = new DeviceConfig(
                Id: "efr-meter",
                Name: "EFR Netzzähler",
                DeviceType: "smgw",
                ConnectionId: "conn-smgw",
                MeterId: "1 EFR 24 75081296",
                Path: new List<string> { "Netz", "Zähler" },
                TelemetryKeys: new List<string> { "energy_export" }
            );

            var node = driver.GetAssetHierarchy(dev);
            Assert.Equal("efr-meter", node.Id);
            Assert.Equal("EFR Netzzähler", node.Name);
            Assert.Single(node.Telemetries);
            Assert.Equal("efr-meter_energy_export", node.Telemetries[0].Id);
        }

        [Fact]
        public void SmgwDriver_AssetHierarchy_DoesNotExposeUnavailableKeys_WhenInstanceRestricted()
        {
            var driver = new SmgwDriver();
            var dev = new DeviceConfig(
                Id: "efr-grid-meter",
                Name: "Abrechnungszähler",
                DeviceType: "smgw",
                ConnectionId: "conn-smgw",
                MeterId: "1 EFR 24 75081296",
                TelemetryKeys: new List<string> { "energy_export" }
            );

            var node = driver.GetAssetHierarchy(dev);
            Assert.NotEmpty(node.Telemetries);
            Assert.Contains(node.Telemetries, t => t.Id == "efr-grid-meter_energy_export");
            // Must not contain values that are not available on this instance
            Assert.DoesNotContain(node.Telemetries, t => t.Id == "efr-grid-meter_power");
            Assert.DoesNotContain(node.Telemetries, t => t.Id == "efr-grid-meter_energy_import");
            Assert.DoesNotContain(node.Telemetries, t => t.Id == "efr-grid-meter_voltage_l1");
            Assert.DoesNotContain(node.Telemetries, t => t.Id == "efr-grid-meter_current_l1");
            Assert.DoesNotContain(node.Telemetries, t => t.Id == "efr-grid-meter_frequency");
        }

        [Fact]
        public void DeviceDriverFactory_CreatesSmgwDriver()
        {
            var driver = DeviceDriverFactory.Create("smgw");
            Assert.NotNull(driver);
            Assert.IsType<SmgwDriver>(driver);
        }
    }
}
