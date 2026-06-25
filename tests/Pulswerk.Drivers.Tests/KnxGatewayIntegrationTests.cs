using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Pulswerk.Drivers.Tests
{
    /// <summary>
    /// Integration tests that talk to a REAL KNXnet/IP gateway.
    ///
    /// These are opt-in: they only run when the environment variable
    /// <c>KNX_GATEWAY</c> is set (e.g. <c>KNX_GATEWAY=10.10.1.40</c>). When it is
    /// not set the tests no-op so CI and other developers are unaffected.
    ///
    /// They were written against an MDT KNX TP IP gateway reachable at
    /// 10.10.1.40:3671 through a routed OPNsense firewall. Because the gateway
    /// sits behind a router/NAT, all HPAI endpoints use the KNXnet/IP
    /// "route-back" form (protocol=UDP, IP=0.0.0.0, port=0) so the gateway
    /// replies to the UDP source address instead of an unreachable private IP.
    ///
    /// Usage:
    ///   KNX_GATEWAY=10.10.1.40 dotnet test --filter KnxGatewayIntegration
    /// </summary>
    public class KnxGatewayIntegrationTests
    {
        private const int DefaultPort = 3671;
        private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

        private readonly ITestOutputHelper _output;

        public KnxGatewayIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static (string host, int port)? GetGateway()
        {
            string? value = Environment.GetEnvironmentVariable("KNX_GATEWAY");
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string host = value;
            int port = DefaultPort;
            int idx = value.LastIndexOf(':');
            if (idx > 0 && int.TryParse(value[(idx + 1)..], out int p))
            {
                host = value[..idx];
                port = p;
            }
            return (host, port);
        }

        // --- KNXnet/IP frame helpers (route-back HPAI) ----------------------------

        /// <summary>HPAI in route-back form: len=8, proto=UDP(0x01), IP=0.0.0.0, port=0.</summary>
        private static byte[] RouteBackHpai() => new byte[] { 0x08, 0x01, 0, 0, 0, 0, 0, 0 };

        private static byte[] Frame(ushort service, byte[] body)
        {
            int total = 6 + body.Length;
            byte[] f = new byte[total];
            f[0] = 0x06; f[1] = 0x10;
            f[2] = (byte)(service >> 8); f[3] = (byte)(service & 0xFF);
            f[4] = (byte)(total >> 8); f[5] = (byte)(total & 0xFF);
            Array.Copy(body, 0, f, 6, body.Length);
            return f;
        }

        private static ushort ServiceOf(byte[] data) =>
            data.Length >= 4 ? (ushort)((data[2] << 8) | data[3]) : (ushort)0;

        private async Task<byte[]?> SendAndReceiveAsync(UdpClient client, IPEndPoint gw, byte[] packet, CancellationToken ct)
        {
            await client.SendAsync(packet, packet.Length, gw);
            var recvTask = client.ReceiveAsync(ct).AsTask();
            var completed = await Task.WhenAny(recvTask, Task.Delay(ReplyTimeout, ct));
            if (completed != recvTask)
                return null; // timeout
            var result = await recvTask;
            return result.Buffer;
        }

        // --- Tests ----------------------------------------------------------------

        [Fact]
        [Trait("Category", "KnxGatewayIntegration")]
        public async Task RealGateway_RespondsToDescriptionRequest()
        {
            var gw = GetGateway();
            if (gw is null)
            {
                _output.WriteLine("KNX_GATEWAY not set; skipping real-gateway integration test.");
                return;
            }

            var (host, port) = gw.Value;
            var endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new UdpClient(0); // bind any local port

            byte[] describe = Frame(0x0203, RouteBackHpai());
            byte[]? reply = await SendAndReceiveAsync(client, endpoint, describe, cts.Token);

            Assert.True(reply is not null,
                $"No DESCRIPTION_RESPONSE from {endpoint}. Check route-back HPAI / firewall.");
            Assert.Equal(0x0204, ServiceOf(reply!)); // DESCRIPTION_RESPONSE

            // Device DIB starts right after the 6-byte header: len, type(0x01), ...name at +24.
            byte[] body = reply!;
            Assert.True(body.Length >= 6 + 54, "DESCRIPTION_RESPONSE too short for a device DIB.");
            Assert.Equal(0x01, body[6 + 1]); // DIB type = device info
            string name = System.Text.Encoding.Latin1
                .GetString(body, 6 + 24, 30).TrimEnd('\0', ' ');
            _output.WriteLine($"Gateway '{endpoint}' identified as: '{name}'");
            Assert.False(string.IsNullOrWhiteSpace(name), "Gateway returned an empty friendly name.");
        }

        [Fact]
        [Trait("Category", "KnxGatewayIntegration")]
        public async Task RealGateway_AcceptsPlainTunnelingConnect()
        {
            var gw = GetGateway();
            if (gw is null)
            {
                _output.WriteLine("KNX_GATEWAY not set; skipping real-gateway integration test.");
                return;
            }

            var (host, port) = gw.Value;
            var endpoint = new IPEndPoint(IPAddress.Parse(host), port);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var client = new UdpClient(0);

            // CONNECT_REQUEST: control HPAI + data HPAI + CRI(tunnel, link layer).
            byte[] cri = { 0x04, 0x04, 0x02, 0x00 };
            byte[] body = new byte[8 + 8 + 4];
            Array.Copy(RouteBackHpai(), 0, body, 0, 8);  // control endpoint (route-back)
            Array.Copy(RouteBackHpai(), 0, body, 8, 8);  // data endpoint (route-back)
            Array.Copy(cri, 0, body, 16, 4);
            byte[] connect = Frame(0x0205, body);

            byte[]? reply = await SendAndReceiveAsync(client, endpoint, connect, cts.Token);

            Assert.True(reply is not null,
                $"No CONNECT_RESPONSE from {endpoint}. Gateway may require IP Secure or UDP is blocked.");
            Assert.Equal(0x0206, ServiceOf(reply!)); // CONNECT_RESPONSE

            byte channel = reply![6];
            byte status = reply![7];
            _output.WriteLine($"CONNECT_RESPONSE channel={channel} status=0x{status:X2}");
            Assert.Equal(0x00, status); // E_NO_ERROR -> plain (non-secure) tunnelling accepted

            // Clean up: DISCONNECT_REQUEST so we don't hold a tunnel slot.
            byte[] disBody = new byte[2 + 8];
            disBody[0] = channel;
            disBody[1] = 0x00;
            Array.Copy(RouteBackHpai(), 0, disBody, 2, 8);
            byte[] disconnect = Frame(0x0209, disBody);
            try
            {
                await client.SendAsync(disconnect, disconnect.Length, endpoint);
                _output.WriteLine($"DISCONNECT_REQUEST sent for channel {channel}.");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"DISCONNECT send failed (non-fatal): {ex.Message}");
            }
        }

        /// <summary>
        /// Drives the PRODUCTION <see cref="Pulswerk.Drivers.Knx.KnxConnection"/> class
        /// with <c>knxNatMode = true</c> against the real gateway and asserts that it
        /// establishes a tunnel through the firewall. This validates the route-back HPAI
        /// implementation end-to-end (not just a hand-rolled probe).
        /// </summary>
        [Fact]
        [Trait("Category", "KnxGatewayIntegration")]
        public async Task ProductionDriver_ConnectsWithNatMode()
        {
            var gw = GetGateway();
            if (gw is null)
            {
                _output.WriteLine("KNX_GATEWAY not set; skipping real-gateway integration test.");
                return;
            }

            var (host, port) = gw.Value;

            var config = new Pulswerk.Core.ConnectionConfig(
                Id: "knx-integration-natmode",
                Type: "knx-ip",
                Address: host,
                Port: port,
                KnxConnectionType: "tunneling",
                KnxIndividualAddress: "15.15.250",
                KnxSecureEnabled: false,
                KnxNatMode: true,
                Name: "Integration Gateway");

            var conn = new Pulswerk.Drivers.Knx.KnxConnection(config);
            try
            {
                conn.Start();

                // Wait up to 10s for the tunnel to come up.
                bool connected = await WaitForAsync(() => conn.IsConnected, TimeSpan.FromSeconds(10));
                Assert.True(connected,
                    "Production KnxConnection did not establish a tunnel in NAT mode. " +
                    "Check route-back HPAI implementation and firewall.");
                _output.WriteLine("Production KnxConnection established a tunnel in NAT mode.");

                // The connection should be resolvable through the static registry.
                Assert.True(Pulswerk.Drivers.Knx.KnxConnection.TryGetConnection(config.Id, out var fromRegistry));
                Assert.NotNull(fromRegistry);

                // Issue a harmless group-read so we exercise the send path over the
                // established tunnel (no assertion on the value; we just verify no throw).
                ushort ga = Pulswerk.Drivers.Knx.KnxConnection.ParseGroupAddress("0/0/1");
                await conn.SendGroupRead(ga);
                _output.WriteLine($"Sent GroupValueRead to {Pulswerk.Drivers.Knx.KnxConnection.FormatGroupAddress(ga)} over the NAT tunnel.");
            }
            finally
            {
                conn.Dispose(); // sends DISCONNECT_REQUEST (route-back) and tears down
            }
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (condition()) return true;
                await Task.Delay(200);
            }
            return condition();
        }
    }
}
