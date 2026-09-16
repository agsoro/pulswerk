using System;
using System.Collections.Generic;
using System.IO;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Storage;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Integration-style tests that verify the AlarmStore handles complex
    /// real-world scenarios correctly (alarm lifecycle, multi-device, edge cases).
    /// </summary>
    public class AlarmIntegrationTests : IDisposable
    {
        private readonly AlarmStore _store;
        private readonly string _dbPath;

        public AlarmIntegrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"alarm_integ_{Guid.NewGuid():N}.db");
            _store = new AlarmStore(_dbPath);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { File.Delete(_dbPath); } catch { }
        }

        // ── Full lifecycle ───────────────────────────────────────────────────

        [Fact]
        public void FullLifecycle_Create_Ack_Clear()
        {
            // 1. Create
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "Sensor failure");
            Assert.Equal("ACTIVE_UNACK", alarm.Status);
            Assert.Equal(1, _store.CountActive());

            // 2. Acknowledge
            _store.Acknowledge(alarm.Id, "Operator reviewed");
            var acked = _store.GetById(alarm.Id);
            Assert.Equal("ACTIVE_ACK", acked!.Status);
            Assert.Equal("Operator reviewed", acked.AckComment);
            Assert.Equal(1, _store.CountActive());

            // 3. Clear
            _store.Clear(alarm.Id);
            var cleared = _store.GetById(alarm.Id);
            Assert.Equal("CLEARED", cleared!.Status);
            Assert.NotNull(cleared.ClearedAt);
            Assert.Equal(0, _store.CountActive());
        }

        // ── Multi-device concurrent alarms ───────────────────────────────────

        [Fact]
        public void MultiDevice_IndependentAlarmLifecycles()
        {
            // Device 1: Fault → Ack → Clear
            var dev1Fault = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "Dev1 fault");
            _store.Acknowledge(dev1Fault.Id);
            _store.Clear(dev1Fault.Id);

            // Device 2: Fault (still active)
            var dev2Fault = _store.CreateOrUpdate("dev2", "DEVICE", "Fault", "CRITICAL", "Dev2 fault");

            // Device 3: HighLimit + LowLimit (both active)
            _store.CreateOrUpdate("dev3", "ASSET", "HighLimit", "WARNING", "High");
            _store.CreateOrUpdate("dev3", "ASSET", "LowLimit", "MINOR", "Low");

            Assert.Equal(3, _store.CountActive());
            Assert.Empty(_store.GetActiveForOriginator("dev1"));
            Assert.Single(_store.GetActiveForOriginator("dev2"));
            Assert.Equal(2, _store.GetActiveForOriginator("dev3", "ASSET").Count);
        }

        // ── Communication Loss pattern ───────────────────────────────────────

        [Fact]
        public void CommunicationLoss_RaiseAndClearOnReconnect()
        {
            // Device goes offline
            _store.CreateOrUpdate("ModbusDevice1", "DEVICE",
                "Communication Loss", "CRITICAL",
                "Device ModbusDevice1 is not responding: Connection refused");

            Assert.Equal(1, _store.CountActive());

            // Device comes back online
            _store.ClearByOriginAndType("ModbusDevice1", "Communication Loss");

            Assert.Equal(0, _store.CountActive());
        }

        [Fact]
        public void CommunicationLoss_IdempotentCreate()
        {
            // Multiple timeouts should not create duplicate alarms
            var a1 = _store.CreateOrUpdate("dev1", "DEVICE", "Communication Loss", "CRITICAL", "timeout 1");
            var a2 = _store.CreateOrUpdate("dev1", "DEVICE", "Communication Loss", "CRITICAL", "timeout 2");
            var a3 = _store.CreateOrUpdate("dev1", "DEVICE", "Communication Loss", "CRITICAL", "timeout 3");

            Assert.Equal(a1.Id, a2.Id);
            Assert.Equal(a2.Id, a3.Id);
            Assert.Equal(1, _store.CountActive());
            Assert.Equal("timeout 3", _store.GetById(a3.Id)!.Message);
        }

        // ── BACnet alarm patterns ────────────────────────────────────────────

        [Fact]
        public void BacnetAlarm_FaultStatusFlags()
        {
            var details = new Dictionary<string, object>
            {
                { "object", "AI:73" },
                { "name", "Supply Air Temp" },
                { "path", "ASP01 / AHU 1 / Supply Air Temp" },
                { "status_flags", "InAlarm, Fault" },
                { "reliability", "SensorFailure" },
                { "bacnetAckKey", "bacnet_ack_10_AI73" }
            };

            var alarm = _store.CreateOrUpdate(
                "ASP01 / AHU 1 / Supply Air Temp", "ASSET",
                "StatusFlags Fault [AI:73]", "CRITICAL",
                "FAULT: Supply Air Temp on ASP01. StatusFlags: InAlarm, Fault.",
                details, "bacnet_ack_10_AI73");

            Assert.Equal("CRITICAL", alarm.Severity);
            Assert.Contains("AI:73", alarm.Details!);
            Assert.Equal("bacnet_ack_10_AI73", alarm.BacnetAckKey);
        }

        [Fact]
        public void BacnetAlarm_InAlarmStatusFlags_ClearOnNormal()
        {
            // Object goes into alarm
            _store.CreateOrUpdate(
                "ASP01 / Zone A / Temp Sensor", "ASSET",
                "StatusFlags InAlarm [AI:42]", "WARNING",
                "ALARM: Temp Sensor. InAlarm.");

            Assert.Equal(1, _store.CountActive());

            // Object returns to normal
            _store.ClearByOriginAndType(
                "ASP01 / Zone A / Temp Sensor",
                "StatusFlags InAlarm [AI:42]",
                "ASSET");

            Assert.Equal(0, _store.CountActive());
        }

        // ── Severity escalation ──────────────────────────────────────────────

        [Fact]
        public void SeverityEscalation_UpdatesExisting()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "WARNING", "Initial");
            Assert.Equal("WARNING", alarm.Severity);

            // Condition worsens
            var updated = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "Escalated");

            Assert.Equal(alarm.Id, updated.Id);
            Assert.Equal("CRITICAL", updated.Severity);
            Assert.Equal("Escalated", updated.Message);
        }

        // ── Purge edge cases ─────────────────────────────────────────────────

        [Fact]
        public void Purge_OnlyClearedAlarmsRemoved()
        {
            var a1 = _store.CreateOrUpdate("dev1", "DEVICE", "Fault1", "CRITICAL", "msg1");
            var a2 = _store.CreateOrUpdate("dev2", "DEVICE", "Fault2", "CRITICAL", "msg2");
            var a3 = _store.CreateOrUpdate("dev3", "DEVICE", "Fault3", "CRITICAL", "msg3");

            // Clear only a2
            _store.Clear(a2.Id);

            // Use -1 days = cutoff is in the future, so even just-cleared alarms get purged
            int purged = _store.PurgeCleared(-1);

            Assert.Equal(1, purged);
            Assert.Equal(2, _store.CountActive());
            Assert.NotNull(_store.GetById(a1.Id));
            Assert.Null(_store.GetById(a2.Id));
            Assert.NotNull(_store.GetById(a3.Id));
        }

        // ── Large scale ──────────────────────────────────────────────────────

        [Fact]
        public void LargeScale_100Alarms_PerformanceOk()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
            {
                _store.CreateOrUpdate($"dev{i}", "DEVICE", "Fault", "CRITICAL", $"msg{i}");
            }

            sw.Stop();

            Assert.Equal(100, _store.CountActive());
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Creating 100 alarms took {sw.ElapsedMilliseconds}ms");

            // Clear all
            var active = _store.GetAllActive();
            foreach (var a in active) _store.Clear(a.Id);

            Assert.Equal(0, _store.CountActive());
        }

        // ── Origin type specifics ────────────────────────────────────────────

        [Fact]
        public void OriginType_SameNameDifferentTypes_DedupsByOriginatorAndType()
        {
            // CreateOrUpdate deduplicates on (originator, type) — origin_type is not part of the
            // dedup key. This is by design: a single originator shouldn't have duplicate alarm types.
            var first = _store.CreateOrUpdate("Zone A", "DEVICE", "Fault", "CRITICAL", "device alarm");
            var second = _store.CreateOrUpdate("Zone A", "ASSET", "Fault", "CRITICAL", "asset alarm");

            // Second call updates the first (same originator + type)
            Assert.Equal(first.Id, second.Id);
            Assert.Equal(1, _store.CountActive());
        }

        [Fact]
        public void OriginType_DifferentNames_IndependentAlarms()
        {
            // Different originators are always independent
            _store.CreateOrUpdate("dev1 / Zone A", "ASSET", "Fault", "CRITICAL", "zone A alarm");
            _store.CreateOrUpdate("dev1 / Zone B", "ASSET", "Fault", "CRITICAL", "zone B alarm");

            Assert.Equal(2, _store.CountActive());
        }

        [Fact]
        public void ClearByOriginAndType_SpecificOriginType_Filters()
        {
            _store.CreateOrUpdate("dev1 / Zone A", "ASSET", "HighLimit", "WARNING", "Too hot");
            _store.CreateOrUpdate("dev1 / Zone B", "ASSET", "HighLimit", "WARNING", "Also hot");

            // Clear only Zone A
            _store.ClearByOriginAndType("dev1 / Zone A", "HighLimit", "ASSET");

            Assert.Equal(1, _store.CountActive());
            Assert.Empty(_store.GetActiveForOriginator("dev1 / Zone A", "ASSET"));
            Assert.Single(_store.GetActiveForOriginator("dev1 / Zone B", "ASSET"));
        }
    }
}
