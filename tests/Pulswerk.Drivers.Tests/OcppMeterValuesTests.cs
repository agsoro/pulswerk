using System;
using System.Text.Json;
using Xunit;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    [Collection("OcppTests")]
    public class OcppMeterValuesTests : IDisposable
    {
        private readonly OcppManagerService _service = OcppManagerService.Instance;
        private const string CpId = "test-charger-01";

        public OcppMeterValuesTests()
        {
            _service.ClearTransactionsForTest();
        }

        public void Dispose()
        {
            _service.ClearTransactionsForTest();
        }

        [Fact]
        public void ProcessMeterValues_WithFullStandardPayload_UpdatesAllTelemetries()
        {
            string json = """
            {
                "connectorId": 1,
                "transactionId": 1001,
                "meterValue": [
                    {
                        "timestamp": "2026-09-10T12:00:00Z",
                        "sampledValue": [
                            { "value": "26096", "measurand": "Energy.Active.Import.Register", "unit": "Wh" },
                            { "value": "11040", "measurand": "Power.Active.Import", "unit": "W" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L1", "unit": "A" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L2", "unit": "A" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L3", "unit": "A" },
                            { "value": "230.0", "measurand": "Voltage", "phase": "L1", "unit": "V" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal(11.04, Convert.ToDouble(telemetry["power"]));
            Assert.Equal(26.096, Convert.ToDouble(telemetry["energy_import"]));
            Assert.Equal(16.0, Convert.ToDouble(telemetry["current"]));
            Assert.Equal(230.0, Convert.ToDouble(telemetry["voltage"]));
            Assert.Equal(3.0, Convert.ToDouble(telemetry["charging_phases"]));
            Assert.Equal("Charging", telemetry["status"]);
        }

        [Fact]
        public void ProcessMeterValues_WithoutPowerMeasurand_CalculatesPowerFromCurrentAndVoltage()
        {
            // Real wallbox scenario: sends Voltage, Energy, Current on 3 phases, but NO Power.Active.Import measurand!
            string json = """
            {
                "connectorId": 1,
                "transactionId": 1002,
                "meterValue": [
                    {
                        "timestamp": "2026-09-10T12:00:05Z",
                        "sampledValue": [
                            { "value": "26.096", "measurand": "Energy.Active.Import.Register", "unit": "kWh" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L1" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L2" },
                            { "value": "16.0", "measurand": "Current.Import", "phase": "L3" },
                            { "value": "230.0", "measurand": "Voltage", "phase": "L1", "unit": "V" },
                            { "value": "230.0", "measurand": "Voltage", "phase": "L2", "unit": "V" },
                            { "value": "230.0", "measurand": "Voltage", "phase": "L3", "unit": "V" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            // 16A * 230V * 3 phases = 11040 W = 11.04 kW
            Assert.Equal(11.04, Convert.ToDouble(telemetry["power"]));
            Assert.Equal(26.096, Convert.ToDouble(telemetry["energy_import"]));
            Assert.Equal(16.0, Convert.ToDouble(telemetry["current"]));
            Assert.Equal(230.0, Convert.ToDouble(telemetry["voltage"]));
            Assert.Equal(3.0, Convert.ToDouble(telemetry["charging_phases"]));
        }

        [Fact]
        public void ProcessMeterValues_WithPerPhasePower_SumsAllPhases()
        {
            // Wallboxes that report Power.Active.Import per phase
            string json = """
            {
                "connectorId": 1,
                "meterValue": [
                    {
                        "sampledValue": [
                            { "value": "3680", "measurand": "Power.Active.Import", "phase": "L1", "unit": "W" },
                            { "value": "3680", "measurand": "Power.Active.Import", "phase": "L2", "unit": "W" },
                            { "value": "3680", "measurand": "Power.Active.Import", "phase": "L3", "unit": "W" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            // 3.68 + 3.68 + 3.68 = 11.04 kW
            Assert.Equal(11.04, Convert.ToDouble(telemetry["power"]));
        }

        [Fact]
        public void ProcessMeterValues_WithNumericJsonTokens_DoesNotThrowAndParsesCorrectly()
        {
            // Value sent as JSON number token, e.g. "value": 16.5 instead of string "16.5"
            string json = """
            {
                "meterValue": [
                    {
                        "sampledValue": [
                            { "value": 16.5, "measurand": "Current.Import", "phase": "L1" },
                            { "value": 230.5, "measurand": "Voltage" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal(16.5, Convert.ToDouble(telemetry["current"]));
            Assert.Equal(230.5, Convert.ToDouble(telemetry["voltage"]));
        }

        [Fact]
        public void ProcessMeterValues_WithDecimalComma_ParsesCorrectly()
        {
            // European decimal comma formatting
            string json = """
            {
                "meterValue": [
                    {
                        "sampledValue": [
                            { "value": "230,30", "measurand": "Voltage" },
                            { "value": "15,5", "measurand": "Current.Import", "phase": "L1" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal(230.3, Convert.ToDouble(telemetry["voltage"]));
            Assert.Equal(15.5, Convert.ToDouble(telemetry["current"]));
        }

        [Fact]
        public void ProcessMeterValues_WithCommaSeparatedTokens_ParsesFirstToken()
        {
            // Multi-value reading like Energy.Active.Import.Interval "0.000,0.000"
            string json = """
            {
                "meterValue": [
                    {
                        "sampledValue": [
                            { "value": "0.000,0.000", "measurand": "Energy.Active.Import.Interval", "unit": "kWh" },
                            { "value": "50.123", "measurand": "Energy.Active.Import.Register", "unit": "kWh" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal(50.123, Convert.ToDouble(telemetry["energy_import"]));
        }

        [Fact]
        public void ProcessMeterValues_WithPascalCaseProperties_ParsesCorrectly()
        {
            string json = """
            {
                "ConnectorId": 1,
                "MeterValue": [
                    {
                        "SampledValue": [
                            { "Value": "7400", "Measurand": "Power.Active.Import", "Unit": "W" },
                            { "Value": "32.0", "Measurand": "Current.Import", "Phase": "L1" }
                        ]
                    }
                ]
            }
            """;

            using var doc = JsonDocument.Parse(json);
            _service.ProcessMeterValues(CpId, doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal(7.4, Convert.ToDouble(telemetry["power"]));
            Assert.Equal(32.0, Convert.ToDouble(telemetry["current"]));
        }

        [Fact]
        public async Task StartTransaction_UpdatesStatusAndEnergy()
        {
            string json = """
            {
                "connectorId": 1,
                "idTag": "RFID-USER-42",
                "meterStart": 12500,
                "timestamp": "2026-09-10T12:00:00Z"
            }
            """;

            using var doc = JsonDocument.Parse(json);
            await _service.HandleOcppCallAsync(CpId, "msg-01", "StartTransaction", doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal("Charging", telemetry["status"]);
            Assert.Equal("RFID-USER-42", telemetry["active_user"]);
            Assert.Equal(12.5, Convert.ToDouble(telemetry["energy_import"]));
        }

        [Fact]
        public async Task StopTransaction_ResetsPowerAndCurrent_AndUpdatesFinalEnergy()
        {
            // First simulate active charging
            _service.SetLiveTelemetryForTest(CpId, "power", 11.0);
            _service.SetLiveTelemetryForTest(CpId, "current", 16.0);
            _service.SetLiveTelemetryForTest(CpId, "status", "Charging");
            _service.SetLiveTelemetryForTest(CpId, "active_user", "RFID-USER-42");

            string stopJson = """
            {
                "transactionId": 1005,
                "meterStop": 25000,
                "timestamp": "2026-09-10T13:00:00Z",
                "reason": "EVDisconnected"
            }
            """;

            using var doc = JsonDocument.Parse(stopJson);
            await _service.HandleOcppCallAsync(CpId, "msg-02", "StopTransaction", doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal("Available", telemetry["status"]);
            Assert.Equal("None", telemetry["active_user"]);
            Assert.Equal(0.0, Convert.ToDouble(telemetry["power"]));
            Assert.Equal(0.0, Convert.ToDouble(telemetry["current"]));
            Assert.Equal(25.0, Convert.ToDouble(telemetry["energy_import"]));
        }

        [Fact]
        public async Task StatusNotification_SuspendedEV_ZerosPowerAndCurrent()
        {
            // Simulate car pausing / full
            _service.SetLiveTelemetryForTest(CpId, "power", 11.0);
            _service.SetLiveTelemetryForTest(CpId, "current", 16.0);
            _service.SetLiveTelemetryForTest(CpId, "status", "Charging");

            string statusJson = """
            {
                "connectorId": 1,
                "errorCode": "NoError",
                "status": "SuspendedEV"
            }
            """;

            using var doc = JsonDocument.Parse(statusJson);
            await _service.HandleOcppCallAsync(CpId, "msg-03", "StatusNotification", doc.RootElement);

            var telemetry = _service.GetTelemetry(CpId);
            Assert.Equal("SuspendedEV", telemetry["status"]);
            Assert.Equal(0.0, Convert.ToDouble(telemetry["power"]));
            Assert.Equal(0.0, Convert.ToDouble(telemetry["current"]));
        }
    }
}
