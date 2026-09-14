using System.Threading.Tasks;
using Xunit;
using Pulswerk.Core;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    /// <summary>
    /// Tests the OCPP force_power (kW) setpoint and current/phase resolution logic,
    /// ensuring direct power control in kW without percentage conversions.
    /// </summary>
    [Collection("OcppTests")]
    public class OcppLimitTests
    {
        private const string Cp = "test-cp";
        private static OcppManagerService Svc => OcppManagerService.Instance;

        // ── Constants ───────────────────────────────────────────────────────
        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal(16.0, OcppManagerService.DefaultMaxCurrentAmps);
            Assert.Equal(3, OcppManagerService.MaxPhases);
            Assert.Equal(6.0, OcppManagerService.MinCurrentAmps);
        }

        // ── ResolveForcePower: Sub-minimum shuts off (0A) ────────────────────
        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(1.37)]
        public void ResolveForcePower_SubMinimum_ShutsOff(double powerKw)
        {
            var (amps, phases) = OcppManagerService.ResolveForcePower(powerKw);
            Assert.Equal(0.0, amps);
            Assert.Equal(1, phases);
        }

        // ── ResolveForcePower: Negative reverts to unrestricted (16A, 3p) ───
        [Theory]
        [InlineData(-1.0)]
        [InlineData(-10.0)]
        public void ResolveForcePower_Negative_ReturnsUnrestricted(double powerKw)
        {
            var (amps, phases) = OcppManagerService.ResolveForcePower(powerKw);
            Assert.Equal(16.0, amps);
            Assert.Equal(3, phases);
        }

        // ── ResolveForcePower: 1-phase range (1.38 kW to 4.13 kW) ───────────
        [Theory]
        [InlineData(1.38, 6.0, 1)]   // 1.38 kW / 0.23 = 6.0A (minimum 1-phase)
        [InlineData(2.3, 10.0, 1)]   // 2.3 kW / 0.23 = 10.0A
        [InlineData(3.0, 13.0, 1)]   // 3.0 kW / 0.23 = 13.04A -> 13.0A
        [InlineData(3.68, 16.0, 1)]  // 3.68 kW / 0.23 = 16.0A (maximum 1-phase)
        public void ResolveForcePower_SinglePhase_Range(double powerKw, double expectedAmps, int expectedPhases)
        {
            var (amps, phases) = OcppManagerService.ResolveForcePower(powerKw);
            Assert.Equal(expectedAmps, amps);
            Assert.Equal(expectedPhases, phases);
        }

        // ── ResolveForcePower: 3-phase range (>= 4.14 kW) ────────────────────
        [Theory]
        [InlineData(4.14, 6.0, 3)]   // 4.14 kW / 0.69 = 6.0A (minimum 3-phase)
        [InlineData(6.9, 10.0, 3)]   // 6.9 kW / 0.69 = 10.0A
        [InlineData(8.0, 11.6, 3)]   // 8.0 kW / 0.69 = 11.59A -> 11.6A
        [InlineData(10.0, 14.5, 3)]  // 10.0 kW / 0.69 = 14.49A -> 14.5A
        [InlineData(11.0, 16.0, 3)]  // 11.0 kW / 0.69 = 15.94A -> 16.0A (capped at 16A)
        [InlineData(22.0, 16.0, 3)]  // Over capacity capped at 16.0A
        public void ResolveForcePower_ThreePhase_Range(double powerKw, double expectedAmps, int expectedPhases)
        {
            var (amps, phases) = OcppManagerService.ResolveForcePower(powerKw);
            Assert.Equal(expectedAmps, amps);
            Assert.Equal(expectedPhases, phases);
        }

        // ── ResolveForcePower: Preferred phases ──────────────────────────────
        [Fact]
        public void ResolveForcePower_PreferredSinglePhase_UsesSinglePhase()
        {
            // 3.0 kW on 1-phase
            var (amps, phases) = OcppManagerService.ResolveForcePower(3.0, preferredPhases: 1);
            Assert.Equal(13.0, amps);
            Assert.Equal(1, phases);
        }

        [Fact]
        public void ResolveForcePower_PreferredThreePhase_FallsBackIfBelowMinimum()
        {
            // 2.3 kW cannot run on 3 phases at 6A (needs 4.14 kW), so it falls back to 1 phase
            var (amps, phases) = OcppManagerService.ResolveForcePower(2.3, preferredPhases: 3);
            Assert.Equal(10.0, amps);
            Assert.Equal(1, phases);
        }

        // ── SetChargingLimitAsync & Telemetry ────────────────────────────────
        [Fact]
        public async Task SetChargingLimitAsync_UpdatesForcePowerTelemetry()
        {
            await Svc.SetChargingLimitAsync("test-cp-limit", 1, 10.0, 3, 6.9);
            var telem = Svc.GetTelemetry("test-cp-limit");

            Assert.True(telem.ContainsKey("force_power"));
            Assert.True(telem.ContainsKey("charging_phases"));
            Assert.False(telem.ContainsKey("power_limit")); // Percentage removed

            Assert.Equal(6.9, telem["force_power"]);
            Assert.Equal(3.0, telem["charging_phases"]);
        }

        [Fact]
        public async Task SetChargingLimitAsync_ZeroAmps_ReportsZeroForcePower()
        {
            await Svc.SetChargingLimitAsync("test-cp-zero", 1, 0.0, 1);
            var telem = Svc.GetTelemetry("test-cp-zero");

            Assert.True(telem.ContainsKey("force_power"));
            Assert.Equal(0.0, telem["force_power"]);
            Assert.Equal(1.0, telem["charging_phases"]);
        }

        // ── OcppDriver: Direct write of force_power ──────────────────────────
        [Fact]
        public void OcppDriver_Write_ForcePower_ActuatesWallbox()
        {
            var driver = new OcppDriver();
            var conn = new ConnectionConfig("conn-1", "ocpp-ws");
            var device = new DeviceConfig("wb-test-1", "Wallbox 1", "ocpp");

            // Write 10.0 kW
            driver.Write(conn, device, "force_power", 10.0);
            var telem = Svc.GetTelemetry("wb-test-1");

            Assert.Equal(10.0, telem["force_power"]);
            Assert.Equal(3.0, telem["charging_phases"]);

            // Write 0.0 kW (EMS pause/curtailment)
            driver.Write(conn, device, "force_power", 0.0);
            telem = Svc.GetTelemetry("wb-test-1");

            Assert.Equal(0.0, telem["force_power"]);
            Assert.Equal(1.0, telem["charging_phases"]);
        }

        [Fact]
        public void OcppDriver_TelemetryKeys_ContainForcePowerAndNoPowerLimit()
        {
            var driver = new OcppDriver();
            var keys = driver.GetTelemetryKeys();
            var units = driver.GetTelemetryUnits();

            Assert.Contains("force_power", keys);
            Assert.Contains("charging_phases", keys);
            Assert.DoesNotContain("power_limit", keys);

            Assert.Equal(Units.Kilowatt, units["force_power"]);
            Assert.False(units.ContainsKey("power_limit"));
        }
    }
}
