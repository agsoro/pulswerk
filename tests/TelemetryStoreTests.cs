using System;
using System.Collections.Generic;
using Connector;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Tests for the TelemetryStore's point-building logic (unit-testable without InfluxDB).
    /// InfluxDB integration tests would require a running instance and are tagged as such.
    /// </summary>
    public class TelemetryStorePointTests
    {
        // We can't easily unit-test the full TelemetryStore without InfluxDB,
        // but we can test the Insert/InsertBatch parameter validation and
        // the TsPoint record itself.

        [Fact]
        public void TsPoint_NumericValue_PropertiesCorrect()
        {
            var point = new TsPoint(1000, 42.5, null);

            Assert.Equal(1000, point.Ts);
            Assert.Equal(42.5, point.Value);
            Assert.Null(point.ValueStr);
        }

        [Fact]
        public void TsPoint_StringValue_PropertiesCorrect()
        {
            var point = new TsPoint(2000, null, "Active");

            Assert.Equal(2000, point.Ts);
            Assert.Null(point.Value);
            Assert.Equal("Active", point.ValueStr);
        }

        [Fact]
        public void TsPoint_RecordEquality()
        {
            var a = new TsPoint(1000, 42.5, null);
            var b = new TsPoint(1000, 42.5, null);

            Assert.Equal(a, b);
        }

        [Fact]
        public void TsPoint_RecordInequality()
        {
            var a = new TsPoint(1000, 42.5, null);
            var b = new TsPoint(1001, 42.5, null);

            Assert.NotEqual(a, b);
        }
    }

    /// <summary>
    /// Tests for the AlarmRecord DTO.
    /// </summary>
    public class AlarmRecordTests
    {
        [Fact]
        public void AlarmRecord_AllFieldsPopulated()
        {
            var record = new AlarmRecord(
                Id: "abc123",
                Type: "Fault",
                Severity: "CRITICAL",
                Status: "ACTIVE_UNACK",
                Message: "Sensor failure",
                Originator: "dev1",
                OriginType: "DEVICE",
                OriginKey: "ai_73",
                Details: "{\"object\":\"AI:73\"}",
                CreatedAt: 1000,
                UpdatedAt: 2000,
                ClearedAt: null,
                AckComment: null,
                BacnetAckKey: "key_123");

            Assert.Equal("abc123", record.Id);
            Assert.Equal("Fault", record.Type);
            Assert.Equal("CRITICAL", record.Severity);
            Assert.Equal("ACTIVE_UNACK", record.Status);
            Assert.Null(record.ClearedAt);
            Assert.Equal("key_123", record.BacnetAckKey);
        }

        [Fact]
        public void AlarmRecord_RecordEquality()
        {
            var a = new AlarmRecord("id", "Fault", "CRITICAL", "ACTIVE_UNACK", "msg",
                "dev1", "DEVICE", null, null, 1000, 1000, null, null, null);
            var b = new AlarmRecord("id", "Fault", "CRITICAL", "ACTIVE_UNACK", "msg",
                "dev1", "DEVICE", null, null, 1000, 1000, null, null, null);

            Assert.Equal(a, b);
        }
    }
}
