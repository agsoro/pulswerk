using System;
using System.Collections.Generic;
using Pulswerk.Ems;
using Xunit;

namespace Pulswerk.Core.Tests
{
    public class EnergyControlServiceTests
    {
        private EnergySourcesConfig CreateDefaultSources(double gridLimit = 8.0, double reserve = 1.0) =>
            new EnergySourcesConfig
            {
                GridMaxImportKw = gridLimit,
                BatteryMinReserveKw = reserve,
                BatteryMinSocPct = 15.0,
                BatteryFullSocPct = 98.0
            };

        [Fact]
        public void Dispatch_WhenBatteryIdleOrDischarging_EnforcesStrictBaseLimit8Kw()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                IsControllable = true,
                BaseLimitKw = 8.0,
                ActualPowerKw = 6.5,
                MinPowerKw = 1.38,
                MaxPowerKw = 22.0
            };

            // Idle battery (0 kW)
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 4.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Equal("Base Limit (8.0 kW)", wallbox.Status);

            // Discharging battery (+4 kW)
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 2.0, pvPowerKw: 0.0, batteryPowerKw: 4.0, batterySocPct: 45.0, new[] { wallbox });
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Equal("Base Limit (8.0 kW)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_WhenBatteryChargingWithSurplus_ExpandsAboveBaseLimit()
        {
            var sources = CreateDefaultSources(8.0, reserve: 1.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                IsControllable = true,
                BaseLimitKw = 8.0,
                ActualPowerKw = 7.0, // drawing 7 kW
                MinPowerKw = 1.38,
                MaxPowerKw = 22.0
            };

            // Battery absorbing 4.5 kW surplus (-4.5 kW)
            // Available surplus headroom = 4.5 - 1.0 = 3.5 kW
            // Pool = 7.0 + 3.5 = 10.5 kW
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 12.0, batteryPowerKw: -4.5, batterySocPct: 60.0, new[] { wallbox });
            Assert.Equal(10.5, wallbox.AllocatedPowerKw);
            Assert.Contains("Surplus Boost", wallbox.Status);
        }

        [Fact]
        public void Dispatch_WhenBatteryIsFull_DoesNotBoostAboveBase()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                IsControllable = true,
                BaseLimitKw = 8.0,
                ActualPowerKw = 7.5,
                MinPowerKw = 1.38,
                MaxPowerKw = 22.0
            };

            // Battery at 99% SoC (exceeds 98% full threshold)
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 5.0, batteryPowerKw: -2.0, batterySocPct: 99.0, new[] { wallbox });
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Equal("Base Limit (8.0 kW)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_MultiConsumer_PrioritizesConsumersCorrectly()
        {
            var sources = CreateDefaultSources(12.0, reserve: 1.0);

            // Wallbox (Priority 1) with 8 kW base
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                IsControllable = true,
                Priority = 1,
                BaseLimitKw = 8.0,
                ActualPowerKw = 7.0,
                MinPowerKw = 1.38,
                MaxPowerKw = 11.0
            };

            // Heat Pump (Priority 2) with 3 kW base
            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                IsControllable = true,
                Priority = 2,
                BaseLimitKw = 3.0,
                ActualPowerKw = 2.0,
                MinPowerKw = 0.5,
                MaxPowerKw = 6.0
            };

            // Uncontrollable base load
            var baseLoad = new EnergyConsumer
            {
                Id = "house",
                Name = "House Lights & Sockets",
                IsControllable = false,
                ActualPowerKw = 1.5
            };

            var consumers = new List<EnergyConsumer> { heatPump, wallbox, baseLoad };

            // Battery absorbing 6.0 kW surplus (-6.0 kW) -> available surplus = 6.0 - 1.0 = 5.0 kW
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 15.0, batteryPowerKw: -6.0, batterySocPct: 50.0, consumers);

            // Wallbox wants 7.0 + 5.0 = 12.0 kW, but MaxPowerKw is 11.0 kW.
            // Wallbox takes 11.0 kW (used surplus above base 8.0 kW = 3.0 kW).
            Assert.Equal(11.0, wallbox.AllocatedPowerKw);

            // Remaining surplus for Heat Pump: 5.0 - 3.0 = 2.0 kW.
            // Heat Pump gets base 3.0 kW + remaining surplus 2.0 kW = 5.0 kW (or actual 2.0 + 2.0 = 4.0 kW, max(3.0, 4.0) = 4.0 kW)
            Assert.Equal(4.0, heatPump.AllocatedPowerKw);

            // Uncontrollable consumer unchanged
            Assert.Equal(1.5, baseLoad.AllocatedPowerKw);
            Assert.Equal("Uncontrollable Load", baseLoad.Status);
        }

        [Fact]
        public void Dispatch_TwoTierConsumer_AllocatesBasePlusOptional()
        {
            var sources = CreateDefaultSources(10.0);
            var heatPump = new EnergyConsumer
            {
                Id = "hp-twotier",
                Name = "Heat Pump with Compressor Base and Booster Optional",
                BasePowerKw = 2.0,      // 2 kW uncontrollable compressor/pump load
                HasOptionalTier = true,
                MaxOptionalKw = 4.0,    // up to 4 kW optional booster
                ActualPowerKw = 3.5,    // currently drawing 3.5 kW (2 base + 1.5 optional)
                Priority = 1
            };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 5.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { heatPump });

            // Base 2.0 + Optional 4.0 = 6.0 kW total setpoint
            Assert.Equal(4.0, heatPump.AllocatedOptionalKw);
            Assert.Equal(6.0, heatPump.AllocatedPowerKw);
            Assert.Equal(0.0, heatPump.UnusedPowerKw);
            Assert.Contains("2.0 kW base + 4.0 kW opt", heatPump.Status);
        }

        [Fact]
        public void Dispatch_IdleConsumer_ReclaimsPowerAndRedistributes()
        {
            var sources = CreateDefaultSources(8.0); // 8 kW max import limit

            // Consumer 1: Wallbox Fleet (Priority 1), drawing 0.0 kW (idle)
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                StandbyOptionalKw = 0.0,
                ActualPowerKw = 0.0 // 0 kW drawn - detected as idle purely from drawn power
            };

            // Consumer 2: Heat Pump (Priority 2), actively requesting power
            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                Priority = 2,
                BasePowerKw = 1.0,
                HasOptionalTier = true,
                MaxOptionalKw = 5.0,
                ActualPowerKw = 3.0 // actively drawing
            };

            var consumers = new List<EnergyConsumer> { wallbox, heatPump };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, consumers);

            // Wallbox should be detected as idle:
            Assert.False(wallbox.IsActivelyDemanding);
            Assert.Equal(0.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(0.0, wallbox.AllocatedPowerKw);
            Assert.Equal(8.0, wallbox.UnusedPowerKw);
            Assert.Contains("Idle (8.0 kW redistributed)", wallbox.Status);

            // Heat Pump (Priority 2) gets the reclaimed power! Full 5.0 kW optional granted!
            Assert.True(heatPump.IsActivelyDemanding);
            Assert.Equal(5.0, heatPump.AllocatedOptionalKw);
            Assert.Equal(6.0, heatPump.AllocatedPowerKw); // 1.0 base + 5.0 optional
        }

        [Fact]
        public void Dispatch_WhenConsumerStartsDrawing_ImmediatelyRestoresPriorityAllocation()
        {
            var sources = CreateDefaultSources(8.0);

            // Wallbox Fleet begins drawing power
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 7.5 // Car is actively drawing 7.5 kW (near full capacity)!
            };

            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                Priority = 2,
                BasePowerKw = 1.0,
                HasOptionalTier = true,
                MaxOptionalKw = 5.0,
                ActualPowerKw = 3.0
            };

            var consumers = new List<EnergyConsumer> { wallbox, heatPump };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, consumers);

            // Wallbox immediately detected as actively demanding based purely on drawn power!
            Assert.True(wallbox.IsActivelyDemanding);
            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Equal(0.0, wallbox.UnusedPowerKw);
        }

        [Fact]
        public void Dispatch_WhenAllConsumersAreIdle_ArmsConsumersWithReadiness()
        {
            var sources = CreateDefaultSources(8.0);

            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 0.0
            };

            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                Priority = 2,
                BasePowerKw = 1.0,
                HasOptionalTier = true,
                MaxOptionalKw = 5.0,
                ActualPowerKw = 0.0
            };

            var consumers = new List<EnergyConsumer> { wallbox, heatPump };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 1.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, consumers);

            // Wallbox is armed with 8.0 kW ready so EV can charge immediately upon plugging in
            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Contains("Ready (8.0 kW)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_ConsumerUnderutilizingCapacity_RedistributesExcessToOthers()
        {
            var sources = CreateDefaultSources(8.0);

            // Wallbox Fleet allocated 8.0 kW, but only 1 single-phase car drawing 3.5 kW
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 3.5 // draws only 3.5 kW out of 8.0 kW
            };

            // Second consumer wants 4 kW optional
            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                Priority = 2,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 4.0,
                ActualPowerKw = 2.0
            };

            var consumers = new List<EnergyConsumer> { wallbox, heatPump };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 5.5, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, consumers);

            // Wallbox receives its actual draw 3.5 + 1.5 headroom = 5.0 kW
            Assert.Equal(5.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(3.0, wallbox.UnusedPowerKw);
            Assert.Contains("3.0 kW redistributed", wallbox.Status);

            // Heat Pump receives remaining 3.0 kW from the 8.0 kW pool!
            Assert.Equal(3.0, heatPump.AllocatedOptionalKw);
            Assert.Equal(1.0, heatPump.UnusedPowerKw);
        }

        [Fact]
        public void Dispatch_BatteryMaxPowerLimit_ClampsSurplusToBatteryRating()
        {
            var sources = CreateDefaultSources(8.0);
            sources.BatteryMaxPowerKw = 5.0; // Battery physically limited to 5.0 kW
            sources.BatteryMinReserveKw = 1.0;

            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                MaxPowerKw = 22.0,
                ActualPowerKw = 8.0
            };

            var consumers = new List<EnergyConsumer> { wallbox };

            // Sensor reports anomalous -12.0 kW charging (beyond 5 kW hardware limit)
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 15.0, batteryPowerKw: -12.0, batterySocPct: 50.0, consumers);

            // Charge clamped to BatteryMaxChargeKw (5.0 kW) -> surplus = 5.0 - 1.0 = 4.0 kW
            // Allocation = 8.0 + 4.0 = 12.0 kW (clamped to physical rating)
            Assert.Equal(12.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(12.0, wallbox.AllocatedPowerKw);
            Assert.Contains("Surplus Boost (+4 kW)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_WhenSolarExceedsBatteryMaxCharge_AccountsForGridExportSurplus()
        {
            var sources = CreateDefaultSources(8.0);
            sources.BatteryMaxPowerKw = 5.0;
            sources.BatteryMinReserveKw = 1.0;

            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallbox Fleet",
                Priority = 1,
                BasePowerKw = 0.0,
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                MaxPowerKw = 22.0,
                ActualPowerKw = 8.0
            };

            var consumers = new List<EnergyConsumer> { wallbox };

            // Battery charging at 5.0 kW (yielding 4.0 kW surplus) + 3.5 kW exported to grid (-3.5 kW)
            // Total surplus = 4.0 + 3.5 = 7.5 kW
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: -3.5, pvPowerKw: 10.0, batteryPowerKw: -5.0, batterySocPct: 60.0, consumers);

            // Allocation = 8.0 base + 7.5 surplus = 15.5 kW
            Assert.Equal(15.5, wallbox.AllocatedOptionalKw);
            Assert.Equal(15.5, wallbox.AllocatedPowerKw);
            Assert.Contains("Surplus Boost (+7.5 kW)", wallbox.Status);
        }

        // ── Rolling 24-Hour Energy Calculation Tests ─────────────────────────

        [Fact]
        public void EnergyCalculator_ConstantPowerIntegration_ProducesAccurateKwh()
        {
            var calc = new EmsRollingEnergyCalculator();
            var startTime = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);

            // Feed 1 hour of constant power (60 samples at 60s intervals)
            // Grid: 6.0 kW import
            // PV: 10.0 kW generation
            // Battery: -3.0 kW charging
            // Uncontrollable: 2.0 kW
            // Surplus: 2.0 kW
            // Wallbox: 7.0 kW
            for (int minute = 0; minute <= 60; minute++)
            {
                var time = startTime.AddMinutes(minute);
                calc.RecordSample(
                    time,
                    gridKw: 6.0,
                    pvKw: 10.0,
                    battKw: -3.0,
                    uncontrollableKw: 2.0,
                    surplusKw: 2.0,
                    new[] { ("wb-fleet", 7.0) });
            }

            var totals = calc.Get24hTotals(startTime.AddMinutes(60));

            // 1 hour at constant power = 1.0 * power (kWh)
            Assert.Equal(6.0, totals.GridImportKwh);
            Assert.Equal(0.0, totals.GridExportKwh);
            Assert.Equal(10.0, totals.PvGenerationKwh);
            Assert.Equal(3.0, totals.BatteryChargedKwh);
            Assert.Equal(0.0, totals.BatteryDischargedKwh);
            Assert.Equal(2.0, totals.UncontrollableKwh);
            Assert.Equal(2.0, totals.TotalSurplusKwh);
            Assert.True(totals.ConsumerKwh.ContainsKey("wb-fleet"));
            Assert.Equal(7.0, totals.ConsumerKwh["wb-fleet"]);
        }

        [Fact]
        public void EnergyCalculator_BidirectionalSplitting_CorrectlySeparatesInAndOut()
        {
            var calc = new EmsRollingEnergyCalculator();
            var startTime = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);

            // Period 1 (0 to 30 min): Grid importing +4 kW, Battery charging -4 kW
            for (int m = 0; m <= 30; m++)
            {
                calc.RecordSample(
                    startTime.AddMinutes(m),
                    gridKw: 4.0,
                    pvKw: 0.0,
                    battKw: -4.0,
                    uncontrollableKw: 0.0,
                    surplusKw: 0.0,
                    Array.Empty<(string, double)>());
            }

            // Period 2 (30 to 60 min): Grid exporting -6 kW, Battery discharging +2 kW
            for (int m = 31; m <= 60; m++)
            {
                calc.RecordSample(
                    startTime.AddMinutes(m),
                    gridKw: -6.0,
                    pvKw: 8.0,
                    battKw: 2.0,
                    uncontrollableKw: 0.0,
                    surplusKw: 0.0,
                    Array.Empty<(string, double)>());
            }

            var totals = calc.Get24hTotals(startTime.AddMinutes(60));

            // Grid: 0.5h * 4.0 kW import = 2.0 kWh In
            // Grid: 0.5h * 6.0 kW export = ~3.0 kWh Out (with transition minute average)
            Assert.InRange(totals.GridImportKwh, 1.9, 2.1);
            Assert.InRange(totals.GridExportKwh, 2.8, 3.1);

            // Battery: 0.5h * 4.0 kW charging = 2.0 kWh In
            // Battery: 0.5h * 2.0 kW discharging = 1.0 kWh Out
            Assert.InRange(totals.BatteryChargedKwh, 1.9, 2.1);
            Assert.InRange(totals.BatteryDischargedKwh, 0.9, 1.1);
        }

        [Fact]
        public void EnergyCalculator_Rolling24hWindow_ExpiresOlderBuckets()
        {
            var calc = new EmsRollingEnergyCalculator();
            var now = new DateTime(2026, 9, 13, 14, 0, 0, DateTimeKind.Utc);

            // Add energy 26 hours ago (outside the 24h rolling window)
            long oldMinute = new DateTimeOffset(now.AddHours(-26)).ToUnixTimeSeconds() / 60;
            calc.AddEnergy(oldMinute, gridImportKwh: 50.0, pvGenKwh: 80.0);

            // Add energy 5 hours ago (inside the 24h rolling window)
            long recentMinute = new DateTimeOffset(now.AddHours(-5)).ToUnixTimeSeconds() / 60;
            calc.AddEnergy(recentMinute, gridImportKwh: 12.5, pvGenKwh: 25.0);

            var totals = calc.Get24hTotals(now);

            // Old energy (26h ago) should be pruned from the rolling 24h window
            Assert.Equal(12.5, totals.GridImportKwh);
            Assert.Equal(25.0, totals.PvGenerationKwh);
        }

        [Fact]
        public void EnergyCalculator_StateSerialization_PreservesBucketsAcrossRestarts()
        {
            var calc = new EmsRollingEnergyCalculator();
            var now = new DateTime(2026, 9, 13, 14, 0, 0, DateTimeKind.Utc);
            long minuteKey = new DateTimeOffset(now).ToUnixTimeSeconds() / 60;

            calc.AddEnergy(
                minuteKey,
                gridImportKwh: 14.2,
                gridExportKwh: 3.5,
                pvGenKwh: 30.0,
                battChargeKwh: 8.0,
                battDischargeKwh: 6.5,
                uncontrollableKwh: 11.0,
                surplusKwh: 5.0,
                new Dictionary<string, double> { ["wb1"] = 12.0 });

            string json = calc.Serialize();
            Assert.False(string.IsNullOrWhiteSpace(json));

            var restoredCalc = new EmsRollingEnergyCalculator();
            restoredCalc.Deserialize(json);

            var totals = restoredCalc.Get24hTotals(now);
            Assert.Equal(14.2, totals.GridImportKwh);
            Assert.Equal(3.5, totals.GridExportKwh);
            Assert.Equal(30.0, totals.PvGenerationKwh);
            Assert.Equal(8.0, totals.BatteryChargedKwh);
            Assert.Equal(6.5, totals.BatteryDischargedKwh);
            Assert.Equal(11.0, totals.UncontrollableKwh);
            Assert.Equal(5.0, totals.TotalSurplusKwh);
            Assert.Equal(12.0, totals.ConsumerKwh["wb1"]);
        }
    }
}

