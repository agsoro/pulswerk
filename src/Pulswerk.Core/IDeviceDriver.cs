// IDeviceDriver.cs – common read/write interfaces for protocol drivers
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Pulswerk.Core
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Outcome of a write-back attempt. Carries the rejection reason so callers can
    /// surface it instead of silently treating a no-op as success.
    /// </summary>
    public readonly record struct WriteResult(bool Success, string? Error)
    {
        public static WriteResult Ok() => new(true, null);
        public static WriteResult Fail(string error) => new(false, error);
    }

    /// <summary>
    /// Implement one class per device type.
    /// Read() receives the resolved ConnectionConfig so the driver knows host/port,
    /// plus the DeviceConfig for device-specific parameters (deviceId, address, …).
    /// </summary>
    public interface IDeviceDriver
    {
        string DriverName { get; }

        /// <summary>Returns true if the driver is currently busy with background tasks like discovery or history sync.</summary>
        bool IsBusy { get; }

        TelemetryValues Read(ConnectionConfig connection, DeviceConfig device);

        /// <summary>Returns the list of data point keys this driver produces.</summary>
        IEnumerable<string> GetTelemetryKeys();

        /// <summary>
        /// Returns a key → unit string map for display purposes.
        /// Drivers that know their units override this; the default returns an empty dict.
        /// </summary>
        IReadOnlyDictionary<string, string> GetTelemetryUnits()
            => new Dictionary<string, string>();

        /// <summary>Returns the hierarchical tree of assets/points for this device.</summary>
        AssetNodeDto GetAssetHierarchy(DeviceConfig device);

        /// <summary>Performs a live read of all technical properties for a specific point.</summary>
        Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key);
    }

    /// <summary>
    /// Optional write-back interface. Implement alongside IDeviceDriver on drivers that
    /// support writing values back to the field device.
    /// </summary>
    public interface IDeviceWriter
    {
        /// <summary>
        /// Writes one value to the field device.
        /// <paramref name="key"/> is the data point key.
        /// Throws on error; the caller logs and handles the failure.
        /// </summary>
        void Write(ConnectionConfig connection, DeviceConfig device,
                   string key, double value);

        /// <summary>
        /// Asynchronous write path. Drivers that perform network I/O should override this
        /// so callers (EMS loop, dashboard) never block on synchronous-over-async waits.
        /// The default implementation delegates to <see cref="Write"/>.
        /// </summary>
        Task WriteAsync(ConnectionConfig connection, DeviceConfig device,
                        string key, double value)
        {
            Write(connection, device, key, value);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Writes complex/structured data to the field device.
        /// Used for schedules, calendars, and other non-numeric properties.
        /// </summary>
        void WriteComplex(ConnectionConfig connection, DeviceConfig device,
                          string key, object value);

        /// <summary>Returns true if the specific key is writable on this device.</summary>
        bool IsWritable(string key);
    }
}
