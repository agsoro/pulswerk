using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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

        private readonly ConnectionConfig _config;
        private readonly ConcurrentDictionary<ushort, byte[]> _rawCache = new();
        
        private UdpClient? _udpClient;
        private IPEndPoint? _gatewayEndPoint;
        private IPEndPoint? _localEndPoint;
        private IPAddress? _localIp;
        private int _localPort;

        private CancellationTokenSource? _cts;
        private Task? _listenTask;
        private Task? _heartbeatTask;

        private byte _channelId;
        private byte _sendSeqNum;
        private bool _connected;
        private readonly object _stateLock = new();
        private readonly object _socketLock = new();

        private readonly bool _isRouting;
        private readonly string _connectionMode;
        private readonly bool _natMode;

        private readonly bool _secureEnabled;
        private byte[]? _sessionKey;
        private ushort _secureSessionId;
        private ulong _sendSecureCounter;
        private ulong _recvSecureCounter;

        public KnxConnection(ConnectionConfig config)
        {
            _config = config;
            _connectionMode = (config.KnxConnectionType ?? "tunneling").ToLowerInvariant();
            _isRouting = _connectionMode == "routing";
            _natMode = config.KnxNatMode;
            _secureEnabled = config.KnxSecureEnabled;
            _sendSecureCounter = 0;
            _recvSecureCounter = 0;
            _connections[config.Id] = this;
        }

        public byte[]? GetCachedValue(ushort groupAddress)
        {
            return _rawCache.TryGetValue(groupAddress, out var val) ? val : null;
        }

        /// <summary>True once a tunnel/routing session is established with the gateway.</summary>
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

                if (_isRouting)
                {
                    Log.Info($"[KNX-{_config.Id}] Starting in ROUTING mode (multicast 224.0.23.12:3671)...");
                    _gatewayEndPoint = new IPEndPoint(IPAddress.Parse("224.0.23.12"), 3671);
                    
                    _udpClient = new UdpClient();
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 3671));
                    _udpClient.JoinMulticastGroup(_gatewayEndPoint.Address);
                    _connected = true;

                    _listenTask = Task.Run(() => ListenLoop(_cts.Token));
                }
                else
                {
                    Log.Info($"[KNX-{_config.Id}] Starting in TUNNELING mode (gateway={gatewayHost}:{gatewayPort}, secure={_secureEnabled})...");
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
        }

        private async Task TunnelingConnectionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool ok = await ConnectTunnelAsync(ct);
                    if (ok)
                    {
                        // Start heartbeat loop
                        _heartbeatTask = Task.Run(() => HeartbeatLoop(ct), ct);

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

        private async Task<bool> ConnectTunnelAsync(CancellationToken ct)
        {
            lock (_socketLock)
            {
                int bindPort = _config.LocalPort ?? 0;
                _udpClient = new UdpClient(bindPort);
                _localPort = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

                bool resolved = false;
                if (!string.IsNullOrWhiteSpace(_config.LocalAddress))
                {
                    if (IPAddress.TryParse(_config.LocalAddress, out var parsedLocalIp))
                    {
                        // 0.0.0.0 / :: are bind-any wildcards, not routable addresses.
                        // Advertising them in the HPAI control endpoint makes the KNX
                        // gateway unable to send CONNECT_RESPONSE back to us, so fall
                        // through to auto-resolution instead.
                        if (parsedLocalIp.Equals(IPAddress.Any) || parsedLocalIp.Equals(IPAddress.IPv6Any))
                        {
                            Log.Info($"[KNX-{_config.Id}] Configured localAddress '{_config.LocalAddress}' is a wildcard (bind-any); auto-resolving routable local IP instead.");
                        }
                        else
                        {
                            _localIp = parsedLocalIp;
                            resolved = true;
                        }
                    }
                    else
                    {
                        Log.Warning($"[KNX-{_config.Id}] Configured localAddress '{_config.LocalAddress}' is invalid. Falling back to auto-resolution.");
                    }
                }

                if (!resolved)
                {
                    // Retrieve local IP by temporarily connecting a socket
                    using (var temp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        temp.Connect(_gatewayEndPoint!);
                        _localEndPoint = (IPEndPoint)temp.LocalEndPoint!;
                        _localIp = _localEndPoint.Address;
                    }
                    Log.Info($"[KNX-{_config.Id}] LocalAddress not configured; auto-resolved local IP via temp socket to {_localIp} (bindPort={bindPort})");
                }
                else
                {
                    Log.Info($"[KNX-{_config.Id}] Using configured LocalAddress '{_config.LocalAddress}' -> {_localIp} (bindPort={bindPort})");
                }

                Log.Info($"[KNX-{_config.Id}] Local tunnel endpoint resolved to {_localIp}:{_localPort}");
            }

            if (_secureEnabled)
            {
                int attempts = 0;
                while (attempts++ < 3 && !ct.IsCancellationRequested)
                {
                    try
                    {
                        Log.Info($"[KNX-{_config.Id}] Initiating secure tunneling handshake (attempt {attempts})...");

                        // 1. Generate client ECDH Curve25519 keypair using BouncyCastle
                        byte[] clientPrivateKey = new byte[32];
                        byte[] clientPublicKey = new byte[32];
                        var secureRandom = new Org.BouncyCastle.Security.SecureRandom();
                        secureRandom.NextBytes(clientPrivateKey);
                        clientPrivateKey[0] &= 248;
                        clientPrivateKey[31] &= 127;
                        clientPrivateKey[31] |= 64;
                        Org.BouncyCastle.Math.EC.Rfc7748.X25519.GeneratePublicKey(clientPrivateKey, 0, clientPublicKey, 0);

                        // 2. Build SESSION_REQUEST (46 bytes)
                        byte[] sReq = new byte[46];
                        sReq[0] = 0x06; sReq[1] = 0x10; // Header length & version
                        sReq[2] = 0x09; sReq[3] = 0x51; // SESSION_REQUEST type
                        sReq[4] = 0x00; sReq[5] = 0x2E; // Length = 46

                        // HPAI Control Endpoint (route-back in NAT mode)
                        WriteHpai(sReq, 6);

                        // Client Public Key
                        Array.Copy(clientPublicKey, 0, sReq, 14, 32);

                        Log.Debug($"[KNX-{_config.Id}] SESSION_REQUEST payload ({sReq.Length} bytes): {BitConverter.ToString(sReq)}");
                        Log.Debug($"[KNX-{_config.Id}] Client public key: {BitConverter.ToString(clientPublicKey)}");
                        Log.Info($"[KNX-{_config.Id}] Sending SESSION_REQUEST to {_gatewayEndPoint}...");
                        int sent = await _udpClient!.SendAsync(sReq, sReq.Length, _gatewayEndPoint);
                        Log.Debug($"[KNX-{_config.Id}] Sent {sent} bytes. Waiting up to 2s for SESSION_RESPONSE (0x0952)...");

                        // Wait up to 2 seconds for SESSION_RESPONSE (0x0952)
                        var receiveTask = _udpClient.ReceiveAsync(ct).AsTask();
                        var delayTask = Task.Delay(2000, ct);
                        var completedTask = await Task.WhenAny(receiveTask, delayTask);

                        if (completedTask == receiveTask)
                        {
                            var result = await receiveTask;
                            byte[] res = result.Buffer;
                            Log.Debug($"[KNX-{_config.Id}] Received {res.Length} bytes from {result.RemoteEndPoint}: {BitConverter.ToString(res)}");

                            if (res.Length >= 56 && res[2] == 0x09 && res[3] == 0x52) // SESSION_RESPONSE
                            {
                                _secureSessionId = (ushort)((res[6] << 8) | res[7]);
                                    byte[] serverPublicKey = new byte[32];
                                Array.Copy(res, 8, serverPublicKey, 0, 32);
                                byte[] serverMac = new byte[16];
                                Array.Copy(res, 40, serverMac, 0, 16);

                                Log.Debug($"[KNX-{_config.Id}] SESSION_RESPONSE parsed. SSID={_secureSessionId}, serverPubKey={BitConverter.ToString(serverPublicKey)}, mac={BitConverter.ToString(serverMac)}");

                                // 3. Derive shared secret and session key
                                byte[] sharedSecret = new byte[32];
                                Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(clientPrivateKey, 0, serverPublicKey, 0, sharedSecret, 0);

                                byte[] keyMaterial;
                                using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(_config.KnxPassword ?? "")))
                                {
                                    keyMaterial = hmac.ComputeHash(sharedSecret);
                                }
                                _sessionKey = new byte[16];
                                Array.Copy(keyMaterial, 0, _sessionKey, 0, 16);

                                Log.Debug($"[KNX-{_config.Id}] Shared secret: {BitConverter.ToString(sharedSecret)}");
                                Log.Debug($"[KNX-{_config.Id}] Key material (HMAC-SHA256): {BitConverter.ToString(keyMaterial)}");
                                Log.Debug($"[KNX-{_config.Id}] Session key (first 16 bytes): {BitConverter.ToString(_sessionKey)}");
                                Log.Debug($"[KNX-{_config.Id}] KnxUserId={_config.KnxUserId}, KnxPassword set={(string.IsNullOrEmpty(_config.KnxPassword) ? "NO" : "YES")} (len={_config.KnxPassword?.Length ?? 0})");
                                Log.Info($"[KNX-{_config.Id}] Secure session response received. SSID = {_secureSessionId}. Key derived successfully.");

                                // 4. Send SESSION_AUTHENTICATE (0x0953) inside SECURE_WRAPPER (0x0950)
                                _sendSecureCounter = 1;
                                _recvSecureCounter = 0;

                                byte[] authPayload = new byte[15];
                                authPayload[0] = 0x06; authPayload[1] = 0x10; // Header
                                authPayload[2] = 0x09; authPayload[3] = 0x53; // SESSION_AUTHENTICATE
                                authPayload[4] = 0x00; authPayload[5] = 0x0F; // Length = 15
                                authPayload[6] = (byte)_config.KnxUserId;
                                authPayload[7] = 0x00; // Reserved
                                for (int i = 0; i < 7; i++) authPayload[8 + i] = (byte)(i ^ 0xAA);

                                Log.Debug($"[KNX-{_config.Id}] SESSION_AUTHENTICATE plaintext ({authPayload.Length} bytes): {BitConverter.ToString(authPayload)}");

                                byte[] wrappedAuth = EncryptFrame(authPayload);
                                Log.Debug($"[KNX-{_config.Id}] SECURE_WRAPPER around SESSION_AUTHENTICATE ({wrappedAuth.Length} bytes): {BitConverter.ToString(wrappedAuth)}");
                                int authSent = await _udpClient.SendAsync(wrappedAuth, wrappedAuth.Length, _gatewayEndPoint);
                                Log.Debug($"[KNX-{_config.Id}] Sent {authSent} bytes. Waiting up to 2s for auth response...");

                                // Wait for confirmation
                                receiveTask = _udpClient.ReceiveAsync(ct).AsTask();
                                delayTask = Task.Delay(2000, ct);
                                completedTask = await Task.WhenAny(receiveTask, delayTask);

                                if (completedTask == receiveTask)
                                {
                                    var authResResult = await receiveTask;
                                    byte[] authRes = authResResult.Buffer;
                                    Log.Debug($"[KNX-{_config.Id}] Auth response: {authRes.Length} bytes from {authResResult.RemoteEndPoint}: {BitConverter.ToString(authRes)}");

                                    if (authRes.Length >= 22 && authRes[2] == 0x09 && authRes[3] == 0x50) // SECURE_WRAPPER
                                    {
                                        byte[]? decryptedAuthRes = DecryptFrame(authRes);
                                        if (decryptedAuthRes == null)
                                        {
                                            Log.Warning($"[KNX-{_config.Id}] DecryptFrame returned null for auth response. MAC verification likely failed.");
                                        }
                                        else
                                        {
                                            Log.Debug($"[KNX-{_config.Id}] Decrypted auth response ({decryptedAuthRes.Length} bytes): {BitConverter.ToString(decryptedAuthRes)}");
                                        }
                                        if (decryptedAuthRes != null && decryptedAuthRes.Length >= 8 && decryptedAuthRes[2] == 0x09 && decryptedAuthRes[3] == 0x53)
                                        {
                                            _channelId = (byte)(_secureSessionId & 0xFF);
                                            lock (_stateLock)
                                            {
                                                _connected = true;
                                            }
                                            Log.Info($"[KNX-{_config.Id}] Secure tunnel authenticated successfully. Channel ID = {_channelId}");
                                            return true;
                                        }
                                        else
                                        {
                                            Log.Warning($"[KNX-{_config.Id}] Auth response decrypted but unexpected content. ServiceType=0x{(decryptedAuthRes != null && decryptedAuthRes.Length >= 4 ? (decryptedAuthRes[2] << 8 | decryptedAuthRes[3]).ToString("X4") : "????")}");
                                        }
                                    }
                                    else
                                    {
                                        Log.Warning($"[KNX-{_config.Id}] Auth response is not a SECURE_WRAPPER. ServiceType=0x{(authRes.Length >= 4 ? (authRes[2] << 8 | authRes[3]).ToString("X4") : "????")}, len={authRes.Length}.");
                                    }
                                }
                                else
                                {
                                    Log.Warning($"[KNX-{_config.Id}] Timed out waiting for auth response. No data within 2s.");
                                }
                            }
                            else
                            {
                                Log.Warning($"[KNX-{_config.Id}] Unexpected response (not SESSION_RESPONSE). ServiceType=0x{(res.Length >= 4 ? (res[2] << 8 | res[3]).ToString("X4") : "????")}, len={res.Length}.");
                            }
                        }
                        else
                        {
                            Log.Warning($"[KNX-{_config.Id}] Timed out waiting for SESSION_RESPONSE. No data within 2s.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KNX-{_config.Id}] Secure handshake attempt {attempts} failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    try { await Task.Delay(1000, ct); }
                    catch (TaskCanceledException) { break; }
                }

                Log.Error($"[KNX-{_config.Id}] Failed to establish secure KNX tunnel after 3 attempts.");
                return false;
            }
            else
            {
                // Prepare CONNECT_REQUEST (26 bytes)
                byte[] req = new byte[26];
                // Header
                req[0] = 0x06; req[1] = 0x10; // Header length & KNXnet/IP version
                req[2] = 0x02; req[3] = 0x05; // CONNECT_REQUEST type
                req[4] = 0x00; req[5] = 0x1A; // Total length: 26

                // HPAI Control Endpoint (route-back in NAT mode)
                WriteHpai(req, 6);

                // HPAI Data Endpoint (route-back in NAT mode)
                WriteHpai(req, 14);

                // CRI (Connection Request Information)
                req[22] = 0x04; // CRI Structure Length
                req[23] = 0x04; // Tunneling connection
                req[24] = 0x02; // KNX Link Layer
                req[25] = 0x00; // Reserved

                Log.Debug($"[KNX-{_config.Id}] CONNECT_REQUEST payload ({req.Length} bytes): {BitConverter.ToString(req)}");
                Log.Debug($"[KNX-{_config.Id}] Target gateway endpoint: {_gatewayEndPoint}, local HPAI advertised: {(_natMode ? "0.0.0.0:0 (NAT route-back)" : $"{_localIp}:{_localPort}")}");

                int attempts = 0;
                while (attempts++ < 3 && !ct.IsCancellationRequested)
                {
                    try
                    {
                        Log.Info($"[KNX-{_config.Id}] Sending CONNECT_REQUEST (attempt {attempts}) to {_gatewayEndPoint}...");
                        int sent = await _udpClient!.SendAsync(req, req.Length, _gatewayEndPoint);
                        Log.Debug($"[KNX-{_config.Id}] Sent {sent} bytes. Waiting up to 2s for CONNECT_RESPONSE...");

                        // Wait up to 2 seconds for CONNECT_RESPONSE
                        var receiveTask = _udpClient.ReceiveAsync(ct).AsTask();
                        var delayTask = Task.Delay(2000, ct);
                        var completedTask = await Task.WhenAny(receiveTask, delayTask);

                        if (completedTask == receiveTask)
                        {
                            var result = await receiveTask;
                            byte[] res = result.Buffer;
                            Log.Debug($"[KNX-{_config.Id}] Received {res.Length} bytes from {result.RemoteEndPoint}: {BitConverter.ToString(res)}");

                            if (res.Length >= 20 && res[2] == 0x02 && res[3] == 0x06) // CONNECT_RESPONSE
                            {
                                // CONNECT_RESPONSE body: communication_channel_id (res[6]),
                                // status (res[7]), then HPAI + CRD.
                                byte channelId = res[6];
                                byte status = res[7];
                                if (status == 0x00) // Success
                                {
                                    _channelId = channelId;
                                    _sendSeqNum = 0;
                                    lock (_stateLock)
                                    {
                                        _connected = true;
                                    }
                                    Log.Info($"[KNX-{_config.Id}] Tunnel established successfully. Channel ID = {_channelId}");
                                    return true;
                                }
                                else
                                {
                                    Log.Error($"[KNX-{_config.Id}] Gateway rejected connection request. Status code = 0x{status:X2}");
                                    return false;
                                }
                            }
                            else
                            {
                                Log.Warning($"[KNX-{_config.Id}] Unexpected response (not CONNECT_RESPONSE). ServiceType=0x{(res.Length >= 4 ? (res[2] << 8 | res[3]).ToString("X4") : "????")}, len={res.Length}.");
                            }
                        }
                        else
                        {
                            Log.Warning($"[KNX-{_config.Id}] Timed out waiting for CONNECT_RESPONSE (attempt {attempts}). No data received within 2s.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[KNX-{_config.Id}] CONNECT_REQUEST attempt {attempts} failed: {ex.GetType().Name}: {ex.Message}");
                    }

                    try { await Task.Delay(1000, ct); }
                    catch (TaskCanceledException) { break; }
                }

                Log.Error($"[KNX-{_config.Id}] Failed to establish KNX tunnel after 3 attempts. Gateway={_gatewayEndPoint}, Local={_localIp}:{_localPort}");
                return false;
            }
        }

        /// <summary>
        /// Writes an 8-byte KNXnet/IP HPAI structure into <paramref name="buffer"/>
        /// at <paramref name="offset"/>. In NAT mode (<see cref="_natMode"/>) the
        /// endpoint is written in route-back form (protocol=UDP, IP=0.0.0.0, port=0)
        /// so the gateway replies to the actual UDP source address — required when the
        /// gateway is reached across a router/NAT/firewall. Otherwise the host's real
        /// local IP and bound port are advertised (flat-LAN behaviour).
        /// </summary>
        private void WriteHpai(byte[] buffer, int offset)
        {
            buffer[offset] = 0x08;     // structure length
            buffer[offset + 1] = 0x01; // host protocol = UDP/IPv4

            if (_natMode)
            {
                // IP (4) + port (2) = 0 → gateway must use the UDP source endpoint.
                buffer[offset + 2] = 0; buffer[offset + 3] = 0;
                buffer[offset + 4] = 0; buffer[offset + 5] = 0;
                buffer[offset + 6] = 0; buffer[offset + 7] = 0;
            }
            else
            {
                byte[] ipBytes = _localIp!.GetAddressBytes();
                Array.Copy(ipBytes, 0, buffer, offset + 2, 4);
                buffer[offset + 6] = (byte)((_localPort >> 8) & 0xFF);
                buffer[offset + 7] = (byte)(_localPort & 0xFF);
            }
        }

        private byte[] EncryptFrame(byte[] plaintext)
        {
            if (_sessionKey == null) throw new InvalidOperationException("Session key not derived.");

            ulong counter = _sendSecureCounter++;
            byte[] nonce = new byte[12];
            nonce[0] = (byte)((_secureSessionId >> 8) & 0xFF);
            nonce[1] = (byte)(_secureSessionId & 0xFF);
            nonce[2] = (byte)((counter >> 40) & 0xFF);
            nonce[3] = (byte)((counter >> 32) & 0xFF);
            nonce[4] = (byte)((counter >> 24) & 0xFF);
            nonce[5] = (byte)((counter >> 16) & 0xFF);
            nonce[6] = (byte)((counter >> 8) & 0xFF);
            nonce[7] = (byte)(counter & 0xFF);

            int totalLen = 6 + 2 + 6 + plaintext.Length + 16;
            byte[] pkt = new byte[totalLen];

            pkt[0] = 0x06; pkt[1] = 0x10;
            pkt[2] = 0x09; pkt[3] = 0x50;
            pkt[4] = (byte)((totalLen >> 8) & 0xFF);
            pkt[5] = (byte)(totalLen & 0xFF);

            pkt[6] = (byte)((_secureSessionId >> 8) & 0xFF);
            pkt[7] = (byte)(_secureSessionId & 0xFF);

            pkt[8] = (byte)((counter >> 40) & 0xFF);
            pkt[9] = (byte)((counter >> 32) & 0xFF);
            pkt[10] = (byte)((counter >> 24) & 0xFF);
            pkt[11] = (byte)((counter >> 16) & 0xFF);
            pkt[12] = (byte)((counter >> 8) & 0xFF);
            pkt[13] = (byte)(counter & 0xFF);

            byte[] associatedData = new byte[14];
            Array.Copy(pkt, 0, associatedData, 0, 14);

            byte[] ciphertext = new byte[plaintext.Length];
            byte[] mac = new byte[16];

            using (var aesCcm = new System.Security.Cryptography.AesCcm(_sessionKey))
            {
                aesCcm.Encrypt(nonce, plaintext, ciphertext, mac, associatedData);
            }

            Array.Copy(ciphertext, 0, pkt, 14, ciphertext.Length);
            Array.Copy(mac, 0, pkt, 14 + ciphertext.Length, 16);

            Log.Debug($"[KNX-{_config.Id}] EncryptFrame: ssid={_secureSessionId}, counter={counter}, plaintextLen={plaintext.Length}, nonce={BitConverter.ToString(nonce)}, sessionKey={BitConverter.ToString(_sessionKey)}");

            return pkt;
        }

        private byte[]? DecryptFrame(byte[] wrapperFrame)
        {
            if (_sessionKey == null)
            {
                Log.Warning($"[KNX-{_config.Id}] DecryptFrame: session key is null.");
                return null;
            }
            if (wrapperFrame.Length < 30)
            {
                Log.Warning($"[KNX-{_config.Id}] DecryptFrame: frame too short ({wrapperFrame.Length} bytes, need >= 30).");
                return null;
            }

            ushort serviceType = (ushort)((wrapperFrame[2] << 8) | wrapperFrame[3]);
            if (serviceType != 0x0950)
            {
                Log.Warning($"[KNX-{_config.Id}] DecryptFrame: not a SECURE_WRAPPER (serviceType=0x{serviceType:X4}).");
                return null;
            }

            ushort ssid = (ushort)((wrapperFrame[6] << 8) | wrapperFrame[7]);
            ulong counter = 0;
            counter |= (ulong)wrapperFrame[8] << 40;
            counter |= (ulong)wrapperFrame[9] << 32;
            counter |= (ulong)wrapperFrame[10] << 24;
            counter |= (ulong)wrapperFrame[11] << 16;
            counter |= (ulong)wrapperFrame[12] << 8;
            counter |= wrapperFrame[13];

            Log.Debug($"[KNX-{_config.Id}] DecryptFrame: ssid={ssid}, counter={counter}, lastRecv={_recvSecureCounter}, frameLen={wrapperFrame.Length}");

            if (counter <= _recvSecureCounter)
            {
                Log.Warning($"[KNX-{_config.Id}] Replay attack or out of order counter detected. Received {counter}, expected > {_recvSecureCounter}");
                return null;
            }
            _recvSecureCounter = counter;

            byte[] nonce = new byte[12];
            nonce[0] = (byte)((ssid >> 8) & 0xFF);
            nonce[1] = (byte)(ssid & 0xFF);
            nonce[2] = (byte)((counter >> 40) & 0xFF);
            nonce[3] = (byte)((counter >> 32) & 0xFF);
            nonce[4] = (byte)((counter >> 24) & 0xFF);
            nonce[5] = (byte)((counter >> 16) & 0xFF);
            nonce[6] = (byte)((counter >> 8) & 0xFF);
            nonce[7] = (byte)(counter & 0xFF);

            int ciphertextLen = wrapperFrame.Length - 14 - 16;
            byte[] ciphertext = new byte[ciphertextLen];
            Array.Copy(wrapperFrame, 14, ciphertext, 0, ciphertextLen);

            byte[] mac = new byte[16];
            Array.Copy(wrapperFrame, wrapperFrame.Length - 16, mac, 0, 16);

            byte[] associatedData = new byte[14];
            Array.Copy(wrapperFrame, 0, associatedData, 0, 14);

            byte[] plaintext = new byte[ciphertextLen];

            try
            {
                using (var aesCcm = new System.Security.Cryptography.AesCcm(_sessionKey))
                {
                    aesCcm.Decrypt(nonce, ciphertext, mac, plaintext, associatedData);
                }
                Log.Debug($"[KNX-{_config.Id}] DecryptFrame: success. plaintextLen={plaintext.Length}, nonce={BitConverter.ToString(nonce)}, mac={BitConverter.ToString(mac)}");
                return plaintext;
            }
            catch (Exception ex)
            {
                Log.Error($"[KNX-{_config.Id}] Decryption failed: {ex.GetType().Name}: {ex.Message}. ssid={ssid}, counter={counter}, ciphertextLen={ciphertextLen}, nonce={BitConverter.ToString(nonce)}, mac={BitConverter.ToString(mac)}, sessionKey={BitConverter.ToString(_sessionKey)}");
                return null;
            }
        }

        private async Task HeartbeatLoop(CancellationToken ct)
        {
            Log.Debug($"[KNX-{_config.Id}] Heartbeat loop started.");
            
            // Build CONNECTIONSTATE_REQUEST (16 bytes)
            byte[] req = new byte[16];
            req[0] = 0x06; req[1] = 0x10;
            req[2] = 0x02; req[3] = 0x06; // CONNECTIONSTATE_REQUEST
            req[4] = 0x00; req[5] = 0x10; // Length=16
            // CRI info
            req[6] = _channelId;
            req[7] = 0x00; // Reserved
            // HPAI Control Endpoint (route-back in NAT mode)
            WriteHpai(req, 8);

            int missedHeartbeats = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(30000, ct); // Every 30 seconds

                    bool isConn;
                    lock (_stateLock) { isConn = _connected; }
                    if (!isConn) break;

                    UdpClient? client;
                    lock (_socketLock) { client = _udpClient; }
                    if (client == null) break;

                    Log.Debug($"[KNX-{_config.Id}] Sending CONNECTIONSTATE_REQUEST...");
                    if (_secureEnabled)
                    {
                        byte[] secureReq = EncryptFrame(req);
                        Log.Debug($"[KNX-{_config.Id}] TX {secureReq.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(secureReq)}");
                        await client.SendAsync(secureReq, secureReq.Length, _gatewayEndPoint);
                    }
                    else
                    {
                        Log.Debug($"[KNX-{_config.Id}] TX {req.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(req)}");
                        await client.SendAsync(req, req.Length, _gatewayEndPoint);
                    }

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
            UdpClient? client;
            lock (_socketLock) { client = _udpClient; }
            if (client == null) return;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    bool isConn;
                    lock (_stateLock) { isConn = _connected; }
                    if (!isConn) break;

                    var result = await client.ReceiveAsync(ct);
                    byte[] data = result.Buffer;

                    Log.Debug($"[KNX-{_config.Id}] RX {data.Length} bytes from {result.RemoteEndPoint}: {BitConverter.ToString(data)}");

                    if (data.Length < 6) continue;

                    // Parse KNXnet/IP Header
                    byte headerLen = data[0];
                    if (headerLen < 6 || data[1] != 0x10) continue;

                    ushort serviceType = (ushort)((data[2] << 8) | data[3]);
                    ushort totalLen = (ushort)((data[4] << 8) | data[5]);

                    if (data.Length < totalLen) continue;

                    if (_secureEnabled && serviceType == 0x0950)
                    {
                        byte[]? decrypted = DecryptFrame(data);
                        if (decrypted == null || decrypted.Length < 6) continue;
                        
                        data = decrypted;
                        headerLen = data[0];
                        if (headerLen < 6 || data[1] != 0x10) continue;
                        serviceType = (ushort)((data[2] << 8) | data[3]);
                        totalLen = (ushort)((data[4] << 8) | data[5]);
                    }

                    if (serviceType == 0x0203) // TUNNELING_REQUEST
                    {
                        if (data.Length < 10) continue;
                        byte chan = data[7];
                        byte seq = data[8];

                        // Send TUNNELING_ACK immediately
                        byte[] ack = new byte[10];
                        ack[0] = 0x06; ack[1] = 0x10;
                        ack[2] = 0x02; ack[3] = 0x04; // TUNNELING_ACK
                        ack[4] = 0x00; ack[5] = 0x0A; // Length=10
                        ack[6] = 0x04; // Structure length
                        ack[7] = chan;
                        ack[8] = seq;
                        ack[9] = 0x00; // Status=Success

                        if (_secureEnabled)
                        {
                            byte[] secureAck = EncryptFrame(ack);
                            Log.Debug($"[KNX-{_config.Id}] TX {secureAck.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(secureAck)}");
                            await client.SendAsync(secureAck, secureAck.Length, _gatewayEndPoint);
                        }
                        else
                        {
                            Log.Debug($"[KNX-{_config.Id}] TX {ack.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(ack)}");
                            await client.SendAsync(ack, ack.Length, _gatewayEndPoint);
                        }

                        // Parse cEMI frame starting at index 10
                        ParseCemiFrame(data, 10, totalLen - 10);
                    }
                    else if (serviceType == 0x0204) // TUNNELING_ACK
                    {
                        Log.Debug($"[KNX-{_config.Id}] Received TUNNELING_ACK for seq {data[8]}");
                    }
                    else if (serviceType == 0x0207) // CONNECTIONSTATE_RESPONSE
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
                    else if (_isRouting && serviceType == 0x0530) // ROUTING_INDICATION
                    {
                        // Routing indication carries the CEMI frame starting directly at index 6
                        ParseCemiFrame(data, 6, totalLen - 6);
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

        public async Task SendGroupWrite(ushort groupAddress, byte[] data, bool isSmall)
        {
            await SendCemiFrame(groupAddress, data, isSmall, isWrite: true);
        }

        private async Task SendCemiFrame(ushort groupAddress, byte[] data, bool isSmall, bool isWrite)
        {
            UdpClient? client;
            lock (_socketLock) { client = _udpClient; }
            if (client == null) return;

            bool isConn;
            lock (_stateLock) { isConn = _connected; }
            if (!isConn) return;

            // CEMI length:
            // 2 (MsgCode, AddInfoLen) + 2 (Control) + 2 (Source) + 2 (Dest) + 1 (Len) + 2 (TPCI/APCI) + Payload (if large)
            int cemiLen = 11 + (isSmall ? 0 : data.Length);
            
            // Total length: Header + CRI (for routing: 6 header + CEMI)
            // For Tunneling: 6 header + 4 connection header + CEMI
            int totalLen = _isRouting ? (6 + cemiLen) : (10 + cemiLen);
            byte[] pkt = new byte[totalLen];

            // Header
            pkt[0] = 0x06; pkt[1] = 0x10;
            pkt[2] = (byte)(_isRouting ? 0x05 : 0x02);
            pkt[3] = (byte)(_isRouting ? 0x30 : 0x03); // Routing Indication (0x0530) or Tunneling Request (0x0203)
            pkt[4] = (byte)((totalLen >> 8) & 0xFF);
            pkt[5] = (byte)(totalLen & 0xFF);

            int cemiOffset = 6;

            if (!_isRouting)
            {
                // Connection Header (4 bytes)
                pkt[6] = 0x04;
                pkt[7] = _channelId;
                pkt[8] = _sendSeqNum++;
                pkt[9] = 0x00;
                cemiOffset = 10;
            }

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

            try
            {
                Log.Debug($"[KNX-{_config.Id}] Sending group packet to {FormatGroupAddress(groupAddress)}...");
                if (_secureEnabled)
                {
                    byte[] securePkt = EncryptFrame(pkt);
                    Log.Debug($"[KNX-{_config.Id}] TX {securePkt.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(securePkt)}");
                    await client.SendAsync(securePkt, securePkt.Length, _gatewayEndPoint);
                }
                else
                {
                    Log.Debug($"[KNX-{_config.Id}] TX {pkt.Length} bytes to {_gatewayEndPoint}: {BitConverter.ToString(pkt)}");
                    await client.SendAsync(pkt, pkt.Length, _gatewayEndPoint);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[KNX-{_config.Id}] Failed to send KNX telegram: {ex.Message}");
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
                if (_udpClient != null)
                {
                    try { _udpClient.Close(); } catch { }
                    _udpClient = null;
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

            if (_connected && !_isRouting && _udpClient != null && _gatewayEndPoint != null)
            {
                // Send DISCONNECT_REQUEST (16 bytes)
                byte[] dis = new byte[16];
                dis[0] = 0x06; dis[1] = 0x10;
                dis[2] = 0x02; dis[3] = 0x09; // DISCONNECT_REQUEST
                dis[4] = 0x00; dis[5] = 0x10;
                dis[6] = _channelId;
                dis[7] = 0x00;
                // local UDP HPAI (route-back in NAT mode)
                WriteHpai(dis, 8);

                try
                {
                    if (_secureEnabled)
                    {
                        byte[] secureDis = EncryptFrame(dis);
                        Log.Debug($"[KNX-{_config.Id}] TX {secureDis.Length} bytes to {_gatewayEndPoint} (DISCONNECT): {BitConverter.ToString(secureDis)}");
                        _udpClient.Send(secureDis, secureDis.Length, _gatewayEndPoint);
                    }
                    else
                    {
                        Log.Debug($"[KNX-{_config.Id}] TX {dis.Length} bytes to {_gatewayEndPoint} (DISCONNECT): {BitConverter.ToString(dis)}");
                        _udpClient.Send(dis, dis.Length, _gatewayEndPoint);
                    }
                }
                catch { }
            }

            CleanupSession();
            _cts?.Dispose();
        }
    }
}
