using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KnxSim
{
    class Program
    {
        private static UdpClient? _server;
        private static IPEndPoint? _clientEndPoint;
        private static byte _channelId = 1;
        private static byte _sendSeqNum = 0;
        private static readonly ConcurrentDictionary<ushort, byte[]> _states = new();
        private static readonly object _sendLock = new();

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== KNX IP Tunneling Simulator starting ===");

            // Initialize default values for simulated group addresses:
            // 1/1/10 (Temperature Sensor, DPT 9.001) -> default 21.5
            _states[ParseGroupAddress("1/1/10")] = EncodeDpt9(21.5);
            // 1/1/11 (Setpoint Temperature, DPT 9.001) -> default 22.0
            _states[ParseGroupAddress("1/1/11")] = EncodeDpt9(22.0);
            // 1/2/1 (Ceiling Light, DPT 1.001) -> default false
            _states[ParseGroupAddress("1/2/1")] = new byte[] { 0 };
            // 1/3/1 (Power Sensor, DPT 14.056) -> default 450.0
            _states[ParseGroupAddress("1/3/1")] = EncodeDpt14(450.0f);
            // 1/2/2 (Simulated Switch, DPT 1.001) -> default false
            _states[ParseGroupAddress("1/2/2")] = new byte[] { 0 };
            // 1/4/1 (Simulated Dimmer, DPT 5.001) -> default 128
            _states[ParseGroupAddress("1/4/1")] = new byte[] { 128 };

            _server = new UdpClient(3671);
            Console.WriteLine("Listening on UDP port 3671...");

            // Start periodic sensor simulation updates task
            _ = Task.Run(SimulateSensorUpdates);

            while (true)
            {
                try
                {
                    var result = await _server.ReceiveAsync();
                    byte[] data = result.Buffer;
                    IPEndPoint remoteEP = result.RemoteEndPoint;

                    if (data.Length < 6) continue;

                    byte headerLen = data[0];
                    byte version = data[1];
                    if (headerLen != 6 || version != 0x10) continue;

                    ushort serviceType = (ushort)((data[2] << 8) | data[3]);
                    ushort totalLen = (ushort)((data[4] << 8) | data[5]);

                    if (serviceType == 0x0205) // CONNECT_REQUEST
                    {
                        Console.WriteLine($"[CONNECT_REQUEST] from {remoteEP}");
                        _clientEndPoint = remoteEP;

                        byte[] response = new byte[20];
                        response[0] = 0x06; response[1] = 0x10; // Header
                        response[2] = 0x02; response[3] = 0x06; // CONNECT_RESPONSE
                        response[4] = 0x00; response[5] = 0x14; // Length=20
                        response[6] = 0x00; // Status: Success
                        response[7] = _channelId; // Channel ID
                        // HPAI (8 bytes)
                        response[8] = 0x08; response[9] = 0x01; // UDP
                        byte[] ipBytes = remoteEP.Address.GetAddressBytes();
                        Array.Copy(ipBytes, 0, response, 10, 4);
                        response[14] = (byte)((remoteEP.Port >> 8) & 0xFF);
                        response[15] = (byte)(remoteEP.Port & 0xFF);
                        // CRD (4 bytes)
                        response[16] = 0x04;
                        response[17] = 0x04; // Tunneling connection
                        response[18] = 0x11; response[19] = 0xff; // IA: 1.1.255

                        lock (_sendLock)
                        {
                            _server.Send(response, response.Length, remoteEP);
                        }
                    }
                    else if (serviceType == 0x0206) // CONNECTIONSTATE_REQUEST
                    {
                        byte chan = data[6];
                        byte[] response = new byte[8];
                        response[0] = 0x06; response[1] = 0x10;
                        response[2] = 0x02; response[3] = 0x07; // CONNECTIONSTATE_RESPONSE
                        response[4] = 0x00; response[5] = 0x08; // Length=8
                        response[6] = chan;
                        response[7] = 0x00; // Success

                        lock (_sendLock)
                        {
                            _server.Send(response, response.Length, remoteEP);
                        }
                    }
                    else if (serviceType == 0x0203) // TUNNELING_REQUEST (Host writes or reads)
                    {
                        byte chan = data[7];
                        byte seq = data[8];

                        // Send TUNNELING_ACK
                        byte[] ack = new byte[10];
                        ack[0] = 0x06; ack[1] = 0x10;
                        ack[2] = 0x02; ack[3] = 0x04; // TUNNELING_ACK
                        ack[4] = 0x00; ack[5] = 0x0A;
                        ack[6] = 0x04;
                        ack[7] = chan;
                        ack[8] = seq;
                        ack[9] = 0x00; // Success

                        lock (_sendLock)
                        {
                            _server.Send(ack, ack.Length, remoteEP);
                        }

                        // Parse CEMI payload starting at index 10
                        if (data.Length >= 20)
                        {
                            byte msgCode = data[10];
                            byte addInfoLen = data[11];
                            int cemiPayloadOffset = 10 + 2 + addInfoLen;

                            if (data.Length >= cemiPayloadOffset + 8)
                            {
                                ushort destAddr = (ushort)((data[cemiPayloadOffset + 4] << 8) | data[cemiPayloadOffset + 5]);
                                byte dataLen = data[cemiPayloadOffset + 6];
                                byte tpci = data[cemiPayloadOffset + 7];
                                byte apci = data[cemiPayloadOffset + 8];
                                int command = ((tpci & 0x03) << 2) | ((apci & 0xC0) >> 6);

                                string addrStr = FormatGroupAddress(destAddr);

                                if (command == 2) // GroupValueWrite
                                {
                                    byte[] payload;
                                    if (dataLen == 1)
                                    {
                                        payload = new byte[] { (byte)(apci & 0x3F) };
                                    }
                                    else
                                    {
                                        payload = new byte[dataLen - 1];
                                        Array.Copy(data, cemiPayloadOffset + 9, payload, 0, payload.Length);
                                    }

                                    _states[destAddr] = payload;
                                    Console.WriteLine($"[WRITE] Group Address {addrStr} value set to {BitConverter.ToString(payload)}");
                                }
                                else if (command == 0) // GroupValueRead
                                {
                                    Console.WriteLine($"[READ] Group Address {addrStr} requested");
                                    // Send response back
                                    if (_states.TryGetValue(destAddr, out var val))
                                    {
                                        SendTelegram(destAddr, val, isWrite: false);
                                    }
                                }
                            }
                        }
                    }
                    else if (serviceType == 0x0204) // TUNNELING_ACK from client
                    {
                        // We received ack for a telegram we sent, nothing to do
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }

        private static void SendTelegram(ushort groupAddress, byte[] value, bool isWrite)
        {
            if (_clientEndPoint == null || _server == null) return;

            bool isSmall = value.Length == 1 && value[0] <= 0x3F;

            int cemiLen = 11 + (isSmall ? 0 : value.Length);
            int totalLen = 10 + cemiLen;
            byte[] pkt = new byte[totalLen];

            pkt[0] = 0x06; pkt[1] = 0x10;
            pkt[2] = 0x02; pkt[3] = 0x03; // TUNNELING_REQUEST
            pkt[4] = (byte)((totalLen >> 8) & 0xFF);
            pkt[5] = (byte)(totalLen & 0xFF);

            // Connection Header
            pkt[6] = 0x04;
            pkt[7] = _channelId;
            pkt[8] = _sendSeqNum++;
            pkt[9] = 0x00;

            int cemiOffset = 10;
            pkt[cemiOffset] = 0x29;     // L_Data.ind
            pkt[cemiOffset + 1] = 0x00; // AddInfo length
            pkt[cemiOffset + 2] = 0xBC;
            pkt[cemiOffset + 3] = 0xE0;
            pkt[cemiOffset + 4] = 0x11; pkt[cemiOffset + 5] = 0x01; // Source IA (1.1.1)
            pkt[cemiOffset + 6] = (byte)((groupAddress >> 8) & 0xFF);
            pkt[cemiOffset + 7] = (byte)(groupAddress & 0xFF);
            pkt[cemiOffset + 8] = (byte)(isSmall ? 1 : (1 + value.Length));

            byte cmdBits = (byte)(isWrite ? 0x80 : 0x40);
            pkt[cemiOffset + 9] = 0x00;

            if (isSmall)
            {
                pkt[cemiOffset + 10] = (byte)(cmdBits | (value[0] & 0x3F));
            }
            else
            {
                pkt[cemiOffset + 10] = cmdBits;
                Array.Copy(value, 0, pkt, cemiOffset + 11, value.Length);
            }

            try
            {
                lock (_sendLock)
                {
                    _server.Send(pkt, pkt.Length, _clientEndPoint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending telegram: {ex.Message}");
            }
        }

        private static async Task SimulateSensorUpdates()
        {
            var random = new Random();
            double temp = 21.5;
            float power = 450.0f;

            while (true)
            {
                await Task.Delay(5000);

                if (_clientEndPoint == null) continue;

                // 1. Simulating Temperature Sensor (DPT 9.001) - Random Walk
                temp += (random.NextDouble() - 0.5) * 0.2;
                temp = Math.Max(18.0, Math.Min(26.0, temp));
                byte[] tempBytes = EncodeDpt9(temp);
                _states[ParseGroupAddress("1/1/10")] = tempBytes;
                SendTelegram(ParseGroupAddress("1/1/10"), tempBytes, isWrite: true);

                // 2. Simulating Power Sensor (DPT 14.056)
                power += (float)((random.NextDouble() - 0.5) * 20.0);
                power = Math.Max(50.0f, Math.Min(2000.0f, power));
                byte[] powerBytes = EncodeDpt14(power);
                _states[ParseGroupAddress("1/3/1")] = powerBytes;
                SendTelegram(ParseGroupAddress("1/3/1"), powerBytes, isWrite: true);
            }
        }

        private static ushort ParseGroupAddress(string addressStr)
        {
            var parts = addressStr.Split('/');
            uint main = uint.Parse(parts[0]);
            uint middle = uint.Parse(parts[1]);
            uint sub = uint.Parse(parts[2]);
            return (ushort)((main << 11) | (middle << 8) | sub);
        }

        private static string FormatGroupAddress(ushort addr)
        {
            int main = (addr >> 11) & 0x1F;
            int middle = (addr >> 8) & 0x07;
            int sub = addr & 0xFF;
            return $"{main}/{middle}/{sub}";
        }

        private static byte[] EncodeDpt9(double val)
        {
            double mantissaD = val * 100.0;
            int exponent = 0;
            
            if (val < 0)
            {
                while (mantissaD < -2048.0 && exponent < 15)
                {
                    mantissaD /= 2.0;
                    exponent++;
                }
            }
            else
            {
                while (mantissaD > 2047.0 && exponent < 15)
                {
                    mantissaD /= 2.0;
                    exponent++;
                }
            }

            int mantissa = (int)Math.Round(mantissaD);
            int mBits = mantissa & 0x7FF;
            int eBits = exponent & 0x0F;
            
            byte b0 = (byte)((val < 0 ? 0x80 : 0x00) | (eBits << 3) | ((mBits >> 8) & 0x07));
            byte b1 = (byte)(mBits & 0xFF);
            
            return new byte[] { b0, b1 };
        }

        private static byte[] EncodeDpt14(float val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return bytes;
        }
    }
}
