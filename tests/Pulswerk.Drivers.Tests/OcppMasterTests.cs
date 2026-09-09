using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Pulswerk.Core;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    public class OcppMasterTests : IDisposable
    {
        private readonly OcppManagerService _service = OcppManagerService.Instance;

        public OcppMasterTests()
        {
            _service.ClearTransactionsForTest();
        }

        public void Dispose()
        {
            _service.ClearTransactionsForTest();
        }

        [Fact]
        public void OcppMasterDriver_RegisteredInFactory()
        {
            var driver = DeviceDriverFactory.Create("ocpp-master");
            Assert.NotNull(driver);
            Assert.IsType<OcppMasterDriver>(driver);
            Assert.Equal("ocpp-master", driver.DriverName);

            // Hyphen-less alias
            var aliasDriver = DeviceDriverFactory.Create("ocppmaster");
            Assert.NotNull(aliasDriver);
            Assert.IsType<OcppMasterDriver>(aliasDriver);

            // Old types should NOT be supported
            Assert.Throws<NotSupportedException>(() => DeviceDriverFactory.Create("ocpp-server"));
            Assert.Throws<NotSupportedException>(() => DeviceDriverFactory.Create("ocppserver"));
        }

        [Fact]
        public void OcppMasterDriver_KeysAndUnits_AreExpected()
        {
            var driver = new OcppMasterDriver();
            var keys = driver.GetTelemetryKeys().ToList();

            Assert.Contains(TelemetryKeys.PowerKw, keys);
            Assert.Contains(TelemetryKeys.EnergyImportKwh, keys);
            Assert.Contains(TelemetryKeys.ActiveSessions, keys);
            Assert.Contains(TelemetryKeys.ForcePowerKw, keys);
            Assert.Equal(4, keys.Count);

            Assert.True(driver.IsWritable(TelemetryKeys.ForcePowerKw));
            Assert.False(driver.IsWritable(TelemetryKeys.PowerKw));
            Assert.False(driver.IsWritable(TelemetryKeys.ActiveSessions));

            var units = driver.GetTelemetryUnits();
            Assert.Equal(Units.Kilowatt, units[TelemetryKeys.PowerKw]);
            Assert.Equal(Units.KilowattHour, units[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(Units.None, units[TelemetryKeys.ActiveSessions]);
            Assert.Equal(Units.Kilowatt, units[TelemetryKeys.ForcePowerKw]);
        }

        [Fact]
        public void OcppManagerService_ServerTelemetry_AggregatesProperly()
        {
            _service.SetLiveTelemetryForTest("cp1", "power", 4.5);
            _service.SetLiveTelemetryForTest("cp1", "energy_import", 120.5);

            _service.SetLiveTelemetryForTest("cp2", "power", 6.2);
            _service.SetLiveTelemetryForTest("cp2", "energy_import", 85.3);

            _service.RegisterTransactionForTest(101, new ActiveTransactionInfo("cp1", 1, "RFID-1", 1000));
            _service.RegisterTransactionForTest(102, new ActiveTransactionInfo("cp2", 1, "RFID-2", 2000));

            var telemetry = _service.GetServerTelemetry();

            Assert.Equal(10.7, Convert.ToDouble(telemetry[TelemetryKeys.PowerKw]));
            Assert.Equal(205.8, Convert.ToDouble(telemetry[TelemetryKeys.EnergyImportKwh]));
            Assert.Equal(2, Convert.ToInt32(telemetry[TelemetryKeys.ActiveSessions]));
            Assert.Equal(0.0, Convert.ToDouble(telemetry[TelemetryKeys.ForcePowerKw]));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        public void ComputeAllocation_ZeroOrNegative_GivesFullPower(double targetKw)
        {
            var sessions = new List<ActiveTransactionInfo>
            {
                new("cp1", 1, "User1", 100, DateTime.UtcNow.AddMinutes(-10)),
                new("cp2", 1, "User2", 200, DateTime.UtcNow.AddMinutes(-5))
            };

            var allocations = OcppManagerService.ComputeAllocation(targetKw, sessions);

            Assert.Equal(2, allocations.Count);
            foreach (var alloc in allocations)
            {
                Assert.Equal(OcppManagerService.DefaultMaxCurrentAmps, alloc.Amps); // 16A
                Assert.Equal(OcppManagerService.MaxPhases, alloc.Phases);           // 3 phases
            }
        }

        [Fact]
        public void ComputeAllocation_3Phase_FairSharing()
        {
            // 10 kW over 2 sessions -> 5 kW per session (>= 4.14 kW -> 3 phases)
            // 5 kW / 0.69 = 7.2A
            var sessions = new List<ActiveTransactionInfo>
            {
                new("cp1", 1, "User1", 100, DateTime.UtcNow.AddMinutes(-10)),
                new("cp2", 1, "User2", 200, DateTime.UtcNow.AddMinutes(-5))
            };

            var allocations = OcppManagerService.ComputeAllocation(10.0, sessions);

            Assert.Equal(2, allocations.Count);
            foreach (var alloc in allocations)
            {
                Assert.Equal(3, alloc.Phases);
                Assert.Equal(7.2, alloc.Amps);
            }
        }

        [Fact]
        public void ComputeAllocation_1Phase_FairSharing()
        {
            // 5 kW over 2 sessions -> 2.5 kW per session (1.38 <= P < 4.14 -> 1 phase)
            // 2.5 kW / 0.23 = 10.9A
            var sessions = new List<ActiveTransactionInfo>
            {
                new("cp1", 1, "User1", 100, DateTime.UtcNow.AddMinutes(-10)),
                new("cp2", 1, "User2", 200, DateTime.UtcNow.AddMinutes(-5))
            };

            var allocations = OcppManagerService.ComputeAllocation(5.0, sessions);

            Assert.Equal(2, allocations.Count);
            foreach (var alloc in allocations)
            {
                Assert.Equal(1, alloc.Phases);
                Assert.Equal(10.9, alloc.Amps);
            }
        }

        [Fact]
        public void ComputeAllocation_SubMinimum_PrioritizesEarliestSession()
        {
            // 2 kW over 2 sessions -> share 1 kW < 1.38 kW (min 1-phase @ 6A)
            // k = floor(2.0 / 1.38) = 1 session can charge
            // Session 1 is earlier (-10m) -> charges with full 2.0 kW (2.0 / 0.23 = 8.7A, 1 phase)
            // Session 2 is later (-5m) -> paused (0.0A)
            var t0 = DateTime.UtcNow;
            var sessions = new List<ActiveTransactionInfo>
            {
                new("cp1", 1, "User1", 100, t0.AddMinutes(-10)),
                new("cp2", 1, "User2", 200, t0.AddMinutes(-5))
            };

            var allocations = OcppManagerService.ComputeAllocation(2.0, sessions);

            Assert.Equal(2, allocations.Count);

            var alloc1 = allocations.First(a => a.ChargePointId == "cp1");
            Assert.Equal(1, alloc1.Phases);
            Assert.Equal(8.7, alloc1.Amps);

            var alloc2 = allocations.First(a => a.ChargePointId == "cp2");
            Assert.Equal(0.0, alloc2.Amps);
        }

        [Fact]
        public void ComputeAllocation_InsufficientPower_PausesAll()
        {
            // 1.0 kW over 2 sessions: 1.0 < 1.38 kW -> k = 0, both paused
            var sessions = new List<ActiveTransactionInfo>
            {
                new("cp1", 1, "User1", 100, DateTime.UtcNow.AddMinutes(-10)),
                new("cp2", 1, "User2", 200, DateTime.UtcNow.AddMinutes(-5))
            };

            var allocations = OcppManagerService.ComputeAllocation(1.0, sessions);

            Assert.Equal(2, allocations.Count);
            Assert.All(allocations, a => Assert.Equal(0.0, a.Amps));
        }

        [Fact]
        public async Task OcppManagerService_ForcePower_WatchdogExpiresAfter60s()
        {
            await _service.SetForcePowerAsync(8.0);

            var now = DateTime.UtcNow;
            Assert.True(_service.IsForcePowerActive(now));
            Assert.Equal(8.0, _service.GetEffectiveForcePowerKw(now));

            // Advance time past 60s
            var expiredTime = now.AddSeconds(65);
            Assert.False(_service.IsForcePowerActive(expiredTime));
            Assert.Equal(0.0, _service.GetEffectiveForcePowerKw(expiredTime));
        }

        [Fact]
        public void OcppMasterDriver_Write_UpdatesManagerSetPoint()
        {
            var driver = new OcppMasterDriver();
            var dev = new DeviceConfig("ocpp-master", "OCPP Master", "ocpp-master");
            var conn = new ConnectionConfig("ocpp-server", "ocpp-ws");

            driver.Write(conn, dev, TelemetryKeys.ForcePowerKw, 7.5);

            var telemetry = _service.GetServerTelemetry();
            Assert.Equal(7.5, Convert.ToDouble(telemetry[TelemetryKeys.ForcePowerKw]));
        }
    }
}
