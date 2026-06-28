using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Security;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Knx
{
    /// <summary>
    /// A KNXnet/IP tunnelling session over <b>TCP</b>.
    ///
    /// <para>The session supports both transport variants over the same TCP socket:</para>
    /// <list type="bullet">
    ///   <item><b>Plain tunnelling</b> — KNXnet/IP frames are sent/received verbatim.</item>
    ///   <item><b>KNX IP Secure</b> (KNX AN159) — the session performs the Diffie-Hellman
    ///   handshake (SESSION_REQUEST / SESSION_RESPONSE / SESSION_AUTHENTICATE), derives the
    ///   session key, and transparently wraps every outgoing frame in a SECURE_WRAPPER
    ///   (0x0950) and unwraps every incoming one.</item>
    /// </list>
    ///
    /// <para>Either way callers work purely with plain KNXnet/IP frames via
    /// <see cref="SendFrameAsync"/> and <see cref="FrameReceived"/> and never need to know
    /// about the transport details. Frames are length-prefixed by the KNXnet/IP header
    /// (bytes 4..5), which we use to reassemble messages from the stream.</para>
    /// </summary>
    public sealed class KnxTcpSession : IDisposable
    {
        // KNXnet/IP service type identifiers used by the secure handshake.
        private const ushort SESSION_REQUEST = 0x0951;
        private const ushort SESSION_RESPONSE = 0x0952;
        private const ushort SESSION_AUTHENTICATE = 0x0953;
        private const ushort SESSION_STATUS = 0x0954;
        private const ushort SECURE_WRAPPER = 0x0950;

        // Fixed values for a tunnelling session (per KNX spec / xknx reference).
        private static readonly byte[] MessageTag = { 0x00, 0x00 };
        // The serial number is informational for unicast secure sessions; any unique
        // 6-byte value works. We use a fixed Pulswerk identifier.
        private static readonly byte[] SerialNumber = { 0x00, 0xFA, 0x50, 0x55, 0x4C, 0x53 };

        // KNX secure tunnel slot. We always use the management slot (user id 1), whose
        // password is the commissioning password printed on the device.
        private const int UserId = 1;

        private readonly string _logId;
        private readonly IPEndPoint _gateway;
        private readonly bool _secure;
        private readonly byte[]? _deviceAuthCode;   // PBKDF2(commissioning password) — secure only
        private readonly byte[]? _userPasswordHash;  // PBKDF2(commissioning password) — secure only

        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private byte[]? _sessionKey;
        private ushort _sessionId;
        private long _txSequence;                   // outgoing SecureWrapper sequence counter
        private long _rxSequence = -1;              // last accepted incoming sequence
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private volatile bool _connected;

        /// <summary>Raised with each (decrypted) plain KNXnet/IP frame received from the gateway.</summary>
        public event Action<byte[]>? FrameReceived;

        /// <summary>Raised when the session/transport drops.</summary>
        public event Action? Closed;

        public bool IsConnected => _connected;

        public KnxTcpSession(string logId, IPEndPoint gateway, ConnectionConfig config)
        {
            _logId = logId;
            _gateway = gateway;
            _secure = config.KnxSecureEnabled;

            if (_secure)
            {
                // The commissioning password / device authentication code (FDSK) printed on the
                // device is the only credential: it is both the device authentication code and the
                // user (management slot) password.
                //
                // The code is printed as hyphen-separated groups over several lines, e.g.
                //   ABCDE-ABCDE
                //   ABCDE-ABCDE
                // and must be hashed exactly as printed (hyphens included). We only strip
                // surrounding whitespace and any stray line breaks the user may have pasted —
                // the hyphens and the groups themselves are preserved untouched.
                string commissioning = NormalizeCommissioningPassword(config.KnxCommissioningPassword);
                _deviceAuthCode = KnxSecureCrypto.DeriveDeviceAuthenticationCode(commissioning);
                _userPasswordHash = KnxSecureCrypto.DeriveUserPasswordHash(commissioning);
            }
        }

        /// <summary>
        /// Connects the TCP transport and, for secure sessions, performs the handshake.
        /// Returns true on success. On success the read loop is running and frames are
        /// delivered via <see cref="FrameReceived"/>.
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken ct)
        {
            try
            {
                _tcp = new TcpClient { NoDelay = true };
                await _tcp.ConnectAsync(_gateway.Address, _gateway.Port, ct);
                _stream = _tcp.GetStream();

                if (_secure)
                {
                    Log.Info($"[KNX-{_logId}] Secure TCP connected to {_gateway}. Starting handshake...");
                    if (!await HandshakeAsync(ct))
                    {
                        Log.Error($"[KNX-{_logId}] Secure handshake did not complete; closing connection (will retry).");
                        Close();
                        return false;
                    }
                }
                else
                {
                    Log.Info($"[KNX-{_logId}] TCP connected to {_gateway} (plain tunnelling).");
                }

                _connected = true;
                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _readTask = Task.Run(() => ReadLoopAsync(_cts.Token));
                Log.Info($"[KNX-{_logId}] TCP session established{(_secure ? $" (secure, SSID={_sessionId})" : "")}.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[KNX-{_logId}] TCP connect failed: {ex.GetType().Name}: {ex.Message}");
                Close();
                return false;
            }
        }

        // ── Handshake ────────────────────────────────────────────────────────────
        private async Task<bool> HandshakeAsync(CancellationToken ct)
        {
            Log.Debug($"[KNX-{_logId}] Secure handshake: starting (user id {UserId}).");

            // 1. Generate ephemeral X25519 keypair.
            byte[] clientPriv = new byte[32];
            byte[] clientPub = new byte[32];
            new SecureRandom().NextBytes(clientPriv);
            X25519.GeneratePublicKey(clientPriv, 0, clientPub, 0);
            Log.Debug($"[KNX-{_logId}] Secure handshake step 1/4: generated ephemeral X25519 key pair.");

            // 2. SESSION_REQUEST: header + HPAI(TCP route-back) + client public key.
            byte[] req = new byte[6 + 8 + 32];
            WriteHeader(req, 0, SESSION_REQUEST, req.Length);
            // HPAI for TCP is the route-back form: length 0x08, protocol 0x02 (TCP), all zeros.
            req[6] = 0x08; req[7] = 0x02;
            Array.Copy(clientPub, 0, req, 14, 32);
            await WriteRawAsync(req, ct);
            Log.Debug($"[KNX-{_logId}] Secure handshake step 2/4: SESSION_REQUEST sent; waiting for SESSION_RESPONSE...");

            // 3. SESSION_RESPONSE: SSID(2) + server public key(32) + MAC(16).
            byte[]? resp = await ReadRawFrameAsync(ct);
            if (resp == null || ReadServiceType(resp) != SESSION_RESPONSE || resp.Length < 6 + 50)
            {
                Log.Error($"[KNX-{_logId}] Secure handshake FAILED: expected SESSION_RESPONSE, got {(resp == null ? "nothing (connection closed/timeout)" : $"0x{ReadServiceType(resp):X4}, {resp.Length} bytes")}.");
                return false;
            }

            _sessionId = (ushort)((resp[6] << 8) | resp[7]);
            byte[] serverPub = new byte[32];
            Array.Copy(resp, 8, serverPub, 0, 32);
            byte[] serverMac = new byte[16];
            Array.Copy(resp, 40, serverMac, 0, 16);
            Log.Debug($"[KNX-{_logId}] Secure handshake step 3/4: SESSION_RESPONSE received (SSID={_sessionId}); verifying device authentication...");

            byte[] pubXor = KnxSecureCrypto.Xor(clientPub, serverPub);

            // 4. Verify SESSION_RESPONSE MAC using the device authentication code.
            //    additional_data = header(06 10 09 52 00 38) + SSID + XOR(pubkeys)
            byte[] respHeader = { 0x06, 0x10, 0x09, 0x52, 0x00, 0x38 };
            byte[] respAd = Concat(respHeader, new[] { (byte)(_sessionId >> 8), (byte)(_sessionId & 0xFF) }, pubXor);
            byte[] respMacCbc = KnxSecureCrypto.CalculateMacCbc(_deviceAuthCode!, respAd);
            var (_, respMacTr) = KnxSecureCrypto.Ctr(_deviceAuthCode!, KnxSecureCrypto.Counter0Handshake, Array.Empty<byte>(), serverMac);
            if (!ConstantTimeEquals(respMacCbc, respMacTr))
            {
                Log.Error($"[KNX-{_logId}] Secure handshake FAILED: SESSION_RESPONSE MAC verification failed — the commissioning password is wrong.");
                return false;
            }
            Log.Debug($"[KNX-{_logId}] Secure handshake: device authentication MAC verified (commissioning password is correct).");

            // 5. Derive the session key: SHA256(ECDH shared secret)[:16].
            byte[] shared = new byte[32];
            X25519.ScalarMult(clientPriv, 0, serverPub, 0, shared, 0);
            _sessionKey = KnxSecureCrypto.SessionKeyFromSharedSecret(shared);
            Log.Debug($"[KNX-{_logId}] Secure handshake: session key derived.");

            // 6. Build SESSION_AUTHENTICATE: reserved(0) + userId + MAC(16).
            //    MAC over additional_data = header(06 10 09 53 00 18) + reserved + userId + XOR(pubkeys),
            //    keyed with the user password hash, then CTR-encrypted.
            byte[] authHeader = { 0x06, 0x10, 0x09, 0x53, 0x00, 0x18 };
            byte[] authAd = Concat(authHeader, new byte[] { 0x00, (byte)UserId }, pubXor);
            byte[] authMacCbc = KnxSecureCrypto.CalculateMacCbc(_userPasswordHash!, authAd);
            var (_, authMac) = KnxSecureCrypto.Ctr(_userPasswordHash!, KnxSecureCrypto.Counter0Handshake, Array.Empty<byte>(), authMacCbc);

            byte[] auth = new byte[6 + 2 + 16];
            WriteHeader(auth, 0, SESSION_AUTHENTICATE, auth.Length);
            auth[6] = 0x00;            // reserved
            auth[7] = (byte)UserId;    // user id (management slot)
            Array.Copy(authMac, 0, auth, 8, 16);

            // 7. SESSION_AUTHENTICATE must be wrapped in a SECURE_WRAPPER.
            await WriteRawAsync(Wrap(auth), ct);
            Log.Debug($"[KNX-{_logId}] Secure handshake step 4/4: SESSION_AUTHENTICATE sent; waiting for SESSION_STATUS...");

            // 8. Expect a wrapped SESSION_STATUS (status 0 = authentication success).
            byte[]? statusWrapped = await ReadRawFrameAsync(ct);
            if (statusWrapped == null || ReadServiceType(statusWrapped) != SECURE_WRAPPER)
            {
                Log.Error($"[KNX-{_logId}] Secure handshake FAILED: expected wrapped SESSION_STATUS, got {(statusWrapped == null ? "nothing (connection closed/timeout)" : $"0x{ReadServiceType(statusWrapped):X4}")}.");
                return false;
            }
            byte[]? status = Unwrap(statusWrapped);
            if (status == null || ReadServiceType(status) != SESSION_STATUS)
            {
                Log.Error($"[KNX-{_logId}] Secure handshake FAILED: SESSION_STATUS could not be decrypted/verified (session key mismatch).");
                return false;
            }
            byte statusCode = status.Length >= 7 ? status[6] : (byte)0xFF;
            if (statusCode != 0x00)
            {
                Log.Error($"[KNX-{_logId}] Secure handshake FAILED: authentication rejected by gateway (status 0x{statusCode:X2}). Check the user id / commissioning password.");
                return false;
            }

            Log.Info($"[KNX-{_logId}] Secure handshake SUCCEEDED (SSID={_sessionId}, user id {UserId}).");
            return true;
        }

        // ── Public send ────────────────────────────────────────────────────────────
        /// <summary>
        /// Sends a plain KNXnet/IP frame over the TCP session. On secure sessions the frame
        /// is wrapped in a SECURE_WRAPPER first; on plain sessions it is sent verbatim.
        /// </summary>
        public async Task SendFrameAsync(byte[] plainFrame, CancellationToken ct = default)
        {
            if (!_connected || _stream == null) throw new InvalidOperationException("TCP session not connected.");
            byte[] wrapped = _secure ? Wrap(plainFrame) : plainFrame;
            await _sendLock.WaitAsync(ct);
            try
            {
                await WriteRawAsync(wrapped, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ── SecureWrapper encode/decode ──────────────────────────────────────────────
        private byte[] Wrap(byte[] plainFrame)
        {
            if (_sessionKey == null) throw new InvalidOperationException("Session key not derived.");

            long seq = Interlocked.Increment(ref _txSequence) - 1; // start at 0
            byte[] seqBytes = SixByteBE(seq);

            int totalLength = 6 + 2 + 6 + 6 + 2 + plainFrame.Length + 16;
            byte[] header = new byte[6];
            WriteHeader(header, 0, SECURE_WRAPPER, totalLength);

            byte[] ssid = { (byte)(_sessionId >> 8), (byte)(_sessionId & 0xFF) };

            // MAC: additional_data = wrapper header + SSID; block_0 = seq+serial+tag+len(plain).
            byte[] ad = Concat(header, ssid);
            byte[] block0 = Concat(seqBytes, SerialNumber, MessageTag,
                new[] { (byte)(plainFrame.Length >> 8), (byte)(plainFrame.Length & 0xFF) });
            byte[] macCbc = KnxSecureCrypto.CalculateMacCbc(_sessionKey, ad, plainFrame, block0);

            // Encrypt: counter_0 = seq+serial+tag+ff 00.
            byte[] counter0 = Concat(seqBytes, SerialNumber, MessageTag, new byte[] { 0xFF, 0x00 });
            var (encData, encMac) = KnxSecureCrypto.Ctr(_sessionKey, counter0, plainFrame, macCbc);

            byte[] pkt = new byte[totalLength];
            int pos = 0;
            Array.Copy(header, 0, pkt, pos, 6); pos += 6;
            Array.Copy(ssid, 0, pkt, pos, 2); pos += 2;
            Array.Copy(seqBytes, 0, pkt, pos, 6); pos += 6;
            Array.Copy(SerialNumber, 0, pkt, pos, 6); pos += 6;
            Array.Copy(MessageTag, 0, pkt, pos, 2); pos += 2;
            Array.Copy(encData, 0, pkt, pos, encData.Length); pos += encData.Length;
            Array.Copy(encMac, 0, pkt, pos, 16);
            return pkt;
        }

        private byte[]? Unwrap(byte[] wrapper)
        {
            if (_sessionKey == null) return null;
            if (wrapper.Length < 6 + 2 + 6 + 6 + 2 + 16) return null;
            if (ReadServiceType(wrapper) != SECURE_WRAPPER) return null;

            int pos = 6;
            byte[] ssid = { wrapper[pos], wrapper[pos + 1] }; pos += 2;
            byte[] seqBytes = Slice(wrapper, pos, 6); pos += 6;
            byte[] serial = Slice(wrapper, pos, 6); pos += 6;
            byte[] tag = Slice(wrapper, pos, 2); pos += 2;

            int encLen = wrapper.Length - pos - 16;
            if (encLen < 0) return null;
            byte[] encData = Slice(wrapper, pos, encLen); pos += encLen;
            byte[] encMac = Slice(wrapper, pos, 16);

            // Decrypt (CTR is symmetric).
            byte[] counter0 = Concat(seqBytes, serial, tag, new byte[] { 0xFF, 0x00 });
            var (plain, macTr) = KnxSecureCrypto.Ctr(_sessionKey, counter0, encData, encMac);

            // Verify MAC.
            byte[] header = Slice(wrapper, 0, 6);
            byte[] ad = Concat(header, ssid);
            byte[] block0 = Concat(seqBytes, serial, tag,
                new[] { (byte)(plain.Length >> 8), (byte)(plain.Length & 0xFF) });
            byte[] macCbc = KnxSecureCrypto.CalculateMacCbc(_sessionKey, ad, plain, block0);
            if (!ConstantTimeEquals(macCbc, macTr))
            {
                Log.Warning($"[KNX-{_logId}] SecureWrapper MAC verification failed; dropping frame.");
                return null;
            }

            // Replay protection on the incoming counter.
            long seq = SixByteToLong(seqBytes);
            if (seq <= _rxSequence)
            {
                Log.Warning($"[KNX-{_logId}] Out-of-order/replayed secure frame (seq {seq} <= {_rxSequence}); dropping.");
                return null;
            }
            _rxSequence = seq;

            return plain;
        }

        // ── TCP read loop ────────────────────────────────────────────────────────────
        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    byte[]? frame = await ReadRawFrameAsync(ct);
                    if (frame == null) break;

                    ushort serviceType = ReadServiceType(frame);
                    if (_secure)
                    {
                        // On a secure session every payload is wrapped; ignore anything else.
                        if (serviceType != SECURE_WRAPPER) continue;

                        byte[]? plain = Unwrap(frame);
                        if (plain == null) continue;

                        // SESSION_STATUS keepalive responses are consumed here.
                        if (ReadServiceType(plain) == SESSION_STATUS) continue;

                        FrameReceived?.Invoke(plain);
                    }
                    else
                    {
                        // Plain tunnelling: deliver the frame as-is.
                        FrameReceived?.Invoke(frame);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    Log.Warning($"[KNX-{_logId}] TCP read loop error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _connected = false;
                Closed?.Invoke();
            }
        }

        /// <summary>
        /// Reads exactly one KNXnet/IP frame off the TCP stream, using the 2-byte total
        /// length field (bytes 4..5 of the KNXnet/IP header) to know how much to read.
        /// </summary>
        private async Task<byte[]?> ReadRawFrameAsync(CancellationToken ct)
        {
            if (_stream == null) return null;

            byte[] header = new byte[6];
            if (!await ReadExactAsync(header, 0, 6, ct)) return null;
            if (header[0] != 0x06 || header[1] != 0x10) return null; // not a KNXnet/IP frame

            int totalLen = (header[4] << 8) | header[5];
            if (totalLen < 6 || totalLen > 0xFFFF) return null;

            byte[] frame = new byte[totalLen];
            Array.Copy(header, frame, 6);
            if (totalLen > 6 && !await ReadExactAsync(frame, 6, totalLen - 6, ct)) return null;
            return frame;
        }

        private async Task<bool> ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_stream == null) return false;
            int read = 0;
            while (read < count)
            {
                int n = await _stream.ReadAsync(buffer.AsMemory(offset + read, count - read), ct);
                if (n == 0) return false; // stream closed
                read += n;
            }
            return true;
        }

        private async Task WriteRawAsync(byte[] data, CancellationToken ct)
        {
            if (_stream == null) throw new InvalidOperationException("Secure stream not open.");
            await _stream.WriteAsync(data, ct);
            await _stream.FlushAsync(ct);
        }

        // ── Helpers ────────────────────────────────────────────────────────────────
        private static void WriteHeader(byte[] buf, int offset, ushort serviceType, int totalLength)
        {
            buf[offset] = 0x06;
            buf[offset + 1] = 0x10;
            buf[offset + 2] = (byte)(serviceType >> 8);
            buf[offset + 3] = (byte)(serviceType & 0xFF);
            buf[offset + 4] = (byte)(totalLength >> 8);
            buf[offset + 5] = (byte)(totalLength & 0xFF);
        }

        private static ushort ReadServiceType(byte[] frame)
            => frame.Length >= 4 ? (ushort)((frame[2] << 8) | frame[3]) : (ushort)0;

        private static byte[] SixByteBE(long value)
        {
            byte[] b = new byte[6];
            for (int i = 5; i >= 0; i--) { b[i] = (byte)(value & 0xFF); value >>= 8; }
            return b;
        }

        private static long SixByteToLong(byte[] b)
        {
            long v = 0;
            for (int i = 0; i < 6; i++) v = (v << 8) | b[i];
            return v;
        }

        private static byte[] Slice(byte[] src, int offset, int len)
        {
            byte[] dst = new byte[len];
            Array.Copy(src, offset, dst, 0, len);
            return dst;
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (var p in parts) len += p.Length;
            byte[] result = new byte[len];
            int pos = 0;
            foreach (var p in parts) { Array.Copy(p, 0, result, pos, p.Length); pos += p.Length; }
            return result;
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        /// <summary>
        /// Cleans up a commissioning password / device authentication code (FDSK) as it might
        /// have been pasted from the device label. The label prints it as hyphen-separated
        /// groups spread over several lines, e.g.
        /// <code>
        ///   ABCDE-ABCDE
        ///   ABCDE-ABCDE
        /// </code>
        /// which represents the single string <c>ABCDE-ABCDE-ABCDE-ABCDE</c> — i.e. a line break
        /// stands in for the hyphen between the groups. The KNX spec hashes the code <b>with</b>
        /// its hyphens, so we normalise to that canonical hyphenated single-line form:
        /// <list type="bullet">
        ///   <item>strip leading/trailing whitespace,</item>
        ///   <item>replace any run of line breaks / tabs / spaces between groups with a single
        ///   hyphen,</item>
        ///   <item>collapse a hyphen that ends up adjacent to a line-break-hyphen so we never
        ///   produce a double hyphen.</item>
        /// </list>
        /// This means the user can paste the code either as the clean single line
        /// (<c>ABCDE-ABCDE-ABCDE-ABCDE</c>) or exactly as it appears on the multi-line label and
        /// get the same result.
        /// </summary>
        private static string NormalizeCommissioningPassword(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            string trimmed = raw.Trim();
            var sb = new System.Text.StringBuilder(trimmed.Length);
            bool pendingSeparator = false; // a run of whitespace/newlines seen between groups

            foreach (char c in trimmed)
            {
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    pendingSeparator = true;
                    continue;
                }

                if (pendingSeparator)
                {
                    pendingSeparator = false;
                    // The line/whitespace break stands in for the hyphen between groups, but
                    // don't create a double hyphen if one side already has it.
                    if (sb.Length > 0 && sb[sb.Length - 1] != '-' && c != '-')
                        sb.Append('-');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>Test-only access to <see cref="NormalizeCommissioningPassword"/>.</summary>
        public static string NormalizeForTesting(string? raw) => NormalizeCommissioningPassword(raw);

        private void Close()
        {
            _connected = false;
            try { _cts?.Cancel(); } catch { }
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
            _stream = null;
            _tcp = null;
        }

        public void Dispose()
        {
            Close();
            try { _cts?.Dispose(); } catch { }
            try { _sendLock.Dispose(); } catch { }
        }
    }
}
