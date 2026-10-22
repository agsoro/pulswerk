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
        public async Task<WriteResult> WriteValueAsync(string key, double value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null)
            {
                string error = $"no device found for key '{key}'";
                Log.Error($"[Dashboard] Write rejected: {error}");
                return WriteResult.Fail(error);
            }

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null && device.DeviceType != "ems" && device.DeviceType != "virtual")
            {
                string error = $"no connection for device '{device.Name}'";
                Log.Error($"[Dashboard] Write rejected: {error}");
                return WriteResult.Fail(error);
            }

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null)
            {
                string error = $"driver for '{device.Name}' is not an IDeviceWriter";
                Log.Error($"[Dashboard] Write rejected: {error}");
                return WriteResult.Fail(error);
            }
            if (!writer.IsWritable(driverKey))
            {
                string error = $"key '{driverKey}' (full: '{key}') is not writable";
                Log.Error($"[Dashboard] Write rejected: {error}");
                return WriteResult.Fail(error);
            }

            try
            {
                await writer.WriteAsync(conn!, device, driverKey, value);
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

                return WriteResult.Ok();
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Write failed for {key}: {ex.Message}");
                return WriteResult.Fail(ex.Message);
            }
        }

        public Task<WriteResult> WriteComplexValueAsync(string key, object value)
        {
            var device = IdentifyDeviceFromTelemetryKey(key);
            if (device == null) return Task.FromResult(WriteResult.Fail($"no device found for key '{key}'"));

            string driverKey = key.Substring(device.Id.Length + 1);

            var conn = Config.Connections.FirstOrDefault(c => c.Id == device.ConnectionId);
            if (conn == null && device.DeviceType != "ems" && device.DeviceType != "virtual")
                return Task.FromResult(WriteResult.Fail($"no connection for device '{device.Name}'"));

            var writer = (Drivers.TryGetValue(device.Name, out var drv) ? drv : null) as IDeviceWriter;
            if (writer == null || !writer.IsWritable(driverKey))
                return Task.FromResult(WriteResult.Fail($"key '{driverKey}' (full: '{key}') is not writable"));

            try
            {
                writer.WriteComplex(conn!, device, driverKey, value);
                Log.Info($"[Dashboard] Complex write success: {key}");
                return Task.FromResult(WriteResult.Ok());
            }
            catch (Exception ex)
            {
                Log.Error($"[Dashboard] Complex write failed for {key}: {ex.Message}");
                return Task.FromResult(WriteResult.Fail(ex.Message));
            }
        }
    }
}
