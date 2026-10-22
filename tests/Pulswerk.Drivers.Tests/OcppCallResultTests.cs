using System.Text.Json;
using Xunit;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    /// <summary>
    /// A CALLRESULT only means "the charge point answered". Smart Charging calls carry a
    /// status field that may be Rejected, which must not be reported as success.
    /// </summary>
    [Collection("OcppTests")]
    public class OcppCallResultTests
    {
        private static bool Accepted(string callResultJson)
        {
            using var doc = JsonDocument.Parse(callResultJson);
            return OcppManagerService.IsCallResultAccepted(doc.RootElement);
        }

        [Fact]
        public void AcceptedStatus_IsAccepted()
        {
            Assert.True(Accepted("""[3,"msg-1",{"status":"Accepted"}]"""));
        }

        [Fact]
        public void RejectedStatus_IsNotAccepted()
        {
            Assert.False(Accepted("""[3,"msg-1",{"status":"Rejected"}]"""));
        }

        [Fact]
        public void NotSupportedStatus_IsNotAccepted()
        {
            Assert.False(Accepted("""[3,"msg-1",{"status":"NotSupported"}]"""));
        }

        [Fact]
        public void StatusComparison_IsCaseInsensitive()
        {
            Assert.True(Accepted("""[3,"msg-1",{"status":"accepted"}]"""));
        }

        [Fact]
        public void PascalCaseStatus_IsAccepted()
        {
            Assert.True(Accepted("""[3,"msg-1",{"Status":"Accepted"}]"""));
        }

        [Fact]
        public void PayloadWithoutStatus_IsAccepted()
        {
            // e.g. TriggerMessage / ChangeConfiguration answers without a status field
            Assert.True(Accepted("""[3,"msg-1",{}]"""));
        }

        [Fact]
        public void EmptyPayload_IsAccepted()
        {
            Assert.True(Accepted("""[3,"msg-1"]"""));
        }
    }
}
