using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Knx
{
    public class KnxConnection : IDisposable
    {
        private static readonly ConcurrentDictionary<string, KnxConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

        public static bool TryGetConnection(string id, out KnxConnection? conn)
        {
            return _connections.TryGetValue(id, out conn);
        }

        // ── Concurrent connection limiting ───────────────────────────────────
        // Many KNXnet/IP gateways accept only a small number of simultaneous
        // tunnel connections, and even a burst of connect attempts against the
        // same gateway/network can cause handshakes to time out. We therefore cap
        // how many tunnel handshakes may run concurrently across the whole process
        // with a global semaphore. A connection holds a permit only while it is
        // establishing its tunnel (handshake); once connected it releases the
        // permit so other connections can establish theirs.
        private const int DefaultMaxConcurrentConnects = 2;
        private static int _maxConcurrentConnects = DefaultMaxConcurrentConnects;
        private static SemaphoreSlim _connectGate = new(DefaultMaxConcurrentConnects, DefaultMaxConcurrentConnects);
        private static readonly object _connectGateLock = new();

        /// <summary>
        /// Configures the maximum number of KNX tunnel handshakes that may run
        /// concurrently across the process. Values &lt;= 0 are clamped to 1.
        /// Safe to call before any connection is started (e.g. from host startup).
        /// </summary>
        public static void ConfigureMaxConcurrentConnects(int max)
        {
            if (max <= 0) max = 1;
            lock (_connectGateLock)
            {
                if (max == _maxConcurrentConnects) return;
                _maxConcurrentConnects = max;
                // Replace the gate; existing in-flight handshakes finish against
                // the old instance (they keep a local reference) and any waiters
                // are unblocked so they re-acquire on the new gate.
                var old = _connectGate;
                _connectGate = new SemaphoreSlim(max, max);
                try { old.Dispose(); } catch { }
            }
        }

        private static SemaphoreSlim CurrentConnectGate()
        {
            lock (_connectGateLock) { return _connectGate; }
        }

        private readonly ConnectionConfig _config;
        private readonly ConcurrentDictionary<ushort, byte[]> _rawCache = new();

        // All group addresses we know about (configured points + anything seen on the bus).
        // Used to drive the throttled read sweep so the cache fills with current state.
        private readonly ConcurrentDictionary<ushort, byte> _knownAddresses = new();
        private Task? _readSweepTask;

        // Throttling for the read sweep (gentle on the bus).
        private const int ReadSweepBatchSize = 10;
        private const int ReadSweepBatchDelayMs = 1000;
        private const int ReadSweepPerReadDelayMs = 50;
        // The sweep repeats periodically so that addresses which did not answer the
        // initial GroupValueRead (or whose value changed without a broadcast) are
        // refreshed instead of being stuck at "---" forever.
        private const int ReadSweepRepeatIntervalMs = 60_000;
        
        private IPEndPoint? _gatewayEndPoint;

        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private Task? _heartbeatTask;

        private byte _channelId;
        private byte _sendSeqNum;
        private bool _connected;
        private readonly object _stateLock = new();
        private readonly object _socketLock = new();

        // KNXnet/IP tunneling requires each TUNNELING_REQUEST to be acknowledged with a
        // TUNNELING_ACK (matching sequence number) before the next request is sent. We
        // serialize sends with this semaphore and signal the matching ACK via _pendingAck.
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private volatile TaskCompletionSource<bool>? _pendingAck;
        private volatile int _pendingAckSeq = -1;
        // The KNX spec allows up to 1s before retransmit, but under heavy inbound bus
        // traffic our ACK can be briefly delayed in the receive queue, causing spurious
        // retransmits. A slightly longer window avoids that while still recovering from
        // genuinely lost ACKs.
        private const int TunnelingAckTimeoutMs = 2000;
        private const int TunnelingAckRetries = 1;

        private readonly bool _secureEnabled;
        // All tunnelling traffic flows over a single TCP session (plain or secure). The
        // session transparently wraps/unwraps frames when secure.
        private KnxTcpSession? _session;
        // (Decrypted) plain KNXnet/IP frames arriving on the session are queued here and
        // consumed by the listener loop.
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _rxQueue = new();

        public KnxConnection(ConnectionConfig config)
        {
            _config = config;
            _secureEnabled = config.KnxSecureEnabled;
            _connections[config.Id] = this;
        }

        public byte[]? GetCachedValue(ushort groupAddress)
        {
            return _rawCache.TryGetValue(groupAddress, out var val) ? val : null;
        }

        /// <summary>True once a tunnel session is established with the gateway.</summary>
        public bool IsConnected
        {
            get { lock (_stateLock) { return _connected; } }
        }

        public void Start()
        {
            lock (_stateLock)
            {
                if (_listenTask != null) return;

                _cts = new CancellationTokenSource();

                string gatewayHost = _config.Address ?? "127.0.0.1";
                int gatewayPort = _config.Port ?? 3671;

                Log.Info($"[KNX-{_config.Id}] Starting in TUNNELING mode over TCP (gateway={gatewayHost}:{gatewayPort}, secure={_secureEnabled})...");
                IPAddress? gatewayIp;
                if (!IPAddress.TryParse(gatewayHost, out gatewayIp))
                {
                    var addresses = Dns.GetHostAddresses(gatewayHost);
                    Log.Info($"[KNX-{_config.Id}] DNS resolved '{gatewayHost}' to {addresses.Length} address(es): {string.Join(", ", addresses.Select(a => a.ToString()))}");
                    if (addresses.Length == 0)
                        throw new Exception($"Could not resolve KNX gateway host '{gatewayHost}'");
                    gatewayIp = addresses[0];
                }
                _gatewayEndPoint = new IPEndPoint(gatewayIp!, gatewayPort);
                Log.Info($"[KNX-{_config.Id}] Gateway endpoint resolved to {_gatewayEndPoint}");

                _listenTask = Task.Run(() => TunnelingConnectionLoop(_cts.Token));
            }
        }

        private async Task TunnelingConnectionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Limit concurrent tunnel handshakes across the process so we
                    // don't overwhelm gateways/the network. The permit is held only
                    // for the duration of the handshake and released as soon as the
                    // tunnel is up (or the attempt fails), so an established tunnel
                    // never ties up a slot needed by another connection.
                    var gate = CurrentConnectGate();
                    bool ok;
                    Log.Debug($"[KNX-{_config.Id}] Waiting for connect slot (max {_maxConcurrentConnects} concurrent handshakes)...");
                    await gate.WaitAsync(ct);
                    try
                    {
                        ok = await ConnectTunnelAsync(ct);
                    }
                    finally
                    {
                        try { gate.Release(); } catch (ObjectDisposedException) { /* gate replaced by ConfigureMaxConcurrentConnects */ }
                    }

                    if (ok)
                    {
                        // Start heartbeat loop
                        _heartbeatTask = Task.Run(() => HeartbeatLoop(ct), ct);

                        // Fill the cache with current state via a throttled read sweep.
                        // (Retriggered by the driver once configured points register.)
                        TriggerReadSweep();

                        // Run the listener loop synchronously in this task until connection drops
                        await ListenLoop(ct);
                    }
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                        Log.Error($"[KNX-{_config.Id}] Connection loop error: {ex.Message}");
                }

                // Cleanup socket and state before reconnecting
                CleanupSession();

                if (!ct.IsCancellationRequested)
                {
                    Log.Info($"[KNX-{_config.Id}] Retrying tunnel connection in 5 seconds...");
                    try { await Task.Delay(5000, ct); }
                    catch (TaskCanceledException) { break; }
                }
            }
        }

        /// <summary>
        /// Establishes a KNXnet/IP tunnel over TCP (plain or secure). The TCP transport and,
        /// for secure sessions, the handshake + per-frame encryption are handled by
        /// <see cref="KnxTcpSession"/>. Here we perform the tunnelling CONNECT_REQUEST/RESPONSE
        /// exchange on top of the session and funnel incoming frames into the listener queue.
        /// </summary>
        private async Task<bool> ConnectTunnelAsync(CancellationToken ct)
        {
            var session = new KnxTcpSession(_config.Id, _gatewayEndPoint!, _config);
            session.FrameReceived += frame =>
            {
                try { _rxQueue.Add(frame); } catch { /* queue completed during teardown */ }
            };
            session.Closed += () =>
            {
                lock (_stateLock) { _connected = false; }
            };

            if (!await session.ConnectAsync(ct))
            {
                session.Dispose();
                return false;
            }

            lock (_socketLock) { _session = session; }

            // CONNECT_REQUEST over the TCP session. The control/data HPAIs are sent in
            // route-back form (protocol=TCP, 0.0.0.0:0) because all traffic flows over the
            // single TCP connection — the gateway answers on the same socket.
            byte[] req = new byte[26];
            req[0] = 0x06; req[1] = 0x10;
            req[2] = 0x02; req[3] = 0x05; // CONNECT_REQUEST
            req[4] = 0x00; req[5] = 0x1A; // length 26
            WriteTcpHpai(req, 6);           // control endpoint (route-back)
            WriteTcpHpai(req, 14);          // data endpoint (route-back)
            req[22] = 0x04; // CRI length
            req[23] = 0x04; // tunnelling connection
            req[24] = 0x02; // KNX link layer
            req[25] = 0x00; // reserved

            for (int attempt = 1; attempt <= 3 && !ct.IsCancellationRequested; attempt++)
            {
                try
                {
                    Log.Info($"[KNX-{_config.Id}] Sending CONNECT_REQUEST (attempt {attempt})...");
                    await session.SendFrameAsync(req, ct);

                    byte[]? res = await DequeueFrameAsync(TimeSpan.FromSeconds(3), ct);
                    if (res == null)
                    {
                        Log.Warning($"[KNX-{_config.Id}] No CONNECT_RESPONSE within timeout (attempt {attempt}).");
                        continue;
                    }

                    ushort serviceType = (ushort)((res[2] << 8) | res[3]);
                    if (serviceType == 0x0206 && res.Length >= 8) // CONNECT_RESPONSE
                    {
                        byte channelId = res[6];
                        byte status = res[7];
                        if (status == 0x00)
                        {
                            _channelId = channelId;
                            _sendSeqNum = 0;
                            lock (_stateLock) { _connected = true; }
                            Log.Info($"[KNX-{_config.Id}] Tunnel established. Channel ID = {_channelId}");
                            return true;
                        }
                        Log.Error($"[KNX-{_config.Id}] Gateway rejected CONNECT_REQUEST. Status 0x{status:X2}.");
                        return false;
                    }

                    Log.Warning($"[KNX-{_config.Id}] Unexpected frame 0x{serviceType:X4} while waiting for CONNECT_RESPONSE.");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KNX-{_config.Id}] CONNECT_REQUEST attempt {attempt} failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Log.Error($"[KNX-{_config.Id}] Failed to establish KNX tunnel.");
            return false;
        }

        /// <summary>
        /// Writes an 8-byte route-back HPAI (protocol=TCP, IP/port all zero). Over a TCP
        /// tunnel the gateway always replies on the established connection, so the literal
        /// endpoint is never needed.
        /// </summary>
        private static void WriteTcpHpai(byte[] buffer, int offset)
        {
            buffer[offset] = 0x08;     // structure length
            buffer[offset + 1] = 0x02; // host protocol = TCP/IPv4
            for (int i = 2; i < 8; i++) buffer[offset + i] = 0x00;
        }

        /// <summary>Pull the next frame from the RX queue, or null on timeout.</summary>
        private async Task<byte[]?> DequeueFrameAsync(TimeSpan timeout, CancellationToken ct)
        {
            await Task.Yield();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < timeout && !ct.IsCancellationRequested)
            {
                if (_rxQueue.TryTake(out var frame, 100, ct))
                    return frame;
            }
            return null;
        }

        /// <summary>Sends a fully-formed plain KNXnet/IP frame to the gateway over the TCP session.</summary>
        private async Task SendKnxIpFrameAsync(byte[] frame, CancellationToken ct = default)
        {
            KnxTcpSession? session;
            lock (_socketLock) { session = _session; }
            if (session == null) throw new InvalidOperationException("TCP session not connected.");
            await session.SendFrameAsync(frame, ct);
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            Log.Debug($"[KNX-{_config.Id}] Heartbeat loop started.");
            
            // Build CONNECTIONSTATE_REQUEST (16 bytes)
            byte[] req = new byte[16];
            req[0] = 0x06; req[1] = 0x10;
            req[2] = 0x02; req[3] = 0x07; // CONNECTIONSTATE_REQUEST (0x0207)
            req[4] = 0x00; req[5] = 0x10; // Length=16
            // CRI info
            req[6] = _channelId;
            req[7] = 0x00; // Reserved
            // HPAI Control Endpoint (route-back over TCP)
            WriteTcpHpai(req, 8);

            int missedHeartbeats = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, ct); // Every 30 seconds

                    bool isConn;
                    lock (_stateLock) { isConn = _connected; }
                    if (!isConn) break;

                    Log.Debug($"[KNX-{_config.Id}] Sending CONNECTIONSTATE_REQUEST...");
                    await SendKnxIpFrameAsync(req, ct);

                    // We let the listener loop process the response. If the listener loop detects
                    // a disconnect response or timeout, it handles the connection teardown.
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning($"[KNX-{_config.Id}] Heartbeat failed to send: {ex.Message}");
                    missedHeartbeats++;
                    if (missedHeartbeats >= 3)
                    {
                        Log.Error($"[KNX-{_config.Id}] Gateway unresponsive. Triggering reconnect.");
                        lock (_stateLock) { _connected = false; }
                        break;
                    }
                }
            }
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            // The TCP session delivers (decrypted) plain KNXnet/IP frames into _rxQueue.
            await Task.Yield();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool isConn;
                    lock (_stateLock) { isConn = _connected; }
                    if (!isConn) break;

                    if (!_rxQueue.TryTake(out var data, 200, ct)) continue;
                    Log.Debug($"[KNX-{_config.Id}] RX {data.Length} bytes: {BitConverter.ToString(data)}");

                    if (data.Length < 6) continue;

                    // Parse KNXnet/IP Header
                    byte headerLen = data[0];
                    if (headerLen < 6 || data[1] != 0x10) continue;

                    ushort serviceType = (ushort)((data[2] << 8) | data[3]);
                    ushort totalLen = (ushort)((data[4] << 8) | data[5]);

                    if (data.Length < totalLen) continue;

                    if (serviceType == 0x0420) // TUNNELING_REQUEST
                    {
                        if (data.Length < 10) continue;
                        byte chan = data[7];
                        byte seq = data[8];

                        // Send TUNNELING_ACK immediately
                        byte[] ack = new byte[10];
                        ack[0] = 0x06; ack[1] = 0x10;
                        ack[2] = 0x04; ack[3] = 0x21; // TUNNELING_ACK (0x0421)
                        ack[4] = 0x00; ack[5] = 0x0A; // Length=10
                        ack[6] = 0x04; // Structure length
                        ack[7] = chan;
                        ack[8] = seq;
                        ack[9] = 0x00; // Status=Success

                        await SendKnxIpFrameAsync(ack, ct);

                        // Parse cEMI frame starting at index 10
                        ParseCemiFrame(data, 10, totalLen - 10);
                    }
                    else if (serviceType == 0x0421) // TUNNELING_ACK
                    {
                        byte ackSeq = data[8];
                        Log.Debug($"[KNX-{_config.Id}] Received TUNNELING_ACK for seq {ackSeq}");

                        // Release the sender waiting on this sequence number.
                        var pending = _pendingAck;
                        if (pending != null && _pendingAckSeq == ackSeq)
                        {
                            pending.TrySetResult(true);
                        }
                    }
                    else if (serviceType == 0x0208) // CONNECTIONSTATE_RESPONSE
                    {
                        byte chan = data[6];
                        byte status = data[7];
                        if (status != 0x00)
                        {
                            Log.Warning($"[KNX-{_config.Id}] Connection state response reported status 0x{status:X2}. Triggering disconnect.");
                            lock (_stateLock) { _connected = false; }
                            break;
                        }
                    }
                    else if (serviceType == 0x020A) // DISCONNECT_RESPONSE
                    {
                        Log.Info($"[KNX-{_config.Id}] Disconnected cleanly from gateway.");
                        lock (_stateLock) { _connected = false; }
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break; // Socket closed
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"[KNX-{_config.Id}] Listener error: {ex.Message}");
                }
            }
        }

        private void ParseCemiFrame(byte[] data, int offset, int length)
        {
            try
            {
                if (length < 10) return;

                byte msgCode = data[offset];
                // L_Data.ind = 0x29, L_Data.req = 0x11, L_Data.con = 0x2E
                if (msgCode != 0x29 && msgCode != 0x11 && msgCode != 0x2E) return;

                byte addInfoLen = data[offset + 1];
                int cemiPayloadOffset = offset + 2 + addInfoLen;
                int remaining = length - 2 - addInfoLen;

                if (remaining < 8) return;

                // Control Field 1 & 2
                // Source Address (2 bytes)
                // Destination Address (2 bytes)
                ushort destAddr = (ushort)((data[cemiPayloadOffset + 4] << 8) | data[cemiPayloadOffset + 5]);
                byte dataLen = data[cemiPayloadOffset + 6];

                if (remaining < 7 + dataLen) return;

                // TPCI/APCI (2 bytes)
                byte tpci = data[cemiPayloadOffset + 7];
                byte apci = data[cemiPayloadOffset + 8];

                // Check command (last 2 bits of first byte + first 2 bits of second byte)
                int command = ((tpci & 0x03) << 2) | ((apci & 0xC0) >> 6);
                
                // GroupValueWrite = 2, GroupValueResponse = 1
                if (command != 2 && command != 1) return;

                byte[] payload;
                if (dataLen == 1)
                {
                    // Small data (<= 6 bits) is embedded in the low 6 bits of apci byte
                    payload = new byte[] { (byte)(apci & 0x3F) };
                }
                else
                {
                    // Large data starts at index 9
                    payload = new byte[dataLen - 1];
                    Array.Copy(data, cemiPayloadOffset + 9, payload, 0, payload.Length);
                }

                _rawCache[destAddr] = payload;
                RegisterAddress(destAddr);
                Log.Debug($"[KNX-{_config.Id}] Received address {FormatGroupAddress(destAddr)} value: {BitConverter.ToString(payload)}");
            }
            catch (Exception ex)
            {
                Log.Debug($"[KNX-{_config.Id}] Failed to parse cEMI frame: {ex.Message}");
            }
        }

        public async Task SendGroupRead(ushort groupAddress)
        {
            await SendCemiFrame(groupAddress, new byte[] { 0 }, isSmall: true, isWrite: false);
        }

        /// <summary>Register a group address so the throttled read sweep will refresh it.</summary>
        public void RegisterAddress(ushort groupAddress)
        {
            if (groupAddress != 0)
                _knownAddresses.TryAdd(groupAddress, 0);
        }

        /// <summary>Register multiple group addresses (e.g. all configured points of a device).</summary>
        public void RegisterAddresses(IEnumerable<ushort> groupAddresses)
        {
            foreach (var ga in groupAddresses)
                RegisterAddress(ga);
        }

        /// <summary>
        /// Trigger the throttled, periodic read sweep across all known group addresses to
        /// populate the cache with current state. Safe to call repeatedly; the background
        /// sweep loop is started only once per connection and keeps running until the
        /// connection is cancelled/disposed.
        /// </summary>
        public void TriggerReadSweep()
        {
            lock (_stateLock)
            {
                // A sweep loop is already running for this connection.
                if (_readSweepTask is { IsCompleted: false }) return;
                if (_knownAddresses.IsEmpty) return; // nothing to read yet; will be retriggered once points register
                var ct = _cts?.Token ?? CancellationToken.None;
                _readSweepTask = Task.Run(() => ReadSweepLoop(ct), ct);
            }
        }

        private async Task ReadSweepLoop(CancellationToken ct)
        {
            try
            {
                bool firstPass = true;

                while (!ct.IsCancellationRequested)
                {
                    bool isConn;
                    lock (_stateLock) { isConn = _connected; }
                    if (!isConn) break;

                    // Snapshot so addresses added mid-sweep don't extend this pass indefinitely.
                    // On the first pass read everything; on later passes prioritise addresses
                    // that still have no cached value (never answered / changed silently), but
                    // still refresh the rest so stale values get corrected over time.
                    var allAddresses = _knownAddresses.Keys.ToArray();
                    var addresses = firstPass
                        ? allAddresses
                        : allAddresses.Where(ga => !_rawCache.ContainsKey(ga))
                                      .Concat(allAddresses.Where(ga => _rawCache.ContainsKey(ga)))
                                      .ToArray();

                    if (addresses.Length > 0)
                    {
                        int missing = addresses.Count(ga => !_rawCache.ContainsKey(ga));
                        Log.Info($"[KNX-{_config.Id}] Starting throttled read sweep for {addresses.Length} group address(es) ({missing} still uncached)...");

                        int sent = 0;
                        foreach (var ga in addresses)
                        {
                            if (ct.IsCancellationRequested) break;
                            lock (_stateLock) { isConn = _connected; }
                            if (!isConn) break;

                            try { await SendGroupRead(ga); }
                            catch (Exception ex) { Log.Debug($"[KNX-{_config.Id}] Read sweep request failed for {FormatGroupAddress(ga)}: {ex.Message}"); }

                            sent++;

                            if (sent % ReadSweepBatchSize == 0)
                            {
                                try { await Task.Delay(ReadSweepBatchDelayMs, ct); }
                                catch (TaskCanceledException) { break; }
                            }
                            else if (ReadSweepPerReadDelayMs > 0)
                            {
                                try { await Task.Delay(ReadSweepPerReadDelayMs, ct); }
                                catch (TaskCanceledException) { break; }
                            }
                        }

                        Log.Info($"[KNX-{_config.Id}] Read sweep complete ({sent} request(s) sent).");
                    }

                    firstPass = false;

                    // Wait before the next periodic refresh sweep.
                    try { await Task.Delay(ReadSweepRepeatIntervalMs, ct); }
                    catch (TaskCanceledException) { break; }
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[KNX-{_config.Id}] Read sweep loop error: {ex.Message}");
            }
        }

        public async Task SendGroupWrite(ushort groupAddress, byte[] data, bool isSmall)
        {
            await SendCemiFrame(groupAddress, data, isSmall, isWrite: true);
        }

        private async Task SendCemiFrame(ushort groupAddress, byte[] data, bool isSmall, bool isWrite)
        {
            KnxTcpSession? session;
            lock (_socketLock) { session = _session; }
            if (session == null) return;

            bool isConn;
            lock (_stateLock) { isConn = _connected; }
            if (!isConn) return;

            // CEMI length:
            // 2 (MsgCode, AddInfoLen) + 2 (Control) + 2 (Source) + 2 (Dest) + 1 (Len) + 2 (TPCI/APCI) + Payload (if large)
            int cemiLen = 11 + (isSmall ? 0 : data.Length);

            // Total length: 6 header + 4 connection header + CEMI
            int totalLen = 10 + cemiLen;
            byte[] pkt = new byte[totalLen];

            // Header
            pkt[0] = 0x06; pkt[1] = 0x10;
            pkt[2] = 0x04;
            pkt[3] = 0x20; // TUNNELING_REQUEST (0x0420)
            pkt[4] = (byte)((totalLen >> 8) & 0xFF);
            pkt[5] = (byte)(totalLen & 0xFF);

            // Connection Header (4 bytes). The sequence number (pkt[8]) is assigned
            // later, under the send gate, so it stays in lock-step with the ACKs.
            pkt[6] = 0x04;
            pkt[7] = _channelId;
            pkt[8] = 0x00;
            pkt[9] = 0x00;
            int cemiOffset = 10;

            // cEMI Frame
            pkt[cemiOffset] = 0x11;     // Message Code: L_Data.req
            pkt[cemiOffset + 1] = 0x00; // AddInfo length

            // Control fields
            pkt[cemiOffset + 2] = 0xBC; // Standard, repeat, low priority
            pkt[cemiOffset + 3] = 0xE0; // Group address destination, hop count 6

            // Source Address (2 bytes) - Gateway overwrites this
            pkt[cemiOffset + 4] = 0x00;
            pkt[cemiOffset + 5] = 0x00;

            // Destination Group Address (2 bytes)
            pkt[cemiOffset + 6] = (byte)((groupAddress >> 8) & 0xFF);
            pkt[cemiOffset + 7] = (byte)(groupAddress & 0xFF);

            // Data length (1 byte)
            pkt[cemiOffset + 8] = (byte)(isSmall ? 1 : (1 + data.Length));

            // TPCI/APCI (2 bytes)
            // Command codes: Read = 0, Write = 2
            byte cmdBits = (byte)(isWrite ? 0x80 : 0x00);
            pkt[cemiOffset + 9] = 0x00;

            if (isSmall)
            {
                pkt[cemiOffset + 10] = (byte)(cmdBits | (data[0] & 0x3F));
            }
            else
            {
                pkt[cemiOffset + 10] = cmdBits;
                Array.Copy(data, 0, pkt, cemiOffset + 11, data.Length);
            }

            // Tunneling: only one outstanding TUNNELING_REQUEST at a time. We assign the
            // sequence number, send, and wait for the matching TUNNELING_ACK before
            // releasing the gate so the gateway never sees out-of-order/unacked requests.
            await _sendGate.WaitAsync();
            try
            {
                byte seq = _sendSeqNum;
                pkt[8] = seq;

                var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingAck = ackTcs;
                _pendingAckSeq = seq;

                Log.Debug($"[KNX-{_config.Id}] Sending group packet to {FormatGroupAddress(groupAddress)} (seq {seq})...");

                bool acked = false;
                for (int attempt = 0; attempt <= TunnelingAckRetries; attempt++)
                {
                    try
                    {
                        Log.Debug($"[KNX-{_config.Id}] TX {pkt.Length} bytes: {BitConverter.ToString(pkt)}");
                        await SendKnxIpFrameAsync(pkt, _cts?.Token ?? default);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[KNX-{_config.Id}] Failed to send KNX telegram: {ex.Message}");
                        break;
                    }

                    var completed = await Task.WhenAny(ackTcs.Task, Task.Delay(TunnelingAckTimeoutMs));
                    if (completed == ackTcs.Task)
                    {
                        acked = true;
                        break;
                    }

                    if (attempt < TunnelingAckRetries)
                        Log.Debug($"[KNX-{_config.Id}] No TUNNELING_ACK for seq {seq} within {TunnelingAckTimeoutMs}ms; retransmitting...");
                }

                if (!acked)
                    Log.Warning($"[KNX-{_config.Id}] No TUNNELING_ACK for seq {seq} after {TunnelingAckRetries + 1} attempt(s).");

                // Advance the sequence number for the next request regardless (the gateway
                // expects monotonically increasing seq numbers, wrapping at 256).
                _sendSeqNum = (byte)(seq + 1);
            }
            finally
            {
                _pendingAck = null;
                _pendingAckSeq = -1;
                _sendGate.Release();
            }
        }

        private void CleanupSession()
        {
            lock (_stateLock)
            {
                _connected = false;
            }
            
            lock (_socketLock)
            {
                if (_session != null)
                {
                    try { _session.Dispose(); } catch { }
                    _session = null;
                }
            }
        }

        public static ushort ParseGroupAddress(string addressStr)
        {
            var parts = addressStr.Split('/');
            if (parts.Length == 3)
            {
                uint main = uint.Parse(parts[0]);
                uint middle = uint.Parse(parts[1]);
                uint sub = uint.Parse(parts[2]);
                if (main > 31 || middle > 7 || sub > 255)
                    throw new FormatException($"Invalid KNX group address range: {addressStr}");
                return (ushort)((main << 11) | (middle << 8) | sub);
            }
            else if (parts.Length == 2)
            {
                uint main = uint.Parse(parts[0]);
                uint sub = uint.Parse(parts[1]);
                if (main > 31 || sub > 2047)
                    throw new FormatException($"Invalid KNX group address range: {addressStr}");
                return (ushort)((main << 11) | sub);
            }
            else if (parts.Length == 1)
            {
                return ushort.Parse(parts[0]);
            }
            throw new FormatException($"Invalid KNX group address format: {addressStr}");
        }

        public static string FormatGroupAddress(ushort addr)
        {
            int main = (addr >> 11) & 0x1F;
            int middle = (addr >> 8) & 0x07;
            int sub = addr & 0xFF;
            return $"{main}/{middle}/{sub}";
        }

        public void Dispose()
        {
            Log.Info($"[KNX-{_config.Id}] Disposing connection...");
            _connections.TryRemove(_config.Id, out _);

            _cts?.Cancel();

            if (_connected && _gatewayEndPoint != null)
            {
                // Send DISCONNECT_REQUEST (16 bytes) over the TCP session.
                byte[] dis = new byte[16];
                dis[0] = 0x06; dis[1] = 0x10;
                dis[2] = 0x02; dis[3] = 0x09; // DISCONNECT_REQUEST
                dis[4] = 0x00; dis[5] = 0x10;
                dis[6] = _channelId;
                dis[7] = 0x00;
                WriteTcpHpai(dis, 8); // route-back over the TCP session

                try
                {
                    KnxTcpSession? session;
                    lock (_socketLock) { session = _session; }
                    session?.SendFrameAsync(dis).GetAwaiter().GetResult();
                }
                catch { }
            }

            CleanupSession();
            _cts?.Dispose();

            // Unblock any sender waiting on an ACK and release pooled resources.
            _pendingAck?.TrySetResult(false);
            try { _sendGate.Dispose(); } catch { }
        }
    }
}
