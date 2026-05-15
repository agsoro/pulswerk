using System;
using System.Collections.Generic;
using Pulswerk.Core;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Tests for monitoring DTOs to ensure serialization correctness and
    /// that TB-specific types have been removed.
    /// </summary>
    public class ServerDtoTests
    {
        // ── DeviceStatusDto ──────────────────────────────────────────────────

        [Fact]
        public void DeviceStatusDto_DefaultValues()
        {
            var dto = new DeviceStatusDto();

            Assert.Equal(0, dto.TotalDevices);
            Assert.Equal(0, dto.OnlineDevices);
            Assert.Equal(0, dto.OfflineDevices);
            Assert.Equal(0, dto.ActiveAlarms);
            Assert.Equal("", dto.ConnectorVersion);
            Assert.Equal(0, dto.UptimeSeconds);
        }

        [Fact]
        public void DeviceStatusDto_AllPropertiesSettable()
        {
            var dto = new DeviceStatusDto
            {
                TotalDevices = 10,
                OnlineDevices = 8,
                OfflineDevices = 2,
                ActiveAlarms = 3,
                ConnectorVersion = "1.2.3",
                UptimeSeconds = 3600,
                LogBufferSize = 100,
                LogBufferCapacity = 5000,
                Timestamp = "2026-01-01T00:00:00Z"
            };

            Assert.Equal(10, dto.TotalDevices);
            Assert.Equal(8, dto.OnlineDevices);
            Assert.Equal(2, dto.OfflineDevices);
            Assert.Equal(3, dto.ActiveAlarms);
            Assert.Equal("1.2.3", dto.ConnectorVersion);
        }

        // ── DeviceDto ────────────────────────────────────────────────────────

        [Fact]
        public void DeviceDto_NoLegacyTbProperties()
        {
            // Ensure assetCount, entityViewCount, deviceCount are removed
            var props = typeof(DeviceDto).GetProperties();
            var propNames = new List<string>();
            foreach (var p in props) propNames.Add(p.Name);

            Assert.DoesNotContain("AssetCount", propNames);
            Assert.DoesNotContain("EntityViewCount", propNames);
            Assert.DoesNotContain("DeviceCount", propNames);
        }

        [Fact]
        public void DeviceDto_HasRequiredProperties()
        {
            var dto = new DeviceDto
            {
                Name = "Test Device",
                Type = "bacnet",
                Status = "online",
                StatusColor = "#10b981",
                LastSeen = "2026-01-01",
                Connection = "192.168.1.1",
                Port = 47808,
                ConnectionId = "conn-1"
            };

            Assert.Equal("Test Device", dto.Name);
            Assert.Equal(47808, dto.Port);
        }

        // ── AlarmDisplayDto ──────────────────────────────────────────────────

        [Fact]
        public void AlarmDisplayDto_HasAlarmId()
        {
            var dto = new AlarmDisplayDto
            {
                AlarmId = "abc123",
                Type = "Fault",
                Severity = "CRITICAL",
                Status = "ACTIVE_UNACK",
                Message = "Test",
                Originator = "dev1",
                Time = "2026-01-01T00:00:00Z"
            };

            Assert.Equal("abc123", dto.AlarmId);
            Assert.Equal("Fault", dto.Type);
        }

        [Fact]
        public void AlarmDisplayDto_HasBacnetAckKey()
        {
            var dto = new AlarmDisplayDto
            {
                BacnetAckKey = "bacnet_ack_10_AI73"
            };

            Assert.Equal("bacnet_ack_10_AI73", dto.BacnetAckKey);
        }

        [Fact]
        public void AlarmDisplayDto_AckCommentNullable()
        {
            var dto = new AlarmDisplayDto();

            Assert.Null(dto.AckComment);
        }

        // ── AlarmDto removed ─────────────────────────────────────────────────

        [Fact]
        public void AlarmDto_TypeDoesNotExist()
        {
            // The old ThingsBoard-specific AlarmDto should be removed
            var type = typeof(DeviceStatusDto).Assembly.GetType("Connector.AlarmDto");

            Assert.Null(type);
        }

        // ── AssetNodeDto ─────────────────────────────────────────────────────

        [Fact]
        public void AssetNodeDto_DefaultCollections()
        {
            var dto = new AssetNodeDto();

            Assert.NotNull(dto.Children);
            Assert.Empty(dto.Children);
            Assert.NotNull(dto.Points);
            Assert.Empty(dto.Points);
        }

        // ── AssetPointDto ────────────────────────────────────────────────────

        [Fact]
        public void AssetPointDto_EnumValuesNullable()
        {
            var dto = new AssetPointDto();

            Assert.Null(dto.EnumValues);
        }

        [Fact]
        public void AssetPointDto_ParentPathDefault()
        {
            var dto = new AssetPointDto();

            Assert.NotNull(dto.ParentPath);
            Assert.Empty(dto.ParentPath);
        }

        // ── AvailableKeyDto ──────────────────────────────────────────────────

        [Fact]
        public void AvailableKeyDto_AllFieldsSettable()
        {
            var dto = new AvailableKeyDto
            {
                Key = "dev10_ai_1_value",
                Name = "AI 1",
                FullName = "Device 10 AI 1",
                Units = "°C",
                Type = "ANALOG_INPUT",
                Path = "Building > Floor 1",
                Value = "21.5",
                ParentId = "node_123",
                IsWritable = true,
                EnumValues = new List<string> { "Off", "On" }
            };

            Assert.Equal("dev10_ai_1_value", dto.Key);
            Assert.True(dto.IsWritable);
            Assert.Equal(2, dto.EnumValues!.Count);
        }

        // ── PropertyDto ──────────────────────────────────────────────────────

        [Fact]
        public void PropertyDto_DefaultValues()
        {
            var dto = new PropertyDto();

            Assert.Equal("", dto.Name);
            Assert.Equal("", dto.Value);
            Assert.Equal("", dto.Units);
        }

        // ── LogEntryDto ──────────────────────────────────────────────────────

        [Fact]
        public void LogEntryDto_AllFieldsSettable()
        {
            var dto = new LogEntryDto
            {
                Timestamp = "2026-01-01 12:00:00.000",
                Severity = "info",
                Message = "Test log",
                Source = "Program"
            };

            Assert.Equal("info", dto.Severity);
            Assert.Equal("Test log", dto.Message);
        }
    }
}
