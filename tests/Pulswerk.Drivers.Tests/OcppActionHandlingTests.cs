using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    /// <summary>
    /// Actions that ABB charge points send but that carry no telemetry (security events,
    /// vendor data transfers) must be answered instead of falling into the unhandled branch.
    /// </summary>
    [Collection("OcppTests")]
    public class OcppActionHandlingTests : IDisposable
    {
        private readonly OcppManagerService _service = OcppManagerService.Instance;
        private const string CpId = "test-charger-actions";

        public OcppActionHandlingTests()
        {
            _service.ClearTransactionsForTest();
        }

        public void Dispose()
        {
            _service.ClearTransactionsForTest();
        }

        [Fact]
        public async Task SecurityEventNotification_IsHandledWithoutChangingTelemetry()
        {
            _service.SetLiveTelemetryForTest(CpId, "status", "Available");

            string json = """
            {
                "type": "FirmwareUpdated",
                "timestamp": "2026-10-27T15:17:22Z",
                "techInfo": "TAC3Z9119006710393::V1.8.33"
            }
            """;

            using var doc = JsonDocument.Parse(json);
            await _service.HandleOcppCallAsync(CpId, "msg-sec", "SecurityEventNotification", doc.RootElement);

            Assert.Equal("Available", _service.GetTelemetry(CpId)["status"]);
        }

        [Fact]
        public async Task DataTransfer_IsHandledWithoutChangingTelemetry()
        {
            _service.SetLiveTelemetryForTest(CpId, "status", "Available");

            string json = """
            {
                "vendorId": "ABB",
                "messageId": "Heartbeat",
                "data": "{}"
            }
            """;

            using var doc = JsonDocument.Parse(json);
            await _service.HandleOcppCallAsync(CpId, "msg-dt", "DataTransfer", doc.RootElement);

            Assert.Equal("Available", _service.GetTelemetry(CpId)["status"]);
        }
    }
}
