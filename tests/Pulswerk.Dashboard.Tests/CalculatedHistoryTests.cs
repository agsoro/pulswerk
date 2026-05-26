using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Dashboard;
using Pulswerk.Drivers;
using Pulswerk.Storage;
using Xunit;

namespace Pulswerk.Dashboard.Tests
{
    public class FakeTelemetryStore : TelemetryStore
    {
        public List<(string Key, long Ts, double Val)> InsertedPoints { get; } = new();
        public Func<string, long, long, Task<List<TsPoint>>> QueryAsyncHandler { get; set; }
        public Func<string, string, long, long, Task<List<TsPoint>>> QueryConsumptionAsyncHandler { get; set; }
        public Func<List<string>, long, long, string, bool, Task<List<TsPoint>>> QuerySumAsyncHandler { get; set; }

        public FakeTelemetryStore() : base("http://localhost:9999", "token", "org", "bucket")
        {
        }

        public override void Insert(string key, long tsMs, object value)
        {
            if (value is double d)
            {
                InsertedPoints.Add((key, tsMs, d));
            }
        }

        public override Task<List<TsPoint>> QueryAsync(string key, long startTs, long endTs, int limit = 1000, bool descending = false)
        {
            if (QueryAsyncHandler != null) return QueryAsyncHandler(key, startTs, endTs);
            return Task.FromResult(new List<TsPoint>());
        }

        public override Task<List<TsPoint>> QueryConsumptionAsync(string key, string interval, long startTs, long endTs, int maxPoints = 300)
        {
            if (QueryConsumptionAsyncHandler != null) return QueryConsumptionAsyncHandler(key, interval, startTs, endTs);
            return Task.FromResult(new List<TsPoint>());
        }

        public override Task<List<TsPoint>> QuerySumAsync(List<string> keys, long startTs, long endTs, string? interval = null, bool isConsumption = false, int maxPoints = 300)
        {
            if (QuerySumAsyncHandler != null) return QuerySumAsyncHandler(keys, startTs, endTs, interval, isConsumption);
            return Task.FromResult(new List<TsPoint>());
        }
    }

    public class CalculatedHistoryTests
    {
        [Fact]
        public async Task GetConsumptionHistoryAsync_CalculatesGaps_And_PersistsToInflux()
        {
            // Setup
            var logBuffer = new LogBuffer(100);
            var config = new AppConfig(null, null, null, new(), new(), new());
            using var dataStore = new FakeTelemetryStore();
            using var alarmStore = new AlarmStore(Path.Combine(Path.GetTempPath(), $"test_calc_alarms_{Guid.NewGuid():N}.db"));

            var service = new DashboardDataService(
                logBuffer, config, dataStore, alarmStore,
                new ConcurrentDictionary<string, byte>(),
                new ConcurrentDictionary<string, DateTime>(),
                new Dictionary<string, IDeviceDriver>()
            );

            // Gaps from 10000000 to 20000000 (interval of 1 hour = 3600000ms)
            // Existing points: none
            long startTs = 10000000;
            long endTs = 20000000; // duration is 10M ms (less than 30 days)

            // Mock QueryConsumptionAsync to return calculated values
            dataStore.QueryConsumptionAsyncHandler = (key, interval, start, end) =>
            {
                Assert.Equal("meter1", key);
                Assert.Equal("1h", interval);
                return Task.FromResult(new List<TsPoint>
                {
                    new TsPoint(12000000, 10.5, null),
                    new TsPoint(16000000, 15.2, null)
                });
            };

            // Call
            var result = await service.GetConsumptionHistoryAsync("meter1", "1h", "meter1_hourly", startTs, endTs);

            // Verify
            Assert.Equal(2, result.Count);
            Assert.Equal(10.5, result[0].Value);
            Assert.Equal(15.2, result[1].Value);

            // Verify points were inserted to mock InfluxDB
            Assert.Equal(2, dataStore.InsertedPoints.Count);
            Assert.Equal("meter1_hourly", dataStore.InsertedPoints[0].Key);
            Assert.Equal(12000000, dataStore.InsertedPoints[0].Ts);
            Assert.Equal(10.5, dataStore.InsertedPoints[0].Val);
        }

