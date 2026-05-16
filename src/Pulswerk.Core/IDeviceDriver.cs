// IDeviceDriver.cs – common read/write interfaces for protocol drivers
using System.Collections.Generic;

namespace Pulswerk.Core
{
    using DataPointValues = Dictionary<string, object>;

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

        DataPointValues Read(ConnectionConfig connection, DeviceConfig device);

        /// <summary>Returns the list of data point keys this driver produces.</summary>
        IEnumerable<string> GetDataPointKeys();

        /// <summary>
        /// Returns a key → unit string map for display purposes.
        /// Drivers that know their units override this; the default returns an empty dict.
        /// </summary>
        IReadOnlyDictionary<string, string> GetDataPointUnits()
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
        /// Writes complex/structured data to the field device.
        /// Used for schedules, calendars, and other non-numeric properties.
        /// </summary>
        void WriteComplex(ConnectionConfig connection, DeviceConfig device,
                          string key, object value);

        /// <summary>Returns true if the specific key is writable on this device.</summary>
        bool IsWritable(string key);
    }
}
