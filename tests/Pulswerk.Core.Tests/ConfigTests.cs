using System;
using System.Collections.Generic;
using System.Text.Json;
using Pulswerk.Core;
using Xunit;

namespace Connector.Tests
{
    /// <summary>
    /// Tests for Config.cs record deserialization and defaults.
    /// </summary>
    public class ConfigTests
    {
        // ── InfluxConfig ─────────────────────────────────────────────────────

        [Fact]
        public void InfluxConfig_Defaults_AreCorrect()
        {
            var cfg = new InfluxConfig();

            Assert.Equal("http://localhost:8086", cfg.Url);
            Assert.Equal("connector-token", cfg.Token);
            Assert.Equal("pulswerk", cfg.Org);
            Assert.Equal("telemetries", cfg.Bucket);
        }

        [Fact]
        public void InfluxConfig_CustomValues_Override()
        {
            var cfg = new InfluxConfig(
                Url: "http://influx.prod:8086",
                Token: "prod-token",
                Org: "acme",
                Bucket: "metrics");

            Assert.Equal("http://influx.prod:8086", cfg.Url);
            Assert.Equal("prod-token", cfg.Token);
            Assert.Equal("acme", cfg.Org);
            Assert.Equal("metrics", cfg.Bucket);
        }

        // ── DatabaseConfig ───────────────────────────────────────────────────

        [Fact]
        public void DatabaseConfig_Defaults_AreCorrect()
        {
            var cfg = new DatabaseConfig();

            Assert.Equal(730, cfg.RetentionDays);
            Assert.Equal(700, cfg.CompactionAfterDays);
            Assert.Equal(30, cfg.AlarmRetentionDays);
        }

        [Fact]
        public void DatabaseConfig_CustomValues_Override()
        {
            var cfg = new DatabaseConfig(
                RetentionDays: 365,
                CompactionAfterDays: 350,
                AlarmRetentionDays: 7);

            Assert.Equal(365, cfg.RetentionDays);
            Assert.Equal(350, cfg.CompactionAfterDays);
            Assert.Equal(7, cfg.AlarmRetentionDays);
        }

        // ── Full AppConfig JSON round-trip ───────────────────────────────────

        [Fact]
        public void AppConfig_FullJsonRoundTrip()
        {
            string json = """
            {
              "influxdb": {
                "url": "http://localhost:8086",
                "token": "test-token",
                "org": "test-org",
                "bucket": "test-bucket"
              },
              "database": {
                "retentionDays": 365,
                "compactionAfterDays": 350,
                "alarmRetentionDays": 7
              },
              "polling": {
                "intervalSeconds": 15
              },
              "connections": [
                { "id": "conn1", "type": "modbus-tcp", "host": "10.0.0.1", "port": 502 }
              ],
              "devices": [
                {
                  "name": "Device 1",
                  "deviceType": "janitza",
                  "connectionId": "conn1",
                  "slaveId": 1
                }
              ],
              "server": {
                "port": 5000,
                "auth": {
                  "enabled": true,
                  "trustedProxies": ["10.0.0.1"]
                },
                "rights": {
                  "enabled": true,
                  "allowAssetValueEdit": ["ops"]
                }
              }
            }
            """;

            var opts = new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip };
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, opts);

            Assert.NotNull(cfg);
            Assert.NotNull(cfg!.InfluxDb);
            Assert.Equal("http://localhost:8086", cfg.InfluxDb!.Url);
            Assert.Equal("test-token", cfg.InfluxDb.Token);
            Assert.NotNull(cfg.Database);
            Assert.Equal(365, cfg.Database!.RetentionDays);
            Assert.Equal(350, cfg.Database.CompactionAfterDays);
            Assert.Equal(15, cfg.Polling!.IntervalSeconds);
            Assert.Single(cfg.Connections);
            Assert.Equal("conn1", cfg.Connections[0].Id);
            Assert.Single(cfg.Devices);
            Assert.Equal("Device 1", cfg.Devices[0].Name);

