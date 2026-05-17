// ModbusConnection.cs – Shared Modbus TCP transport layer
using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using NModbus;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    /// <summary>
    /// Provides connection-scoped access to a Modbus TCP master.
    /// Uses a persistent connection pool (one TCP socket per connection ID)
    /// to avoid TCP port exhaustion from TIME_WAIT accumulation.
    /// Auto-reconnects on socket failure with configurable timeout.
    /// </summary>
    public static class ModbusConnection
    {
        private static readonly ConcurrentDictionary<string, (TcpClient Tcp, IModbusMaster Master)> _pool = new();
        private static readonly ConcurrentDictionary<string, object> _connLocks = new();
        private static readonly ConcurrentDictionary<string, DateTime> _lastConnectAttempt = new();
        private static readonly ConcurrentDictionary<string, int> _consecutiveFailures = new();
        private static readonly object _lock = new();

        /// <summary>Minimum interval between reconnect attempts to the same connection.</summary>
        private static readonly TimeSpan BaseReconnectCooldown = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MaxReconnectCooldown = TimeSpan.FromMinutes(5);

        /// <summary>TCP connect timeout in milliseconds.</summary>
        private const int ConnectTimeoutMs = 10_000;

        public static T WithMaster<T>(ConnectionConfig conn, Func<IModbusMaster, T> action)
        {
            var connLock = _connLocks.GetOrAdd(conn.Id, _ => new object());
            lock (connLock)
            {
                IModbusMaster master;
                bool wasFreshConnection = false;
                try
                {
                    var result = GetOrCreateMaster(conn);
                    master = result.Master;
                    wasFreshConnection = result.IsNew;
                }
                catch (Exception)
                {
                    // Connection establishment failed — log and re-throw.
                    // The error is already logged inside GetOrCreateMaster if it's the first one.
                    throw;
                }

                try
                {
                    return action(master);
                }
                catch (Exception ex) when (IsTransportError(ex))
                {
                    // Connection died during action.
                    Log.Warning($"[Modbus] Connection '{conn.Id}' transport error: {ex.GetType().Name}: {ex.Message}. Reconnecting…");
                    
                    // Mark as dead and purge.
                    FullReset(conn.Id);

                    // If it was already a fresh connection that died during the action,
                    // we don't retry immediately to avoid a loop. If it was a pooled
                    // connection, we try exactly once more.
                    if (wasFreshConnection)
                    {
                        throw;
                    }

                    try
                    {
                        var result = GetOrCreateMaster(conn);
                        return action(result.Master);
                    }
                    catch (Exception retryEx)
                    {
                        Log.Error($"[Modbus] Connection '{conn.Id}' retry failed: {retryEx.GetType().Name}: {retryEx.Message}");
                        throw;
                    }
                }
            }
        }

        private struct MasterResult
        {
            public IModbusMaster Master;
            public bool IsNew;
        }

        private static MasterResult GetOrCreateMaster(ConnectionConfig conn)
        {
            string key = conn.Id;
            if (_pool.TryGetValue(key, out var entry) && IsAlive(entry.Tcp))
                return new MasterResult { Master = entry.Master, IsNew = false };
            
            if (entry.Tcp != null) Log.Debug($"[Modbus] Pooled connection for '{key}' is dead. Reconnecting…");

            // Calculate backoff based on consecutive failures
            _consecutiveFailures.TryGetValue(key, out int failCount);
            var cooldown = TimeSpan.FromTicks(Math.Min(
                MaxReconnectCooldown.Ticks,
                BaseReconnectCooldown.Ticks * (long)Math.Pow(2, Math.Min(failCount, 6)) // exponential up to ~10.6 mins, but capped at 5
            ));

            // Cooldown check — don't hammer a dead gateway
            if (_lastConnectAttempt.TryGetValue(key, out var lastAttempt) &&
                DateTime.UtcNow - lastAttempt < cooldown)
            {
                Log.Debug($"[Modbus] Connection '{key}' in backoff cooldown ({cooldown.TotalSeconds:F1}s). Skipping attempt.");
                throw new SocketException((int)SocketError.HostUnreachable);
            }

            // Dispose old if present
            PurgeConnection(key);
            _lastConnectAttempt[key] = DateTime.UtcNow;

            string address = conn.Address
                ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing address.");
            int port = conn.Port
                ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing port.");

            var tcp = new TcpClient();
            try
            {
                // Use async connect with timeout
                var connectTask = tcp.ConnectAsync(address, port);
                if (!connectTask.Wait(ConnectTimeoutMs))
                {
                    tcp.Dispose();
                    throw new TimeoutException(
                        $"TCP connect to {address}:{port} timed out after {ConnectTimeoutMs}ms.");
                }

                if (connectTask.IsFaulted)
                {
                    tcp.Dispose();
                    throw connectTask.Exception?.InnerException
                        ?? new SocketException((int)SocketError.ConnectionRefused);
                }

                tcp.ReceiveTimeout = 5_000;
                tcp.SendTimeout = 5_000;

                var master = new ModbusFactory().CreateMaster(tcp);
                master.Transport.ReadTimeout = 3_000;
                master.Transport.WriteTimeout = 3_000;
                master.Transport.Retries = 1;

                _pool[key] = (tcp, master);
                _lastConnectAttempt.TryRemove(key, out _);
                _consecutiveFailures.TryRemove(key, out _); // Reset failures on success
                
                Log.Info($"[Modbus] Connected to '{conn.Id}' ({address}:{port}).");
                return new MasterResult { Master = master, IsNew = true };
            }
            catch (Exception ex)
            {
                tcp.Dispose();
                _consecutiveFailures.AddOrUpdate(key, 1, (_, c) => c + 1);
                Log.Error($"[Modbus] Connection '{conn.Id}' ({address}:{port}) establishment failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks whether a transport error should trigger reconnection.
        /// Catches all common .NET socket/stream failure modes.
        /// </summary>
        private static bool IsTransportError(Exception ex) =>
            ex is SocketException
            or System.IO.IOException
            or ObjectDisposedException
            or InvalidOperationException
            or TimeoutException
            or NModbus.SlaveException;

        /// <summary>
        /// Checks if a TcpClient is still actually alive.
        /// TcpClient.Connected only reflects the last known state —
        /// this probes the socket for real connectivity.
        /// </summary>
        private static bool IsAlive(TcpClient tcp)
        {
            try
            {
                if (!tcp.Connected) return false;
                var socket = tcp.Client;
                if (socket == null) return false;
                // Poll returns true if: (a) data available, (b) connection closed, (c) error
                // If poll returns true but Available == 0, the connection was closed by the remote end
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Dispose and remove a pooled connection.</summary>
        public static void PurgeConnection(string connId)
        {
            if (_pool.TryRemove(connId, out var entry))
            {
                Log.Debug($"[Modbus] Purging pooled connection '{connId}'.");
                try { entry.Master?.Dispose(); } catch { }
                try { entry.Tcp?.Dispose(); } catch { }
            }
        }

        /// <summary>Resets the reconnect cooldown for a connection, allowing immediate reconnection.</summary>
        public static void ResetCooldown(string connId)
        {
            _lastConnectAttempt.TryRemove(connId, out _);
            _consecutiveFailures.TryRemove(connId, out _);
        }

        /// <summary>
        /// Purges the pooled connection AND resets the cooldown in one atomic operation.
        /// Use this when a connection is known-dead and should be retried immediately.
        /// </summary>
        public static void FullReset(string connId)
        {
            PurgeConnection(connId);
            ResetCooldown(connId);
        }

        /// <summary>Returns the number of active pooled connections.</summary>
        public static int ActiveConnectionCount => _pool.Count;

        /// <summary>Reads two 16-bit registers and converts them to a big-endian float32.</summary>
        public static float ReadFloat32(IModbusMaster master, byte slaveId, ushort address, bool input = false)
        {
            var regs = input ? master.ReadInputRegisters(slaveId, address, 2) : master.ReadHoldingRegisters(slaveId, address, 2);
            return RegsToFloat(regs, 0);
        }

        public static float RegsToFloat(ushort[] regs, int offset)
        {
            var b = new byte[]
            {
                (byte)(regs[offset]     >> 8), (byte)(regs[offset]     & 0xFF),
                (byte)(regs[offset + 1] >> 8), (byte)(regs[offset + 1] & 0xFF),
            };
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }

        public static int RegsToInt32(ushort[] regs, int offset)
        {
            var b = new byte[]
            {
                (byte)(regs[offset]     >> 8), (byte)(regs[offset]     & 0xFF),
                (byte)(regs[offset + 1] >> 8), (byte)(regs[offset + 1] & 0xFF),
            };
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToInt32(b, 0);
        }

        public static ulong RegsToUInt64(ushort[] regs, int offset)
        {
            var b = new byte[]
            {
                (byte)(regs[offset]     >> 8), (byte)(regs[offset]     & 0xFF),
                (byte)(regs[offset + 1] >> 8), (byte)(regs[offset + 1] & 0xFF),
                (byte)(regs[offset + 2] >> 8), (byte)(regs[offset + 2] & 0xFF),
                (byte)(regs[offset + 3] >> 8), (byte)(regs[offset + 3] & 0xFF),
            };
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToUInt64(b, 0);
        }

        public static uint ReadUInt32(IModbusMaster master, byte slaveId, ushort address, bool swapped = false, bool input = false)
        {
            var regs = input ? master.ReadInputRegisters(slaveId, address, 2) : master.ReadHoldingRegisters(slaveId, address, 2);
            ushort w1 = swapped ? regs[1] : regs[0];
            ushort w2 = swapped ? regs[0] : regs[1];
            return (uint)((w1 << 16) | w2);
        }

        public static int ReadInt32(IModbusMaster master, byte slaveId, ushort address, bool swapped = false, bool input = false)
        {
            var regs = input ? master.ReadInputRegisters(slaveId, address, 2) : master.ReadHoldingRegisters(slaveId, address, 2);
            ushort w1 = swapped ? regs[1] : regs[0];
            ushort w2 = swapped ? regs[0] : regs[1];
            return (int)((w1 << 16) | w2);
        }

        public static ushort ReadUInt16(IModbusMaster master, byte slaveId, ushort address, bool input = false)
        {
            var regs = input ? master.ReadInputRegisters(slaveId, address, 1) : master.ReadHoldingRegisters(slaveId, address, 1);
            return regs[0];
        }

        public static ulong ReadUInt64(IModbusMaster master, byte slaveId, ushort address, bool input = false)
        {
            var regs = input ? master.ReadInputRegisters(slaveId, address, 4) : master.ReadHoldingRegisters(slaveId, address, 4);
            return RegsToUInt64(regs, 0);
        }

        /// <summary>Writes a float32 value to two contiguous holding registers (big-endian).</summary>
        public static void WriteFloat32(IModbusMaster master, byte slaveId, ushort address, float value)
        {
            var b = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            var regs = new ushort[]
            {
                (ushort)((b[0] << 8) | b[1]),
                (ushort)((b[2] << 8) | b[3]),
            };
            master.WriteMultipleRegisters(slaveId, address, regs);
        }

        public static void WriteUInt16(IModbusMaster master, byte slaveId, ushort address, ushort value)
        {
            master.WriteSingleRegister(slaveId, address, value);
        }

        public static void WriteUInt32(IModbusMaster master, byte slaveId, ushort address, uint value, bool swapped = false)
        {
            ushort w1 = (ushort)(value >> 16);
            ushort w2 = (ushort)(value & 0xFFFF);
            var regs = swapped ? new ushort[] { w2, w1 } : new ushort[] { w1, w2 };
            master.WriteMultipleRegisters(slaveId, address, regs);
        }
    }
}
