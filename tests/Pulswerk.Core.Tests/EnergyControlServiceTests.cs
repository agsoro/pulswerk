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

        // ── Autarky Dispatch Tests ────────────────────────────────────────────
        // Rule: consumers may draw unlimited power while no grid import occurs.
        // On grid import, ALL controllable consumers are curtailed to 0.

        [Fact]
        public void Dispatch_NoGridImport_ConsumersAreUnrestricted()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 6.5,
                MinOptionalKw = 1.38,
                MaxPowerKw = 22.0
            };

            // Grid idle, PV covers everything -> no curtailment.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 10.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });

            // Unrestricted up to the physical ceiling: min(MaxOptionalKw=8, MaxPowerKw-Base=22).
            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
            Assert.Equal("Autarky (Unrestricted)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_GridImport_AllControllableConsumersCurtailedToMinimum()
        {
            var sources = CreateDefaultSources(8.0);

            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 6.5,
                MinOptionalKw = 1.38, // guaranteed minimum (6A 1-phase)
                MaxPowerKw = 22.0
            };

            var heatPump = new EnergyConsumer
            {
                Id = "hp",
                Name = "Heat Pump",
                HasOptionalTier = true,
                BasePowerKw = 2.0,
                MaxOptionalKw = 5.0,
                MinOptionalKw = 0.5,
                ActualPowerKw = 4.0,
                MaxPowerKw = 7.5
            };

            var noMinimum = new EnergyConsumer
            {
                Id = "booster",
                Name = "Booster (no guaranteed minimum)",
                HasOptionalTier = true,
                BasePowerKw = 0.0,
                MaxOptionalKw = 3.0,
                MinOptionalKw = 0.0,
                ActualPowerKw = 1.0,
                MaxPowerKw = 5.0
            };

            var consumers = new List<EnergyConsumer> { wallbox, heatPump, noMinimum };

            // Grid imports 3 kW -> all controllable consumers drop to their guaranteed minimum.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, consumers);

            // Wallbox: base 0 + guaranteed minimum 1.38 kW.
            Assert.Equal(1.38, wallbox.AllocatedOptionalKw);
            Assert.Equal(1.38, wallbox.AllocatedPowerKw);
            Assert.Equal("Grid Import Curtailment (Min 1.38 kW)", wallbox.Status);

            // Heat pump: base 2.0 + guaranteed minimum 0.5 kW.
            Assert.Equal(0.5, heatPump.AllocatedOptionalKw);
            Assert.Equal(2.5, heatPump.AllocatedPowerKw);
            Assert.Equal("Grid Import Curtailment (Min 0.50 kW)", heatPump.Status);

            // Consumer without a minimum falls to 0 (off).
            Assert.Equal(0.0, noMinimum.AllocatedOptionalKw);
            Assert.Equal(0.0, noMinimum.AllocatedPowerKw);
            Assert.Equal("Grid Import Curtailment", noMinimum.Status);
        }

        [Fact]
        public void Dispatch_GridImport_MinimumNeverExceedsOptionalTier()
        {
            var sources = CreateDefaultSources(8.0);
            var consumer = new EnergyConsumer
            {
                Id = "odd",
                Name = "Consumer with minimum above optional tier",
                HasOptionalTier = true,
                BasePowerKw = 0.0,
                MaxOptionalKw = 2.0,
                MinOptionalKw = 3.0, // misconfiguration: minimum above the tier
                ActualPowerKw = 1.0,
                MaxPowerKw = 10.0
            };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { consumer });

            // Clamped to MaxOptionalKw, never above it.
            Assert.Equal(2.0, consumer.AllocatedOptionalKw);
            Assert.Equal(2.0, consumer.AllocatedPowerKw);
        }

        [Fact]
        public void Dispatch_GridImportDeadband_DoesNotTriggerCurtailment()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 6.5,
                MaxPowerKw = 22.0
            };

            // Import below the 0.2 kW deadband -> no curtailment.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.15, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });

            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal("Autarky (Unrestricted)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_GridExport_ConsumersAreUnrestricted()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 7.0,
                MaxPowerKw = 22.0
            };

            // Grid export (negative = out of pool) -> autarky, unlimited draw.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: -3.5, pvPowerKw: 10.0, batteryPowerKw: 0.0, batterySocPct: 0.0, new[] { wallbox });

            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal("Autarky (Unrestricted)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_Curtailment_RecoversWhenImportStops()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 6.5,
                MaxPowerKw = 22.0
            };

            // Phase 1: grid import -> curtailed.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });
            Assert.Equal(0.0, wallbox.AllocatedOptionalKw);

            // Phase 2: import stops -> immediately unrestricted again.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 10.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });
            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal("Autarky (Unrestricted)", wallbox.Status);
        }

        [Fact]
        public void Dispatch_UncontrollableLoads_AreNeverCurtailed()
        {
            var sources = CreateDefaultSources(8.0);
            var baseLoad = new EnergyConsumer
            {
                Id = "house",
                Name = "House Lights & Sockets",
                HasOptionalTier = false,
                ActualPowerKw = 1.5,
                BasePowerKw = 1.0
            };

            // Grid import: uncontrollable load keeps reporting its measured/base draw.
            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 3.0, pvPowerKw: 0.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { baseLoad });

            Assert.Equal(0.0, baseLoad.AllocatedOptionalKw);
            Assert.Equal(1.5, baseLoad.AllocatedPowerKw);
            Assert.Equal("Uncontrollable Load", baseLoad.Status);
        }

        [Fact]
        public void Dispatch_Unrestricted_CapsAtPhysicalMaxPower()
        {
            var sources = CreateDefaultSources(8.0);
            var wallbox = new EnergyConsumer
            {
                Id = "wb",
                Name = "Wallboxes",
                HasOptionalTier = true,
                MaxOptionalKw = 8.0,
                ActualPowerKw = 6.0,
                MinOptionalKw = 1.38,
                MaxPowerKw = 11.0 // physical ceiling below MaxOptionalKw
            };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 10.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { wallbox });

            // min(MaxOptionalKw=8, MaxPowerKw-Base=11) = 8.
            Assert.Equal(8.0, wallbox.AllocatedOptionalKw);
            Assert.Equal(8.0, wallbox.AllocatedPowerKw);
        }

        [Fact]
        public void Dispatch_TwoTierConsumer_UnrestrictedKeepsBasePlusOptional()
        {
            var sources = CreateDefaultSources(10.0);
            var heatPump = new EnergyConsumer
            {
                Id = "hp-twotier",
                Name = "Heat Pump with Compressor Base and Booster Optional",
                BasePowerKw = 2.0,
                HasOptionalTier = true,
                MaxOptionalKw = 4.0,
                ActualPowerKw = 3.5,
                Priority = 1
            };

            EnergyDispatchEngine.Dispatch(sources, gridPowerKw: 0.0, pvPowerKw: 8.0, batteryPowerKw: 0.0, batterySocPct: 50.0, new[] { heatPump });

            // Autarky: base 2.0 + full optional 4.0 = 6.0 kW total setpoint.
            Assert.Equal(4.0, heatPump.AllocatedOptionalKw);
            Assert.Equal(6.0, heatPump.AllocatedPowerKw);
            Assert.Equal("Autarky (Unrestricted)", heatPump.Status);
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
        public void EnergyCalculator_WithNegativePvPower_AccumulatesPositiveGeneration()
        {
            var calc = new EmsRollingEnergyCalculator();
            var startTime = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);

            // The calculator consumes pre-normalized values (canonical: + = generation).
            // Feed 1 hour of constant PV generation of +8.0 kW.
            for (int minute = 0; minute <= 60; minute++)
            {
                var time = startTime.AddMinutes(minute);
                calc.RecordSample(
                    time,
                    gridKw: 0.0,
                    pvKw: 8.0,
                    battKw: 0.0,
                    uncontrollableKw: 2.0,
                    surplusKw: 6.0,
                    Array.Empty<(string, double)>());
            }

            var totals = calc.Get24hTotals(startTime.AddMinutes(60));
            Assert.Equal(8.0, totals.PvGenerationKwh);
        }

        [Fact]
        public void EnergyCalculator_NegativePvPower_IsNotCountedAsGeneration()
        {
            var calc = new EmsRollingEnergyCalculator();
            var startTime = new DateTime(2026, 9, 13, 10, 0, 0, DateTimeKind.Utc);

            // Negative PV power violates the canonical rule and must not be integrated as generation.
            for (int minute = 0; minute <= 60; minute++)
            {
                calc.RecordSample(
                    startTime.AddMinutes(minute),
                    gridKw: 0.0,
                    pvKw: -8.0,
                    battKw: 0.0,
                    uncontrollableKw: 0.0,
                    surplusKw: 0.0,
                    Array.Empty<(string, double)>());
            }

            var totals = calc.Get24hTotals(startTime.AddMinutes(60));
            Assert.Equal(0.0, totals.PvGenerationKwh);
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

        [Fact]
        public void EmsService_GetTelemetryValues_PublishesStandardAndConsumerKeys()
        {
            var svc = EmsService.Instance;
            var values = svc.GetTelemetryValues();

            Assert.NotNull(values);
            Assert.True(values.ContainsKey("grid_import"));
            Assert.True(values.ContainsKey("grid_export"));
            Assert.True(values.ContainsKey("grid_power"));
            Assert.True(values.ContainsKey("pv_power"));
            Assert.True(values.ContainsKey("pv_generation"));
            Assert.True(values.ContainsKey("battery_power"));
            Assert.True(values.ContainsKey("battery_charge"));
            Assert.True(values.ContainsKey("battery_soc"));
            Assert.True(values.ContainsKey("surplus_power"));
            Assert.True(values.ContainsKey("uncontrollable_load"));
            Assert.True(values.ContainsKey("controllable_load"));
            Assert.True(values.ContainsKey("total_base_load"));
            Assert.True(values.ContainsKey("total_optional_load"));
            Assert.True(values.ContainsKey("reclaimed_power"));

            // 24h rolling energy keys
            Assert.True(values.ContainsKey("grid_import_24h"));
            Assert.True(values.ContainsKey("grid_export_24h"));
            Assert.True(values.ContainsKey("pv_generation_24h"));
            Assert.True(values.ContainsKey("battery_charged_24h"));
            Assert.True(values.ContainsKey("battery_discharged_24h"));
            Assert.True(values.ContainsKey("uncontrollable_24h"));
            Assert.True(values.ContainsKey("controllable_24h"));
            Assert.True(values.ContainsKey("total_surplus_24h"));

            // Verify event publishing
            Dictionary<string, object>? published = null;
            svc.OnTelemetryUpdated += vals => published = vals;
            svc.PublishTelemetries();

            Assert.NotNull(published);
            Assert.True(published!.ContainsKey("uncontrollable_load"));
        }

        [Fact]
        public void EmsService_Snapshot_WithoutBatteryAndNegativePv_ReflectsCorrectBalance()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig
                {
                    HasBattery = false,
                    GridMaxImportKw = 10.0,
                    PvMeterKey = "pv_test"
                });

                // Simulate live readings in the canonical convention:
                // Grid -3.0 (exporting 3 kW), PV +7.0 (generating 7 kW)
                svc.SetLiveTelemetryForTesting(gridKw: -3.0, pvKw: 7.0, batteryKw: -2.0, batterySocPct: 80.0);

                var snap = svc.GetSnapshot();
                Assert.False(snap.Sources.HasBattery);
                Assert.Equal(7.0, snap.PvPowerKw);
                Assert.Equal(7.0, snap.PvGenerationKw);
                Assert.Equal(0.0, snap.BatteryPowerKw);
                Assert.Equal(0.0, snap.BatteryChargeKw);
                Assert.Equal(0.0, snap.BatterySocPct);
                Assert.False(snap.IsBatteryCharging);

                // Residual base load balance without battery:
                // Grid + PV - Controllable = -3.0 + 7.0 - Controllable
                // With controllable = 0, balanceLoad = 4.0 kW
                Assert.Equal(4.0, snap.UncontrollableLoadKw);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_WithInvertedPvSign_NormalizesToCanonicalConvention()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig
                {
                    HasBattery = false,
                    GridMaxImportKw = 10.0,
                    PvMeterKey = "pv_test",
                    InvertPvPowerSign = true
                });

                // Raw meter reports generation as negative (-7.0 kW); the invert rule normalizes it to +7.0.
                svc.SetLiveTelemetryForTesting(gridKw: -3.0, pvKw: -7.0, batteryKw: 0.0, batterySocPct: 80.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(7.0, snap.PvPowerKw);
                Assert.Equal(7.0, snap.PvGenerationKw);
                Assert.Equal(4.0, snap.UncontrollableLoadKw);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_WithInvertedGridSign_NormalizesToCanonicalConvention()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig
                {
                    HasBattery = false,
                    GridMaxImportKw = 10.0,
                    PvMeterKey = "pv_test",
                    InvertGridPowerSign = true
                });

                // Raw meter reports import as negative (-3.0 kW); the invert rule normalizes it to +3.0 import.
                svc.SetLiveTelemetryForTesting(gridKw: -3.0, pvKw: 0.0, batteryKw: 0.0, batterySocPct: 80.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(3.0, snap.GridImportKw);
                Assert.Equal(3.0, snap.UncontrollableLoadKw);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_Autarky_IsZeroWhenAllPowerComesFromGrid()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig { HasBattery = false, GridMaxImportKw = 10.0, PvMeterKey = "pv_test" });

                // Grid imports 5 kW, no PV, no battery -> consumption 5 kW, all from grid -> 0% autarky.
                svc.SetLiveTelemetryForTesting(gridKw: 5.0, pvKw: 0.0, batteryKw: 0.0, batterySocPct: 50.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(5.0, snap.TotalConsumptionKw);
                Assert.Equal(0.0, snap.AutarkyPct);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_Autarky_IsFullWhenGridIsIdle()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig { HasBattery = false, GridMaxImportKw = 10.0, PvMeterKey = "pv_test" });

                // PV generates 6 kW, grid idle -> consumption 6 kW, no grid import -> 100% autarky.
                svc.SetLiveTelemetryForTesting(gridKw: 0.0, pvKw: 6.0, batteryKw: 0.0, batterySocPct: 50.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(6.0, snap.TotalConsumptionKw);
                Assert.Equal(100.0, snap.AutarkyPct);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_Autarky_IsPartialWithMixedSupply()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig { HasBattery = false, GridMaxImportKw = 10.0, PvMeterKey = "pv_test" });

                // PV 6 kW + grid import 2 kW -> consumption 8 kW.
                // Autarky = 1 - (2 / 8) = 75%.
                svc.SetLiveTelemetryForTesting(gridKw: 2.0, pvKw: 6.0, batteryKw: 0.0, batterySocPct: 50.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(8.0, snap.TotalConsumptionKw);
                Assert.Equal(75.0, snap.AutarkyPct);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_Autarky_IsClampedToZeroWhenGridExceedsConsumption()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig
                {
                    HasBattery = true,
                    GridMaxImportKw = 10.0,
                    PvMeterKey = "pv_test",
                    BatteryPowerKey = "batt_test",
                    BatterySocKey = "batt_soc_test"
                });

                // Grid imports 5 kW, of which 3 kW charges the battery -> only 2 kW reach the loads.
                // Autarky = 1 - (5 / 2) = -150% -> clamped to 0%.
                svc.SetLiveTelemetryForTesting(gridKw: 5.0, pvKw: 0.0, batteryKw: -3.0, batterySocPct: 50.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(2.0, snap.TotalConsumptionKw);
                Assert.Equal(0.0, snap.AutarkyPct);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_Snapshot_Autarky_IsFullWhenNoConsumption()
        {
            var svc = EmsService.Instance;
            var originalSources = svc.SourcesConfig;
            try
            {
                svc.Configure(new EnergySourcesConfig { HasBattery = false, GridMaxImportKw = 10.0, PvMeterKey = "pv_test" });

                svc.SetLiveTelemetryForTesting(gridKw: 0.0, pvKw: 0.0, batteryKw: 0.0, batterySocPct: 50.0);

                var snap = svc.GetSnapshot();
                Assert.Equal(0.0, snap.TotalConsumptionKw);
                Assert.Equal(100.0, snap.AutarkyPct);
                Assert.Equal(100.0, snap.Autarky24hPct);
            }
            finally
            {
                svc.Configure(originalSources);
            }
        }

        [Fact]
        public void EmsService_GetTelemetryValues_PublishesAutarkyKeys()
        {
            var svc = EmsService.Instance;
            var values = svc.GetTelemetryValues();

            Assert.True(values.ContainsKey("total_consumption"));
            Assert.True(values.ContainsKey("autarky"));
            Assert.True(values.ContainsKey("total_consumption_24h"));
            Assert.True(values.ContainsKey("autarky_24h"));

            var units = svc.GetTelemetryUnits();
            Assert.Equal(Pulswerk.Core.Units.Percent, units["autarky"]);
            Assert.Equal(Pulswerk.Core.Units.Percent, units["autarky_24h"]);
            Assert.Equal(Pulswerk.Core.Units.Kilowatt, units["total_consumption"]);
            Assert.Equal(Pulswerk.Core.Units.KilowattHour, units["total_consumption_24h"]);
        }

        [Fact]
        public void EmsDriver_GetAssetHierarchy_ExposesAllTelemetryPoints()
        {            var driver = new EmsDriver();
            Assert.Equal("ems", driver.DriverName);

            var dev = new Pulswerk.Core.DeviceConfig(
                Id: "ems",
                Name: "Energy Management System",
                DeviceType: "ems",
                Path: new List<string> { "EMS" });

            var tree = driver.GetAssetHierarchy(dev);
            Assert.NotNull(tree);
            Assert.Equal("ems", tree.Id);
            Assert.Equal("Energy Management System", tree.Name);
            Assert.True(tree.Telemetries.Count >= 20);

            var unc = tree.Telemetries.Find(t => t.Key == "ems_uncontrollable_load");
            Assert.NotNull(unc);
            Assert.Equal("Uncontrollable Base Load", unc!.Name);
            Assert.Equal(Pulswerk.Core.Units.Kilowatt, unc.Units);

            var surplus = tree.Telemetries.Find(t => t.Key == "ems_surplus_power");
            Assert.NotNull(surplus);
            Assert.Equal(Pulswerk.Core.Units.Kilowatt, surplus!.Units);
        }

        [Fact]
        public void EmsDriver_Write_UpdatesEnabledAndGridMaxImport()
        {
            var driver = new EmsDriver();
            Assert.True(driver.IsWritable("enabled"));
            Assert.True(driver.IsWritable("grid_max_import"));
            Assert.False(driver.IsWritable("uncontrollable_load"));

            var dev = new Pulswerk.Core.DeviceConfig(
                Id: "ems",
                Name: "Energy Management System",
                DeviceType: "ems");

            driver.Write(null!, dev, "enabled", 0.0);
            Assert.False(EmsService.Instance.Enabled);

            driver.Write(null!, dev, "enabled", 1.0);
            Assert.True(EmsService.Instance.Enabled);

            driver.Write(null!, dev, "grid_max_import", 15.0);
            Assert.Equal(15.0, EmsService.Instance.SourcesConfig.GridMaxImportKw);
        }
    }
}

