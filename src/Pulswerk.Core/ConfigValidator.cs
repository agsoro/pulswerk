using System;
using System.Collections.Generic;
using System.Linq;

namespace Pulswerk.Core
{
    /// <summary>
    /// Validates the AppConfig structure and logical consistency.
    /// Throws an exception if any critical validation rule is violated.
    /// </summary>
    public static class ConfigValidator
    {
        public static void Validate(AppConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg), "Configuration cannot be null.");

            var errors = new List<string>();

            // 1. Validate Connections
            if (cfg.Connections == null || cfg.Connections.Count == 0)
            {
                errors.Add("At least one connection must be defined.");
            }
            else
            {
                var connIds = new HashSet<string>();
                foreach (var conn in cfg.Connections)
                {
                    if (string.IsNullOrWhiteSpace(conn.Id))
                        errors.Add("Connection ID cannot be empty.");
                    else if (!connIds.Add(conn.Id))
                        errors.Add($"Duplicate connection ID found: '{conn.Id}'.");

                    if (conn.Type != "modbus-tcp" && conn.Type != "bacnet-ip")
                        errors.Add($"Connection '{conn.Id}' has unsupported type '{conn.Type}'. Supported: 'modbus-tcp', 'bacnet-ip'.");

                    if (conn.Type == "bacnet-ip")
                    {
                        if (conn.LocalPort == null)
                            errors.Add($"BACnet connection '{conn.Id}' is missing 'localPort'.");
                    }
                    else if (conn.Type == "modbus-tcp")
                    {
                        if (string.IsNullOrWhiteSpace(conn.Address) && !cfg.Devices.Any(d => d.ConnectionId == conn.Id && !string.IsNullOrWhiteSpace(d.Address)))
                            errors.Add($"Modbus connection '{conn.Id}' has no address and no devices provide one.");
                    }
                }
            }

            // 2. Validate Devices
            if (cfg.Devices == null || cfg.Devices.Count == 0)
            {
                errors.Add("At least one device must be defined.");
            }
            else
            {
                var deviceIds = new HashSet<string>();
                var connections = cfg.Connections?.ToDictionary(c => c.Id) ?? new Dictionary<string, ConnectionConfig>();

                foreach (var dev in cfg.Devices)
                {
                    if (string.IsNullOrWhiteSpace(dev.Id))
                        errors.Add("Device ID cannot be empty.");
                    else if (!deviceIds.Add(dev.Id))
                        errors.Add($"Duplicate device ID found: '{dev.Id}'.");

                    if (string.IsNullOrWhiteSpace(dev.Name))
                        errors.Add($"Device '{dev.Id}' is missing a name.");

                    if (string.IsNullOrWhiteSpace(dev.ConnectionId))
                        errors.Add($"Device '{dev.Id}' is missing a 'connectionId'.");
                    else if (!connections.ContainsKey(dev.ConnectionId))
                        errors.Add($"Device '{dev.Id}' references unknown connectionId '{dev.ConnectionId}'.");

                    if (dev.DeviceId == null)
                        errors.Add($"Device '{dev.Id}' is missing 'deviceId' (Slave ID or Instance ID).");
                }
            }

            // 3. Validate Polling
            if (cfg.Polling != null && cfg.Polling.IntervalSeconds < 1)
                errors.Add("Polling interval must be at least 1 second.");

            if (errors.Any())
            {
                var msg = "Configuration validation failed:\n" + string.Join("\n", errors.Select(e => $"  • {e}"));
                throw new Exception(msg);
            }
        }
    }
}
