using System;

namespace Pulswerk.Drivers.Knx
{
    public static class KnxDpt
    {
        public static object Decode(byte[] bytes, string dpt)
        {
            if (bytes == null || bytes.Length == 0)
                return 0.0;

            if (dpt.StartsWith("1.")) // 1-bit boolean (Switch, True/False)
            {
                return bytes[0] != 0;
            }
            else if (dpt.StartsWith("5.")) // 8-bit unsigned value (Scaling, 0-255 / 0-100%)
            {
                return (double)bytes[0];
            }
            else if (dpt.StartsWith("9.")) // 2-byte float (Temperature, Humidity, etc.)
            {
                if (bytes.Length < 2) return 0.0;
                int sign = (bytes[0] & 0x80) >> 7;
                int exponent = (bytes[0] & 0x78) >> 3;
                int mantissa = ((bytes[0] & 0x07) << 8) | bytes[1];
                if (sign == 1)
                {
                    mantissa = mantissa - 2048; // Two's complement for 11-bit mantissa
                }
                return Math.Round(mantissa * 0.01 * Math.Pow(2, exponent), 2);
            }
            else if (dpt.StartsWith("12.")) // 4-byte unsigned integer
            {
                if (bytes.Length < 4) return 0U;
                byte[] temp = new byte[4];
                Array.Copy(bytes, temp, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(temp);
                return BitConverter.ToUInt32(temp, 0);
            }
            else if (dpt.StartsWith("13.")) // 4-byte signed integer
            {
                if (bytes.Length < 4) return 0;
                byte[] temp = new byte[4];
                Array.Copy(bytes, temp, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(temp);
                return BitConverter.ToInt32(temp, 0);
            }
            else if (dpt.StartsWith("14.")) // 4-byte Float (IEEE 754)
            {
                if (bytes.Length < 4) return 0.0;
                byte[] temp = new byte[4];
                Array.Copy(bytes, temp, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(temp);
                return Math.Round((double)BitConverter.ToSingle(temp, 0), 3);
            }

            return bytes[0]; // Fallback to first byte
        }

        public static byte[] Encode(object value, string dpt, out bool isSmall)
        {
            isSmall = false;

            if (dpt.StartsWith("1."))
            {
                isSmall = true;
                bool b = Convert.ToBoolean(value);
                return new byte[] { (byte)(b ? 1 : 0) };
            }
            else if (dpt.StartsWith("5."))
            {
                byte val = Convert.ToByte(value);
                return new byte[] { val };
            }
            else if (dpt.StartsWith("9."))
            {
                double val = Convert.ToDouble(value);
                
                // Convert double to 16-bit KNX float:
                // Value = (Mantissa * 0.01) * 2^Exponent
                // We want Mantissa = (Value / 0.01) / 2^Exponent
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
                int mBits = mantissa & 0x7FF; // 11-bit mantissa
                int eBits = exponent & 0x0F;  // 4-bit exponent
                
                byte b0 = (byte)((val < 0 ? 0x80 : 0x00) | (eBits << 3) | ((mBits >> 8) & 0x07));
                byte b1 = (byte)(mBits & 0xFF);
                
                return new byte[] { b0, b1 };
            }
            else if (dpt.StartsWith("12."))
            {
                uint val = Convert.ToUInt32(value);
                byte[] bytes = BitConverter.GetBytes(val);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                return bytes;
            }
            else if (dpt.StartsWith("13."))
            {
                int val = Convert.ToInt32(value);
                byte[] bytes = BitConverter.GetBytes(val);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                return bytes;
            }
            else if (dpt.StartsWith("14."))
            {
                float val = Convert.ToSingle(value);
                byte[] bytes = BitConverter.GetBytes(val);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                return bytes;
            }

            throw new NotSupportedException($"DPT '{dpt}' encoding is not supported.");
        }

        public static string NormalizeDpt(string? dpt)
        {
            if (string.IsNullOrWhiteSpace(dpt))
                return "1.001"; // Fallback default

            dpt = dpt.Trim();

            // If it's already in the format "1.001" or "9.001"
            if (dpt.Length > 0 && char.IsDigit(dpt[0]) && dpt.Contains('.'))
                return dpt;

            // Handle formats like "DPST-9-1", "DPS-9-1", "DPT-9", "DPST-1-1"
            var parts = dpt.Split(new[] { '-', '.', '_' }, StringSplitOptions.RemoveEmptyEntries);
            int mainType = 0;
            int subType = 1;
            bool foundMain = false;

            foreach (var part in parts)
            {
                if (int.TryParse(part, out int val))
                {
                    if (!foundMain)
                    {
                        mainType = val;
                        foundMain = true;
                    }
                    else
                    {
                        subType = val;
                        break;
                    }
                }
            }

            if (foundMain)
            {
                return $"{mainType}.{subType:D3}";
            }

            return dpt; // Return as-is if no numbers found
        }
    }
}
