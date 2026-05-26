using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using Pulswerk.Drivers;
using Pulswerk.Storage;
using Xunit;

namespace Pulswerk.Dashboard.Tests
{
    public class DashboardPerformanceTests
    {
        [Fact]
        public void GetAvailableTelemetries_Caching_Works_And_Is_Fast()
        {
            var logBuffer = new LogBuffer(100);
            var config = new AppConfig(
                null, null, null,
                new List<ConnectionConfig>(),
                new List<DeviceConfig>
                {
                    new DeviceConfig(
                        "vdev",
                        "VirtualDevice",
                        "virtual",
                        null,
                        null,
                        null,
                        null,
                        2000,
                        null,
                        null,
                        null,
                        null,
                        null,
                        false,
                        null,
                        new List<TelemetryConfig>
                        {
                            new TelemetryConfig("calc1", "Calc 1", "pathsum('Path', 'Unit')")
                        }
                    )
                },
                new ServerConfig()
            );

            // Create a TelemetryStore with dummy Influx settings (fails gracefully in background task)
            using var dataStore = new TelemetryStore("http://localhost:9999", "dummy-token", "dummy-org", "dummy-bucket");
            var alarmDbPath = Path.Combine(Path.GetTempPath(), $"perf_alarms_{Guid.NewGuid():N}.db");
            var alarmStore = new AlarmStore(alarmDbPath);

            try
            {
                var offlineDevices = new ConcurrentDictionary<string, byte>();
                var lastPolledAtMap = new ConcurrentDictionary<string, DateTime>();
                var drivers = new Dictionary<string, IDeviceDriver>();

                var service = new DashboardDataService(logBuffer, config, dataStore, alarmStore, offlineDevices, lastPolledAtMap, drivers);

                // Measure first call (hits the tree generation code)
                var sw = Stopwatch.StartNew();
                var list1 = service.GetAvailableTelemetries(true);
                sw.Stop();
                var time1 = sw.ElapsedMilliseconds;

                // Measure second call (should hit the cache and be extremely fast)
                sw.Restart();
                var list2 = service.GetAvailableTelemetries(true);
                sw.Stop();
                var time2 = sw.ElapsedMilliseconds;

                // Verify they have the same counts and items
                Assert.Single(list1);
                Assert.Single(list2);
                Assert.Equal(list1[0].Key, list2[0].Key);

                // Cached execution should be fast
                Assert.True(time2 <= time1, $"Cached execution ({time2}ms) was not faster than first execution ({time1}ms)");
            }
            finally
            {
                // Clean up alarm db
                alarmStore.Dispose();
                try { File.Delete(alarmDbPath); } catch { }
            }
        }
    }
}
