using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Dashboard.Services;
using Xunit;

namespace Pulswerk.Dashboard.Tests
{
    public class NetworkScanServiceTests
    {
        // ── Range parsing ──────────────────────────────────────────────────

        [Theory]
        [InlineData("192.168.1.0/24", 256)]   // includes network + broadcast
        [InlineData("10.0.0.0/30", 4)]
        [InlineData("10.0.0.0/31", 2)]
        [InlineData("10.0.0.0/32", 1)]
        public void ParseRange_Cidr_ExpandsCorrectly(string range, int expectedCount)
        {
            var ips = NetworkScanService.ParseRange(range);
            Assert.Equal(expectedCount, ips.Count);
        }

        [Fact]
        public void ParseRange_Cidr_IncludesNetworkAndBroadcast()
        {
            var ips = NetworkScanService.ParseRange("192.168.1.0/24");
            Assert.Contains("192.168.1.0", ips);
            Assert.Contains("192.168.1.255", ips);
            Assert.Contains("192.168.1.1", ips);
            Assert.Contains("192.168.1.254", ips);
        }

        [Fact]
        public void ParseRange_LastOctetRange_ExpandsCorrectly()
        {
            var ips = NetworkScanService.ParseRange("192.168.1.10-12");
            Assert.Equal(3, ips.Count);
            Assert.Contains("192.168.1.10", ips);
            Assert.Contains("192.168.1.11", ips);
            Assert.Contains("192.168.1.12", ips);
        }

        [Fact]
        public void ParseRange_LastOctetRange_ReversedOrderWorks()
        {
            var ips = NetworkScanService.ParseRange("192.168.1.12-10");
            Assert.Equal(3, ips.Count);
            Assert.Contains("192.168.1.10", ips);
        }

        [Fact]
        public void ParseRange_ExplicitStartEnd_ExpandsCorrectly()
        {
            var ips = NetworkScanService.ParseRange("10.0.0.1-10.0.0.3");
            Assert.Equal(3, ips.Count);
            Assert.Equal("10.0.0.1", ips[0]);
            Assert.Equal("10.0.0.3", ips[2]);
        }

        [Fact]
        public void ParseRange_SingleHost_ReturnsOne()
        {
            var ips = NetworkScanService.ParseRange("192.168.1.50");
            Assert.Single(ips);
            Assert.Equal("192.168.1.50", ips[0]);
        }

        [Fact]
        public void ParseRange_Empty_Throws()
        {
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange(""));
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange("   "));
        }

        [Fact]
        public void ParseRange_InvalidCidr_Throws()
        {
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange("192.168.1.0/33"));
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange("192.168.1.0/abc"));
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange("not-an-ip/24"));
        }

        [Fact]
        public void ParseRange_TooLarge_Throws()
        {
            // /20 expands to 4094 addresses, over the 1024 cap
            Assert.Throws<ArgumentException>(() => NetworkScanService.ParseRange("10.0.0.0/20"));
        }

        // ── Default probes ────────────────────────────────────────────────

        [Fact]
        public void DefaultProbes_ContainsKnownProtocols()
        {
            var types = NetworkScanService.DefaultProbes.Select(p => p.Type).ToList();
            Assert.Contains("modbus-tcp", types);
            Assert.Contains("bacnet-ip", types);
            Assert.Contains("knx-ip", types);
            Assert.Contains("ocpp-ws", types);
        }

        [Fact]
        public void DefaultProbes_HasCorrectPorts()
        {
            var modbus = NetworkScanService.DefaultProbes.First(p => p.Type == "modbus-tcp");
            Assert.Equal(502, modbus.Port);

            var bacnet = NetworkScanService.DefaultProbes.First(p => p.Type == "bacnet-ip");
            Assert.Equal(47808, bacnet.Port);

            var knx = NetworkScanService.DefaultProbes.First(p => p.Type == "knx-ip");
            Assert.Equal(3671, knx.Port);
        }

        // ── Scan against localhost ─────────────────────────────────────────

        [Fact]
        public async Task ScanAsync_SingleHostNoOpenPorts_ReturnsEmpty()
        {
            // 127.0.0.1 is up but unlikely to have modbus/bacnet/knx/ocpp ports open in CI.
            // Use a high, almost-certainly-closed port to guarantee no false positives.
            var probes = new[] { new ProtocolProbe("test", "Test", 59999) };
            var result = await NetworkScanService.ScanAsync("127.0.0.1", probes);
            Assert.Equal(0, result.HostsFound);
            Assert.Empty(result.Hosts);
        }

        [Fact]
        public async Task ScanAsync_DetectsOpenPort()
        {
            // Start a local TCP listener on a random port and confirm the scan finds it.
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
                var probes = new[] { new ProtocolProbe("test", "Test", port) };
                var result = await NetworkScanService.ScanAsync("127.0.0.1", probes);

                Assert.Equal(1, result.HostsFound);
                Assert.Single(result.Hosts);
                Assert.Equal("127.0.0.1", result.Hosts[0].Ip);
                Assert.Single(result.Hosts[0].Protocols);
                Assert.Equal("test", result.Hosts[0].Protocols[0].Type);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task ScanAsync_ResultContainsRangeAndStats()
        {
            var result = await NetworkScanService.ScanAsync("127.0.0.1");
            Assert.Equal("127.0.0.1", result.Range);
            Assert.Equal(1, result.HostsScanned);
            Assert.True(result.DurationMs >= 0);
        }
    }
}
