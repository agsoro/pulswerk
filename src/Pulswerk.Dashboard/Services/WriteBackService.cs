// Services/WriteBackService.cs
// WriteValueAsync and WriteComplexValueAsync — write values back to field devices.

using System;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;
using Pulswerk.Drivers;
using Pulswerk.Drivers.BACnet;
using BACnet = System.IO.BACnet;

namespace Pulswerk.Dashboard
{
    public partial class DashboardDataService
    {
        public Task<bool> WriteValueAsync(string key, double value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null)
            {
                Log.Error($"[Dashboard] Write rejected: no device found for key '{key}'");
                return Task.FromResult(false);
            }

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null)
            {
                Log.Error($"[Dashboard] Write rejected: no connection for device '{device.Name}'");
                return Task.FromResult(false);
            }

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null)
            {
                Log.Error($"[Dashboard] Write rejected: driver for '{device.Name}' is not an IDeviceWriter");
                return Task.FromResult(false);
            }
            if (!writer.IsWritable(driverKey))
            {
                Log.Error($"[Dashboard] Write rejected: key '{driverKey}' (full: '{key}') is not writable");
                return Task.FromResult(false);
            }

            try
            {
                writer.Write(conn, device, driverKey, value);
                Log.Info($"[Dashboard] Manual write success: {key} = {value}");

                // Immediately update LatestValues with the correctly formatted display value
                object displayVal = value;
                if (drv is BacnetDriver bacDrv)
                {
                    var cachedObj = bacDrv.FindCachedObject(key);
                    if (cachedObj != null)
                    {
                        double internalVal = BacnetValueConverter.FromDisplayValue(cachedObj, value);
                        displayVal = BacnetValueConverter.FormatValue(
                            cachedObj, BACnet.BacnetPropertyIds.PROP_PRESENT_VALUE, internalVal);
                    }
                }
                lock (LatestValues)
                {
                    LatestValues[key] = displayVal;
                    LatestTimestamps[key] = DateTime.UtcNow;
                }

                // Immediately persist so charts reflect the change
                DataStore.Insert(key, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), value);
                DataStore.Flush();

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public Task<bool> WriteComplexValueAsync(string key, object value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null) return Task.FromResult(false);

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null) return Task.FromResult(false);

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null || !writer.IsWritable(driverKey)) return Task.FromResult(false);

            try
            {
                writer.WriteComplex(conn, device, driverKey, value);
                Log.Info($"[Dashboard] Complex write success: {key}");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Complex write failed for {key}: {ex.Message}");
                return Task.FromResult(false);
            }
        }
    }
}