        [Fact]
        public async Task GetConsumptionHistoryAsync_RealTimePathsum_CalculatesSumWithoutConsumption()
        {
            // Setup
            var logBuffer = new LogBuffer(100);
            var config = new AppConfig(null, null, null, new(), new(), new());
            using var dataStore = new FakeTelemetryStore();
            using var alarmStore = new AlarmStore(Path.Combine(Path.GetTempPath(), $"test_calc_alarms_{Guid.NewGuid():N}.db"));

            var service = new DashboardDataService(
                logBuffer, config, dataStore, alarmStore,
                new ConcurrentDictionary<string, byte>(),
                new ConcurrentDictionary<string, DateTime>(),
                new Dictionary<string, IDeviceDriver>()
            );

            long startTs = 10000000;
            long endTs = 12000000;

            // Mock QuerySumAsync
            dataStore.QuerySumAsyncHandler = (keys, start, end, interval, isConsumption) =>
            {
                Assert.Contains("meter1_power", keys);
                Assert.Contains("meter2_power", keys);
                Assert.Equal("5m", interval);
                Assert.False(isConsumption); // Crucial: must be false for real-time pathsums!
                return Task.FromResult(new List<TsPoint>
                {
                    new TsPoint(11000000, 120.0, null)
                });
            };

            // Call
            var allKeys = new List<AvailableTelemetryDto>
            {
                new AvailableTelemetryDto { Key = "meter1_power", Path = "Building A › meter1_power", Units = "kW" },
                new AvailableTelemetryDto { Key = "meter2_power", Path = "Building A › meter2_power", Units = "kW" }
            };

            var result = await service.GetConsumptionHistoryAsync("pathsum(\"Building A\", \"*_power\")", "5m", "total_power", startTs, endTs, allKeys, isConsumption: false);

            // Verify
            Assert.Single(result);
            Assert.Equal(120.0, result[0].Value);
            Assert.Single(dataStore.InsertedPoints);
            Assert.Equal("total_power", dataStore.InsertedPoints[0].Key);
            Assert.Equal(120.0, dataStore.InsertedPoints[0].Val);
        }

        [Fact]
        public async Task GetConsumptionHistoryAsync_OngoingPeriod_CalculatedButNotPersisted()
        {
            // Setup
            var logBuffer = new LogBuffer(100);
            var config = new AppConfig(null, null, null, new(), new(), new());
            using var dataStore = new FakeTelemetryStore();
            using var alarmStore = new AlarmStore(Path.Combine(Path.GetTempPath(), $"test_calc_alarms_{Guid.NewGuid():N}.db"));

            var service = new DashboardDataService(
                logBuffer, config, dataStore, alarmStore,
                new ConcurrentDictionary<string, byte>(),
                new ConcurrentDictionary<string, DateTime>(),
                new Dictionary<string, IDeviceDriver>()
            );

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long currentHourStart = (nowMs / 3600000) * 3600000;

            // Two periods:
            // 1. Completed period starting at currentHourStart - 3600000 (1 hour ago)
            // 2. Ongoing period starting at currentHourStart (now)
            long startTs = currentHourStart - 3600000;
            long endTs = nowMs;

            dataStore.QueryConsumptionAsyncHandler = (key, interval, start, end) =>
            {
                Assert.Equal("meter1", key);
                Assert.Equal("1h", interval);
                return Task.FromResult(new List<TsPoint>
                {
                    new TsPoint(currentHourStart - 3600000, 15.0, null), // Completed
                    new TsPoint(currentHourStart, 5.0, null)            // Ongoing
                });
            };

            // Call
            var result = await service.GetConsumptionHistoryAsync("meter1", "1h", "meter1_hourly", startTs, endTs, isConsumption: true);

            // Verify both are returned in result
            Assert.Equal(2, result.Count);
            Assert.Equal(currentHourStart - 3600000, result[0].Ts);
            Assert.Equal(15.0, result[0].Value);
            Assert.Equal(currentHourStart, result[1].Ts);
            Assert.Equal(5.0, result[1].Value);

            // Verify only the completed one (timestamp currentHourStart - 3600000) is stored/inserted in InfluxDB
            Assert.Single(dataStore.InsertedPoints);
            Assert.Equal("meter1_hourly", dataStore.InsertedPoints[0].Key);
            Assert.Equal(currentHourStart - 3600000, dataStore.InsertedPoints[0].Ts);
            Assert.Equal(15.0, dataStore.InsertedPoints[0].Val);
        }
    }
}
