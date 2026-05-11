using System;
using System.IO;
using Pulswerk.Core;
using Pulswerk.Storage;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Comprehensive tests for the AlarmStore (SQLite-backed alarm state machine).
    /// Each test uses a fresh in-memory database via a unique temp file.
    /// </summary>
    public class AlarmStoreTests : IDisposable
    {
        private readonly AlarmStore _store;
        private readonly string _dbPath;

        public AlarmStoreTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"alarm_test_{Guid.NewGuid():N}.db");
            _store = new AlarmStore(_dbPath);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { File.Delete(_dbPath); } catch { }
        }

        // ── Create / Idempotent Update ────────────────────────────────────────

        [Fact]
        public void CreateOrUpdate_NewAlarm_ReturnsRecord()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "Sensor failure");

            Assert.NotNull(alarm);
            Assert.Equal("Fault", alarm.Type);
            Assert.Equal("CRITICAL", alarm.Severity);
            Assert.Equal("ACTIVE_UNACK", alarm.Status);
            Assert.Equal("Sensor failure", alarm.Message);
            Assert.Equal("dev1", alarm.Originator);
            Assert.Equal("DEVICE", alarm.OriginType);
            Assert.True(alarm.CreatedAt > 0);
            Assert.True(alarm.UpdatedAt > 0);
            Assert.Null(alarm.ClearedAt);
        }

        [Fact]
        public void CreateOrUpdate_Idempotent_UpdatesExisting()
        {
            var first = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "MAJOR", "First message");
            var second = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "Updated message");

            Assert.Equal(first.Id, second.Id);
            Assert.Equal("CRITICAL", second.Severity);
            Assert.Equal("Updated message", second.Message);
            Assert.True(second.UpdatedAt >= first.UpdatedAt);
        }

        [Fact]
        public void CreateOrUpdate_DifferentOriginators_CreatesSeparateAlarms()
        {
            var a1 = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            var a2 = _store.CreateOrUpdate("dev2", "DEVICE", "Fault", "CRITICAL", "msg2");

            Assert.NotEqual(a1.Id, a2.Id);
            Assert.Equal(2, _store.CountActive());
        }

        [Fact]
        public void CreateOrUpdate_DifferentTypes_CreatesSeparateAlarms()
        {
            var a1 = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            var a2 = _store.CreateOrUpdate("dev1", "DEVICE", "Communication Loss", "MAJOR", "msg2");

            Assert.NotEqual(a1.Id, a2.Id);
            Assert.Equal(2, _store.CountActive());
        }

        [Fact]
        public void CreateOrUpdate_WithDetails_PersistsDetails()
        {
            var details = new System.Collections.Generic.Dictionary<string, object>
            {
                { "object", "AI:73" },
                { "path", "Floor1/Zone2" },
                { "value", 42.5 }
            };

            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "HighLimit", "WARNING", "Too hot", details);

            Assert.NotNull(alarm.Details);
            Assert.Contains("AI:73", alarm.Details);
            Assert.Contains("Floor1/Zone2", alarm.Details);
        }

        [Fact]
        public void CreateOrUpdate_WithBacnetAckKey_StoresKey()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg",
                bacnetAckKey: "bacnet_ack_10_AI73");

            Assert.Equal("bacnet_ack_10_AI73", alarm.BacnetAckKey);
        }

        // ── State Transitions ────────────────────────────────────────────────

        [Fact]
        public void Acknowledge_ActiveUnack_TransitionsToActiveAck()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            bool result = _store.Acknowledge(alarm.Id, "Acknowledged by operator");

            Assert.True(result);
            var updated = _store.GetById(alarm.Id);
            Assert.Equal("ACTIVE_ACK", updated!.Status);
            Assert.Equal("Acknowledged by operator", updated.AckComment);
        }

        [Fact]
        public void Acknowledge_AlreadyAcked_ReturnsFalse()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Acknowledge(alarm.Id);

            bool result = _store.Acknowledge(alarm.Id);

            Assert.False(result);
        }

        [Fact]
        public void Acknowledge_ClearedAlarm_ReturnsFalse()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Clear(alarm.Id);

            bool result = _store.Acknowledge(alarm.Id);

            Assert.False(result);
        }

        [Fact]
        public void Clear_ById_TransitionsToCleared()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            bool result = _store.Clear(alarm.Id);

            Assert.True(result);
            var updated = _store.GetById(alarm.Id);
            Assert.Equal("CLEARED", updated!.Status);
            Assert.NotNull(updated.ClearedAt);
        }

        [Fact]
        public void Clear_AckedAlarm_TransitionsToCleared()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Acknowledge(alarm.Id);

            bool result = _store.Clear(alarm.Id);

            Assert.True(result);
            var updated = _store.GetById(alarm.Id);
            Assert.Equal("CLEARED", updated!.Status);
        }

        [Fact]
        public void Clear_AlreadyCleared_ReturnsFalse()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Clear(alarm.Id);

            bool result = _store.Clear(alarm.Id);

            Assert.False(result);
        }

        [Fact]
        public void ClearByOriginAndType_MatchingAlarm_ClearsIt()
        {
            _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            bool result = _store.ClearByOriginAndType("dev1", "Fault");

            Assert.True(result);
            Assert.Equal(0, _store.CountActive());
        }

        [Fact]
        public void ClearByOriginAndType_NoMatch_ReturnsFalse()
        {
            _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            bool result = _store.ClearByOriginAndType("dev2", "Fault");

            Assert.False(result);
            Assert.Equal(1, _store.CountActive());
        }

        [Fact]
        public void ClearByOriginAndType_WithAssetOriginType()
        {
            _store.CreateOrUpdate("dev1 / Zone A", "ASSET", "HighLimit", "WARNING", "Too hot");

            bool result = _store.ClearByOriginAndType("dev1 / Zone A", "HighLimit", "ASSET");

            Assert.True(result);
            Assert.Equal(0, _store.CountActive());
        }

        // ── Queries ──────────────────────────────────────────────────────────

        [Fact]
        public void GetAllActive_ReturnsOnlyActiveAlarms()
        {
            var a1 = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            _store.CreateOrUpdate("dev2", "DEVICE", "Warning", "MINOR", "msg2");
            _store.Clear(a1.Id);

            var active = _store.GetAllActive();

            Assert.Single(active);
            Assert.Equal("Warning", active[0].Type);
        }

        [Fact]
        public void GetAllActive_IncludesAckedAlarms()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Acknowledge(alarm.Id);

            var active = _store.GetAllActive();

            Assert.Single(active);
            Assert.Equal("ACTIVE_ACK", active[0].Status);
        }

        [Fact]
        public void GetActiveForOriginator_FiltersCorrectly()
        {
            _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            _store.CreateOrUpdate("dev2", "DEVICE", "Fault", "CRITICAL", "msg2");
            _store.CreateOrUpdate("dev1", "DEVICE", "HighLimit", "WARNING", "msg3");

            var dev1Alarms = _store.GetActiveForOriginator("dev1");

            Assert.Equal(2, dev1Alarms.Count);
        }

        [Fact]
        public void GetById_ExistingAlarm_ReturnsRecord()
        {
            var created = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            var fetched = _store.GetById(created.Id);

            Assert.NotNull(fetched);
            Assert.Equal(created.Id, fetched.Id);
        }

        [Fact]
        public void GetById_NonExistent_ReturnsNull()
        {
            var result = _store.GetById("nonexistent");

            Assert.Null(result);
        }

        [Fact]
        public void CountActive_CorrectCount()
        {
            Assert.Equal(0, _store.CountActive());

            _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            Assert.Equal(1, _store.CountActive());

            var a2 = _store.CreateOrUpdate("dev2", "DEVICE", "Fault", "CRITICAL", "msg2");
            Assert.Equal(2, _store.CountActive());

            _store.Clear(a2.Id);
            Assert.Equal(1, _store.CountActive());
        }

        // ── Retention / Purge ────────────────────────────────────────────────

        [Fact]
        public void PurgeCleared_RemovesOldClearedAlarms()
        {
            var alarm = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");
            _store.Clear(alarm.Id);

            // Purging with -1 days = cutoff is in the future, so even just-cleared alarms get purged
            int purged = _store.PurgeCleared(-1);

            Assert.Equal(1, purged);
            Assert.Null(_store.GetById(alarm.Id));
        }

        [Fact]
        public void PurgeCleared_DoesNotRemoveActiveAlarms()
        {
            _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg");

            int purged = _store.PurgeCleared(-1);

            Assert.Equal(0, purged);
            Assert.Equal(1, _store.CountActive());
        }

        // ── Re-activation after clear ────────────────────────────────────────

        [Fact]
        public void CreateOrUpdate_AfterClear_CreatesNewAlarm()
        {
            var first = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg1");
            _store.Clear(first.Id);

            var second = _store.CreateOrUpdate("dev1", "DEVICE", "Fault", "CRITICAL", "msg2");

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal("ACTIVE_UNACK", second.Status);
            Assert.Equal(1, _store.CountActive());
        }

        // ── Concurrency ──────────────────────────────────────────────────────

        [Fact]
        public async System.Threading.Tasks.Task ConcurrentCreates_NoConflicts()
        {
            var tasks = new System.Threading.Tasks.Task[50];
            for (int i = 0; i < 50; i++)
            {
                int idx = i;
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                    _store.CreateOrUpdate($"dev{idx}", "DEVICE", "Fault", "CRITICAL", $"msg{idx}"));
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);

            Assert.Equal(50, _store.CountActive());
        }
    }
}