            Assert.NotNull(cfg.Server!.Auth);
            Assert.True(cfg.Server.Auth!.Enabled);
            Assert.Equal("10.0.0.1", cfg.Server.Auth.TrustedProxies![0]);
            Assert.NotNull(cfg.Server.Rights);
            Assert.True(cfg.Server.Rights!.Enabled);
            Assert.Equal("ops", cfg.Server.Rights.AllowAssetValueEdit![0]);
            Assert.Equal(5000, cfg.Server.Port);
        }

        [Fact]
        public void AppConfig_MinimalJson_UsesDefaults()
        {
            string json = """
            {
              "connections": [],
              "devices": []
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);

            Assert.NotNull(cfg);
            Assert.Null(cfg!.InfluxDb);
            Assert.Null(cfg.Database);
            Assert.Null(cfg.Polling);
            Assert.Empty(cfg.Connections);
            Assert.Empty(cfg.Devices);
        }

        // ── DeviceConfig ─────────────────────────────────────────────────────

        [Fact]
        public void DeviceConfig_Writeback_IsBoolNow()
        {
            string json = """
            {
              "connections": [],
              "devices": [
                {
                  "name": "BACnet1",
                  "deviceType": "deziko",
                  "connectionId": "c1",
                  "writeback": true
                }
              ]
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.NotNull(cfg);
            Assert.True(cfg!.Devices[0].Writeback);
        }

        [Fact]
        public void DeviceConfig_WritbackDefault_IsFalse()
        {
            string json = """
            {
              "connections": [],
              "devices": [
                {
                  "name": "BACnet1",
                  "deviceType": "deziko",
                  "connectionId": "c1"
                }
              ]
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.NotNull(cfg);
            Assert.False(cfg!.Devices[0].Writeback);
        }

        [Fact]
        public void DeviceConfig_AssetType_ParsedCorrectly()
        {
            string json = """
            {
              "connections": [],
              "devices": [
                {
                  "name": "Node1",
                  "deviceType": "deziko",
                  "connectionId": "c1",
                  "assetType": "BACnet Node"
                }
              ]
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.Equal("BACnet Node", cfg!.Devices[0].AssetType);
        }

        [Fact]
        public void DeviceConfig_HierarchyEnabled_ViaAssetType()
        {
            string json = """
            {
              "connections": [],
              "devices": [
                {
                  "name": "Node1",
                  "deviceType": "deziko",
                  "connectionId": "c1",
                  "assetType": "BACnet Node"
                }
              ]
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.True(cfg!.Devices[0].HierarchyEnabled);
        }

        [Fact]
        public void DeviceConfig_PollIntervalSeconds_Override()
        {
            string json = """
            {
              "connections": [],
              "devices": [
                {
                  "name": "FastDevice",
                  "deviceType": "janitza",
                  "connectionId": "c1",
                  "slaveId": 1,
                  "pollIntervalSeconds": 5
                }
              ]
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.Equal(5, cfg!.Devices[0].PollIntervalSeconds);
        }

        // ── ConnectionConfig ─────────────────────────────────────────────────

        [Fact]
        public void ConnectionConfig_ParsedCorrectly()
        {
            string json = """
            {
              "connections": [
                { "id": "modbus-1", "type": "modbus-tcp", "host": "192.168.1.100", "port": 502 },
                { "id": "bacnet-1", "type": "bacnet-ip",  "host": "192.168.1.200", "port": 47808 }
              ],
              "devices": []
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.Equal(2, cfg!.Connections.Count);
            Assert.Equal("modbus-tcp", cfg.Connections[0].Type);
            Assert.Equal(502, cfg.Connections[0].Port);
            Assert.Equal("bacnet-ip", cfg.Connections[1].Type);
        }


        // ── MonitoringConfig ─────────────────────────────────────────────────

        [Fact]
        public void MonitoringConfig_DisabledByDefault()
        {
            string json = """
            {
              "connections": [],
              "devices": []
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.Null(cfg!.Server);
        }

        [Fact]
        public void MonitoringConfig_LogBufferSize()
        {
            string json = """
            {
              "connections": [],
              "devices": [],
              "server": {
                "port": 8080,
                "logBufferSize": 10000
              }
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.Equal(10000, cfg!.Server!.LogBufferSize);
        }

        [Fact]
        public void ServerConfig_Auth_NestedCorrectly()
        {
            string json = """
            {
              "server": {
                "auth": {
                  "enabled": true,
                  "trustedProxies": ["1.1.1.1"]
                }
              }
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.NotNull(cfg!.Server!.Auth);
            Assert.True(cfg.Server.Auth!.Enabled);
            Assert.Equal("1.1.1.1", cfg.Server.Auth.TrustedProxies![0]);
        }

        // ── No ThingsBoard config accepted ───────────────────────────────────

        [Fact]
        public void AppConfig_NoThingsBoardProperty()
        {
            // Ensure old "thingsboard" key is silently ignored
            string json = """
            {
              "thingsboard": { "host": "localhost", "httpPort": 9090 },
              "connections": [],
              "devices": []
            }
            """;

            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            Assert.NotNull(cfg);
            // No TbConfig property should exist
            var props = typeof(AppConfig).GetProperties();
            Assert.DoesNotContain(props, p => p.Name == "ThingsBoard");
        }
    }
}
