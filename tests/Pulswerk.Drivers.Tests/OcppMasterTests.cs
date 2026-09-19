using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Pulswerk.Core;
using Pulswerk.Drivers.Ocpp;

namespace Pulswerk.Drivers.Tests
{
    [Collection("OcppTests")]
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
            Assert.Equal(3, keys.Count);

            Assert.False(driver.IsWritable(TelemetryKeys.ForcePowerKw));
            Assert.False(driver.IsWritable(TelemetryKeys.PowerKw));
            Assert.False(driver.IsWritable(TelemetryKeys.ActiveSessions));

            var units = driver.GetTelemetryUnits();
            Assert.Equal(Units.Kilowatt, units[TelemetryKeys.PowerKw]);
            Assert.Equal(Units.KilowattHour, units[TelemetryKeys.EnergyImportKwh]);
            Assert.Equal(Units.None, units[TelemetryKeys.ActiveSessions]);
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
        }
    }
}
