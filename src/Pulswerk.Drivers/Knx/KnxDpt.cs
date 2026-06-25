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
            else if (dpt.StartsWith("3.")) // 4-bit controlled (Dimming/Blinds: 1 control bit + 3 step bits)
            {
                // Lower 4 bits: bit 3 = direction/control, bits 0-2 = step code (0 = stop)
                return (int)(bytes[0] & 0x0F);
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
            else if (dpt.StartsWith("10.")) // 3-byte Time of day
            {
                if (bytes.Length < 3) return "00:00:00";
                int hour = bytes[0] & 0x1F;       // bits 0-4
                int minute = bytes[1] & 0x3F;     // bits 0-5
                int second = bytes[2] & 0x3F;     // bits 0-5
                return $"{hour:D2}:{minute:D2}:{second:D2}";
            }
            else if (dpt.StartsWith("11.")) // 3-byte Date
            {
                if (bytes.Length < 3) return "2000-01-01";
                int day = bytes[0] & 0x1F;        // bits 0-4
                int month = bytes[1] & 0x0F;      // bits 0-3
                int yy = bytes[2] & 0x7F;         // bits 0-6
                int year = yy < 90 ? 2000 + yy : 1900 + yy;
                return $"{year:D4}-{month:D2}-{day:D2}";
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
            else if (dpt.StartsWith("3.")) // 4-bit controlled (Dimming/Blinds: 1 control bit + 3 step bits)
            {
                isSmall = true;
                int code;
                if (value is bool boolVal)
                {
                    // true  => increase/up at full step  (control=1, step=1) => 0b1001 = 0x09
                    // false => decrease/down at full step (control=0, step=1) => 0b0001 = 0x01
                    code = boolVal ? 0x09 : 0x01;
                }
                else
                {
                    // Treat the value as the raw 4-bit control code:
                    // bit 3 = direction (1 = up/increase, 0 = down/decrease)
                    // bits 0-2 = step code (0 = stop/break, 1-7 = intervals)
                    code = Convert.ToInt32(value) & 0x0F;
                }
                return new byte[] { (byte)code };
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
            else if (dpt.StartsWith("10.")) // 3-byte Time of day
            {
                int dayOfWeek = 0; // 0 = no day specified
                int hour, minute, second;
                if (value is DateTime dtTime)
                {
                    // ISO day of week: Mon=1 .. Sun=7
                    dayOfWeek = dtTime.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)dtTime.DayOfWeek;
                    hour = dtTime.Hour; minute = dtTime.Minute; second = dtTime.Second;
                }
                else if (value is TimeSpan ts)
                {
                    hour = ts.Hours; minute = ts.Minutes; second = ts.Seconds;
                }
                else
                {
                    var t = TimeSpan.Parse(Convert.ToString(value) ?? "00:00:00");
                    hour = t.Hours; minute = t.Minutes; second = t.Seconds;
                }
                byte b0 = (byte)(((dayOfWeek & 0x07) << 5) | (hour & 0x1F));
                byte b1 = (byte)(minute & 0x3F);
                byte b2 = (byte)(second & 0x3F);
                return new byte[] { b0, b1, b2 };
            }
            else if (dpt.StartsWith("11.")) // 3-byte Date
            {
                DateTime d = value is DateTime dtDate
                    ? dtDate
                    : DateTime.Parse(Convert.ToString(value) ?? "2000-01-01");
                int yy = d.Year % 100;
                byte b0 = (byte)(d.Day & 0x1F);
                byte b1 = (byte)(d.Month & 0x0F);
                byte b2 = (byte)(yy & 0x7F);
                return new byte[] { b0, b1, b2 };
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
