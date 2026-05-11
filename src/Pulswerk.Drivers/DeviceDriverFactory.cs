// DeviceDriverFactory.cs – creates driver instances by deviceType, discovered via reflection
//
//  Convention: each IDeviceDriver declares its key via the DriverName property.
//  The factory creates a probe instance at startup, reads DriverName, lowercases it,
//  and registers the type under that key.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Pulswerk.Core;

namespace Pulswerk.Drivers
{
    /// <summary>
    /// Pure factory — creates a new <see cref="IDeviceDriver"/> instance on every call.
    /// Driver types are discovered at startup via reflection: any concrete class
    /// implementing <see cref="IDeviceDriver"/> is registered under its
    /// <see cref="IDeviceDriver.DriverName"/> (lowercased).
    /// </summary>
    public static class DeviceDriverFactory
    {
        // Discovered once, keyed by DriverName.ToLower() (e.g. "janitza" → typeof(JanitzaDriver))
        static readonly Dictionary<string, Type> _driverTypes;

        static DeviceDriverFactory()
        {
            _driverTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

            var iface = typeof(IDeviceDriver);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && a.FullName?.StartsWith("Pulswerk") == true);

            foreach (var asm in assemblies)
            {
                foreach (var type in asm.GetTypes())
                {
                    if (type.IsAbstract || type.IsInterface || !iface.IsAssignableFrom(type))
                        continue;

                    // Create a probe instance to read the DriverName property
                    try
                    {
                        var probe = (IDeviceDriver)Activator.CreateInstance(type)!;
                        string key = probe.DriverName.ToLowerInvariant();
                        _driverTypes[key] = type;
                    }
                    catch
                    {
                        // Skip types that can't be instantiated (e.g. missing dependencies)
                    }
                }
            }
        }

        /// <summary>
        /// Creates a new driver instance for the given device type.
        /// </summary>
        public static IDeviceDriver Create(string deviceType)
        {
            if (_driverTypes.TryGetValue(deviceType, out var type))
                return (IDeviceDriver)Activator.CreateInstance(type)!;

            throw new NotSupportedException(
                $"Unknown deviceType '{deviceType}'. " +
                $"Known types: {string.Join(", ", _driverTypes.Keys.OrderBy(k => k))}");
        }

        /// <summary>Returns the registered device type keys (for diagnostics / help text).</summary>
        public static IReadOnlyCollection<string> KnownTypes => _driverTypes.Keys;
    }
}
