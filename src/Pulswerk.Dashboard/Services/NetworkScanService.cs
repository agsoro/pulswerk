// NetworkScanService.cs – scans an IP range for devices speaking known protocols
//
//  Supported protocols and their default ports:
//    Modbus TCP  → 502
//    BACnet/IP   → 47808 (0xBAC0)
//    KNXnet/IP    → 3671
//    OCPP        → 9000 (common WebSocket CS port; also probes 80/443)
//
//  The scan opens a short-lived TCP connection to each candidate port. A host
//  is reported once for every port that accepted the connection. The scan is
//  parallelised with a bounded degree of concurrency so a /24 sweep finishes
//  in a few seconds.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Dashboard.Services
{
    /// <summary>
    /// Describes a single protocol probe target.
    /// </summary>
    public record ProtocolProbe(string Type, string Label, int Port);

    /// <summary>
    /// A host discovered during the scan, with the list of protocols that responded.
    /// </summary>
    public record ScanResultHost(string Ip, List<ScanResultProtocol> Protocols);

    /// <summary>
    /// A protocol that was detected as open on a scanned host.
    /// </summary>
    public record ScanResultProtocol(string Type, string Label, int Port);

    /// <summary>
    /// The full result of a network scan request.
    /// </summary>
    public record NetworkScanResult(
        string Range,
        int HostsScanned,
        int HostsFound,
        List<ScanResultHost> Hosts,
        int DurationMs);

    /// <summary>
    /// Stateless helper that sweeps an IP range and reports hosts that accept
    /// TCP connections on known industrial-protocol ports.
    /// </summary>
    public static class NetworkScanService
    {
        // Default probes. The UI also receives this list via the metadata endpoint
        // so the user can see which ports are being tried.
        public static readonly IReadOnlyList<ProtocolProbe> DefaultProbes = new[]
        {
            new ProtocolProbe("modbus-tcp", "Modbus TCP",  502),
            new ProtocolProbe("bacnet-ip",  "BACnet/IP",   47808),
            new ProtocolProbe("knx-ip",     "KNXnet/IP",    3671),
            new ProtocolProbe("ocpp",       "OCPP (WS)",   9000),
        };

        // Per-port connect timeout. Kept short so a full /24 sweep stays snappy
        // even when most hosts are dark.
        private const int ConnectTimeoutMs = 600;

        // Maximum number of concurrent (host × port) probes. 64 keeps a typical
        // home/office network saturated without exhausting the ephemeral port range.
        private const int MaxConcurrency = 64;

        /// <summary>
        /// Scans the given range for hosts speaking any of the known protocols.
        /// </summary>
        /// <param name="range">
        /// Accepted forms:
        ///   "192.168.1.0/24"            – CIDR
        ///   "192.168.1.1-254"           – last-octet range
        ///   "192.168.1.1-192.168.1.254"  – explicit start/end
        ///   "192.168.1.10"              – single host
        /// </param>
        /// <param name="ports">Optional override list of ports to probe. When null the default probes are used.</param>
        public static async Task<NetworkScanResult> ScanAsync(string range, IReadOnlyList<ProtocolProbe>? probes = null, CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var probeList = probes ?? DefaultProbes;
            var ips = ParseRange(range);

            var hosts = new ConcurrentBag<ScanResultHost>();
            using var sem = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

            var tasks = ips.Select(ip => ProbeHostAsync(ip, probeList, sem, hosts, ct));
            await Task.WhenAll(tasks);

            sw.Stop();
            var sorted = hosts.OrderBy(h => IPAddress.Parse(h.Ip).GetAddressBytes().Aggregate(0, (a, b) => a * 256 + b)).ToList();
            return new NetworkScanResult(range, ips.Count, sorted.Count, sorted, (int)sw.ElapsedMilliseconds);
        }

        // ── Range parsing ────────────────────────────────────────────────────

        /// <summary>
        /// Expands the range expression into a list of individual IPv4 addresses.
        /// Caps the result at 1024 entries to prevent accidental huge scans.
        /// </summary>
        public static List<string> ParseRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range))
                throw new ArgumentException("Range expression is empty.");

            range = range.Trim();

            // CIDR: 192.168.1.0/24
            if (range.Contains('/'))
            {
                var parts = range.Split('/');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
                    throw new ArgumentException($"Invalid CIDR '{range}'.");
                return ExpandCidr(parts[0].Trim(), prefix);
            }

            // Explicit start-end: 192.168.1.1-192.168.1.254
            if (range.Contains('-'))
            {
                var dash = range.IndexOf('-');
                var startStr = range.Substring(0, dash).Trim();
                var endStr = range.Substring(dash + 1).Trim();

                // Last-octet range: 192.168.1.1-254
                if (!endStr.Contains('.') && IPAddress.TryParse(startStr, out var startIp))
                {
                    if (!int.TryParse(endStr, out var lastOctet) || lastOctet < 0 || lastOctet > 255)
                        throw new ArgumentException($"Invalid last-octet range '{range}'.");
                    var bytes = startIp.GetAddressBytes();
                    if (bytes.Length != 4) throw new ArgumentException("Only IPv4 ranges are supported.");
                    int startOctet = bytes[3];
                    int lo = Math.Min(startOctet, lastOctet);
                    int hi = Math.Max(startOctet, lastOctet);
                    var result = new List<string>();
                    for (int i = lo; i <= hi; i++)
                    {
                        bytes[3] = (byte)i;
                        result.Add(new IPAddress(bytes).ToString());
                    }
                    return Cap(result);
                }

                // Full start-end IPs
                if (!IPAddress.TryParse(startStr, out var s) || !IPAddress.TryParse(endStr, out var e))
                    throw new ArgumentException($"Invalid IP range '{range}'.");
                return ExpandRange(s, e);
            }

            // Single host
            if (!IPAddress.TryParse(range, out var single))
                throw new ArgumentException($"Invalid IP address '{range}'.");
            return new List<string> { single.ToString() };
        }

        private static List<string> ExpandCidr(string ipStr, int prefix)
        {
            if (!IPAddress.TryParse(ipStr, out var ip)) throw new ArgumentException($"Invalid IP '{ipStr}'.");
            var bytes = ip.GetAddressBytes();
            if (bytes.Length != 4) throw new ArgumentException("Only IPv4 CIDR is supported.");

            uint addr = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            uint mask = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix);
            uint network = addr & mask;
            uint broadcast = network | ~mask;

            var result = new List<string>();
            for (uint a = network; a <= broadcast; a++)
            {
                result.Add(new IPAddress(new byte[]
                {
                    (byte)(a >> 24), (byte)(a >> 16 & 0xFF),
                    (byte)(a >> 8 & 0xFF), (byte)(a & 0xFF)
                }).ToString());
                if (a == broadcast) break; // guard against wrap-around
            }
            return Cap(result);
        }

        private static List<string> ExpandRange(IPAddress start, IPAddress end)
        {
            var sb = start.GetAddressBytes();
            var eb = end.GetAddressBytes();
            if (sb.Length != 4 || eb.Length != 4) throw new ArgumentException("Only IPv4 ranges are supported.");

            uint s = (uint)(sb[0] << 24 | sb[1] << 16 | sb[2] << 8 | sb[3]);
            uint e = (uint)(eb[0] << 24 | eb[1] << 16 | eb[2] << 8 | eb[3]);
            uint lo = Math.Min(s, e), hi = Math.Max(s, e);

            var result = new List<string>();
            for (uint a = lo; a <= hi; a++)
            {
                result.Add(new IPAddress(new byte[]
                {
                    (byte)(a >> 24), (byte)(a >> 16 & 0xFF),
                    (byte)(a >> 8 & 0xFF), (byte)(a & 0xFF)
                }).ToString());
                if (a == hi) break;
            }
            return Cap(result);
        }

        private static List<string> Cap(List<string> ips)
        {
            const int Max = 1024;
            if (ips.Count > Max)
                throw new ArgumentException($"Range expands to {ips.Count} addresses (max {Max}). Please narrow the range.");
            return ips;
        }

        // ── Probing ──────────────────────────────────────────────────────────

        private static async Task ProbeHostAsync(string ip, IReadOnlyList<ProtocolProbe> probes, SemaphoreSlim sem, ConcurrentBag<ScanResultHost> hosts, CancellationToken ct)
        {
            var found = new List<ScanResultProtocol>();
            foreach (var probe in probes)
            {
                await sem.WaitAsync(ct);
                try
                {
                    if (await IsPortOpenAsync(ip, probe.Port, ct))
                        found.Add(new ScanResultProtocol(probe.Type, probe.Label, probe.Port));
                }
                catch { /* ignore individual probe failures */ }
                finally { sem.Release(); }
            }

            if (found.Count > 0)
                hosts.Add(new ScanResultHost(ip, found));
        }

        private static async Task<bool> IsPortOpenAsync(string ip, int port, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(ConnectTimeoutMs);
                await client.ConnectAsync(IPAddress.Parse(ip), port, cts.Token);
                return client.Connected;
            }
            catch { return false; }
        }
    }
}
