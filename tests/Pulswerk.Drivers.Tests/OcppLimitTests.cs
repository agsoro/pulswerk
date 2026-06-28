using Xunit;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    /// <summary>
    /// Tests the OCPP power_limit ⇄ charge-current conversion logic, where the
    /// percentage refers to the TOTAL power capacity (16A × 3 phases = 48 "phase-A"),
    /// with a 6A-per-phase minimum and a 10% shut-off threshold.
    /// </summary>
    public class OcppLimitTests
    {
        private const string Cp = "test-cp";
        private static OcppManagerService Svc => OcppManagerService.Instance;

        // ── Constants / capacity ────────────────────────────────────────────
        [Fact]
        public void Constants_HaveExpectedValues()
        {
            Assert.Equal(16.0, OcppManagerService.DefaultMaxCurrentAmps);
            Assert.Equal(3, OcppManagerService.MaxPhases);
            Assert.Equal(6.0, OcppManagerService.MinCurrentAmps);
            Assert.Equal(10.0, OcppManagerService.MinChargePercent);
        }

        [Fact]
        public void GetMaxTotalCapacity_Is16Times3()
        {
            Assert.Equal(48.0, Svc.GetMaxTotalCapacity(Cp));
        }

        // ── AmpsToPercent (per-phase amps × phases / total) ─────────────────
        [Theory]
        [InlineData(16, 3, 100.0)] // full
        [InlineData(8, 3, 50.0)]   // half on three phases
        [InlineData(6, 3, 37.5)]   // 18 / 48
        [InlineData(6, 2, 25.0)]   // 12 / 48
        [InlineData(6, 1, 12.5)]   // 6 / 48 (minimum)
        [InlineData(0, 3, 0.0)]    // off
        public void AmpsToPercent_Cases(double amps, int phases, double expected)
        {
            Assert.Equal(expected, Svc.AmpsToPercent(Cp, amps, phases));
        }

        [Fact]
        public void AmpsToPercent_IsClampedTo100()
        {
            // 20A on 3 phases would be 125% – clamped to 100.
            Assert.Equal(100.0, Svc.AmpsToPercent(Cp, 20, 3));
        }

        // ── PercentToAmps (total / phases, per-phase capped at 16A) ─────────
        [Theory]
        [InlineData(100, 3, 16.0)] // 48 / 3
        [InlineData(50, 3, 8.0)]   // 24 / 3
        [InlineData(25, 2, 6.0)]   // 12 / 2
        [InlineData(100, 1, 16.0)] // 48 / 1 -> capped at per-phase max 16A
        [InlineData(0, 3, 0.0)]    // off
        public void PercentToAmps_Cases(double percent, int phases, double expected)
        {
            Assert.Equal(expected, Svc.PercentToAmps(Cp, percent, phases));
        }

        [Fact]
        public void AmpsToPercent_And_PercentToAmps_RoundTrip()
        {
            // 8A on 3 phases -> 50% -> back to 8A on 3 phases.
            double pct = Svc.AmpsToPercent(Cp, 8, 3);
            Assert.Equal(50.0, pct);
            Assert.Equal(8.0, Svc.PercentToAmps(Cp, pct, 3));
        }

        // ── ResolveLimit: shut-off below 10% ────────────────────────────────
        [Theory]
        [InlineData(0.0)]
        [InlineData(1.0)]
        [InlineData(5.0)]
        [InlineData(9.9)]
        public void ResolveLimit_BelowThreshold_ShutsOff(double percent)
        {
            var (amps, phases) = Svc.ResolveLimit(Cp, percent);
            Assert.Equal(0.0, amps);
            Assert.Equal(1, phases);
        }

        // ── ResolveLimit: charges at/above 10%, never below 6A per phase ────
        [Theory]
        [InlineData(10.0, 6.0, 1)]   // minimum: 4.8 phase-A bumped to 1×6A
        [InlineData(12.5, 6.0, 1)]   // exactly 6 phase-A -> 1×6A
        [InlineData(25.0, 6.0, 2)]   // 12 phase-A -> 2×6A
        [InlineData(33.333, 8.0, 2)] // 16 phase-A -> 2×8A
        [InlineData(37.5, 6.0, 3)]   // 18 phase-A -> 3×6A
        [InlineData(50.0, 8.0, 3)]   // 24 phase-A -> 3×8A
        [InlineData(100.0, 16.0, 3)] // full -> 3×16A
        public void ResolveLimit_AtOrAboveThreshold_Cases(double percent, double expectedAmps, int expectedPhases)
        {
            var (amps, phases) = Svc.ResolveLimit(Cp, percent);
            Assert.Equal(expectedAmps, amps);
            Assert.Equal(expectedPhases, phases);
        }

        [Fact]
        public void ResolveLimit_NeverBelowMinimumPerPhaseWhileCharging()
        {
            // Every chargeable percentage from the threshold up must stay >= 6A/phase.
            for (double pct = OcppManagerService.MinChargePercent; pct <= 100.0; pct += 0.5)
            {
                var (amps, _) = Svc.ResolveLimit(Cp, pct);
                Assert.True(amps >= OcppManagerService.MinCurrentAmps,
                    $"At {pct}% the per-phase current {amps}A dropped below the 6A minimum.");
            }
        }

        [Fact]
        public void ResolveLimit_NeverExceedsPerPhaseMax()
        {
            for (double pct = 0; pct <= 100.0; pct += 1.0)
            {
                var (amps, _) = Svc.ResolveLimit(Cp, pct);
                Assert.True(amps <= OcppManagerService.DefaultMaxCurrentAmps,
                    $"At {pct}% the per-phase current {amps}A exceeded the 16A maximum.");
            }
        }

        [Fact]
        public void ResolveLimit_AboveThreshold_ResultedPercentIsAtLeastMinimum()
        {
            // A small (but >= 10%) request charges minimally; the effective stored
            // percentage reflects the real 1×6A = 12.5% floor.
            var (amps, phases) = Svc.ResolveLimit(Cp, 10.0);
            double effective = Svc.AmpsToPercent(Cp, amps, phases);
            Assert.Equal(12.5, effective);
        }
    }
}
