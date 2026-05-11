// ModbusConnection.cs – Shared Modbus TCP transport layer
using System;
using System.Net.Sockets;
using NModbus;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    /// <summary>
    /// Provides connection-scoped access to a Modbus TCP master.
    /// Opens a short-lived TCP connection per call. For high-frequency polling
    /// consider pooling or long-lived connections in a future iteration.
    /// </summary>
    public static class ModbusConnection
    {
        public static T WithMaster<T>(ConnectionConfig conn, Func<IModbusMaster, T> action)
        {
            using var tcp = new TcpClient();
            tcp.Connect(conn.Host, conn.Port);
            using var master = new ModbusFactory().CreateMaster(tcp);
            return action(master);
        }

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
