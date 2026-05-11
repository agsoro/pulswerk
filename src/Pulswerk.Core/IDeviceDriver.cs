// IDeviceDriver.cs – common read/write interfaces for protocol drivers
using System.Collections.Generic;

namespace Pulswerk.Core
{
    using Telemetry = Dictionary<string, object>;

    /// <summary>
    /// Implement one class per device type.
    /// Read() receives the resolved ConnectionConfig so the driver knows host/port,
    /// plus the DeviceConfig for device-specific parameters (slaveId, bacnetDeviceId, …).
    /// </summary>
    public interface IDeviceDriver
    {
        string DriverName { get; }

        /// <summary>Returns true if the driver is currently busy with background tasks like discovery or history sync.</summary>
        bool IsBusy { get; }

        Telemetry Read(ConnectionConfig connection, DeviceConfig device);

        /// <summary>Returns the list of telemetry keys this driver produces.</summary>
        IEnumerable<string> GetTelemetryKeys();

        /// <summary>
        /// Returns a key → unit string map for display purposes.
        /// Drivers that know their units override this; the default returns an empty dict.
        /// </summary>
        IReadOnlyDictionary<string, string> GetTelemetryUnits()
            => new Dictionary<string, string>();
    }

    /// <summary>
    /// Optional write-back interface. Implement alongside IDeviceDriver on drivers that
    /// support writing values back to the field device.
    /// </summary>
    public interface IDeviceWriter
    {
        /// <summary>
        /// Writes one value to the field device.
        /// <paramref name="key"/> is the telemetry/attribute key
        /// (e.g. "ao_3_supply_sp_value").
        /// Throws on error; the caller logs and handles the failure.
        /// </summary>
        void Write(ConnectionConfig connection, DeviceConfig device,
                   string key, double value);
    }
}
