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
    /// Auto-reconnects on socket failure.
    /// </summary>
    public static class ModbusConnection
    {
        private static readonly ConcurrentDictionary<string, (TcpClient Tcp, IModbusMaster Master)> _pool = new();
        private static readonly ConcurrentDictionary<string, object> _connLocks = new();
        private static readonly object _lock = new();

        public static T WithMaster<T>(ConnectionConfig conn, Func<IModbusMaster, T> action)
        {
            // Per-connection lock — NModbus masters are NOT thread-safe;
            // concurrent reads on the same TCP stream corrupt the framing.
            var connLock = _connLocks.GetOrAdd(conn.Id, _ => new object());
            lock (connLock)
            {
                var master = GetOrCreateMaster(conn);
                try
                {
                    return action(master);
                }
                catch (Exception ex) when (ex is SocketException or System.IO.IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // Connection died — purge from pool and retry once with a fresh connection
                    PurgeConnection(conn.Id);
                    master = GetOrCreateMaster(conn);
                    return action(master);
                }
            }
        }

        private static IModbusMaster GetOrCreateMaster(ConnectionConfig conn)
        {
            string key = conn.Id;
            if (_pool.TryGetValue(key, out var entry) && entry.Tcp.Connected)
                return entry.Master;

            lock (_lock)
            {
                // Double-check after acquiring lock
                if (_pool.TryGetValue(key, out entry) && entry.Tcp.Connected)
                    return entry.Master;

                // Dispose old if present
                PurgeConnection(key);

                var tcp = new TcpClient();
                tcp.Connect(
                    conn.Address ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing address."),
                    conn.Port ?? throw new InvalidOperationException($"Connection '{conn.Id}' is missing port."));
                var master = new ModbusFactory().CreateMaster(tcp);

                _pool[key] = (tcp, master);
                return master;
            }
        }

        /// <summary>Dispose and remove a pooled connection.</summary>
        public static void PurgeConnection(string connId)
        {
            if (_pool.TryRemove(connId, out var entry))
            {
                try { entry.Master?.Dispose(); } catch { }
                try { entry.Tcp?.Dispose(); } catch { }
            }
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
