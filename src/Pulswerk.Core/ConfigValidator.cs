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

                    if (dev.DeviceType != "virtual")
                    {
                        if (string.IsNullOrWhiteSpace(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' is missing a 'connectionId'.");
                        else if (!connections.ContainsKey(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' references unknown connectionId '{dev.ConnectionId}'.");

                        if (dev.DeviceId == null)
                            errors.Add($"Device '{dev.Id}' is missing 'deviceId' (Slave ID or Instance ID).");
                    }
                    if (dev.Telemetries != null)
                    {
                        foreach (var dp in dev.Telemetries)
                        {
                            string context = $"Device '{dev.Id}' virtual data point '{dp.Id}'";
                            if (string.IsNullOrWhiteSpace(dp.Id))
                                errors.Add($"{context}: Missing 'id'.");
                            if (string.IsNullOrWhiteSpace(dp.Formula))
                                errors.Add($"{context}: Missing 'formula'.");
                            else
                                ValidateFormula(dp.Formula, cfg, dev, context, errors);
                        }
                    }
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

        private static void ValidateFormula(string formula, AppConfig cfg, DeviceConfig? currentDevice, string context, List<string> errors)
        {
            // 1. Validate pathsum syntax
            if (formula.Contains("pathsum", StringComparison.OrdinalIgnoreCase))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(formula, @"pathsum\s*\(\s*['""](.+?)['""]\s*,\s*['""](.+?)['""]\s*\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    errors.Add($"{context}: Invalid pathsum syntax. Expected pathsum(\"path\", \"filter\").");
                }
            }

            // 2. Validate consumption modifiers
            if (formula.Contains(":consumption:"))
            {
                var parts = formula.Split(":consumption:");
                if (parts.Length != 2 || (parts[1] != "1h" && parts[1] != "1d" && parts[1] != "1m" && parts[1] != "1y"))
                {
                    errors.Add($"{context}: Invalid consumption interval '{parts.LastOrDefault()}'. Supported: :1h, :1d, :1m, :1y.");
                }
            }

            // 3. Validate point references (external to strings)
            var allDeviceIds = cfg.Devices.Select(d => d.Id).ToHashSet();
            
            // Remove strings and consumption modifiers from formula before checking for variable references
            string cleanFormula = System.Text.RegularExpressions.Regex.Replace(formula, @"(['""])(?:(?=(\\?))\2.)*?\1", "");
            cleanFormula = System.Text.RegularExpressions.Regex.Replace(cleanFormula, @":consumption:[a-z0-9]+", "");
            
            var segments = System.Text.RegularExpressions.Regex.Matches(cleanFormula, @"([a-zA-Z0-9\-_]+)")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Value)
                .ToList();

            foreach (var segment in segments)
            {
                if (double.TryParse(segment, out _) || 
                    segment.Equals("pathsum", StringComparison.OrdinalIgnoreCase)) 
                    continue;

                // Check if segment refers to a known device
                bool hasKnownDevice = false;
                foreach (var devId in allDeviceIds)
                {
                    if (segment.StartsWith(devId + "_") || segment == devId)
                    {
                        hasKnownDevice = true;
                        break;
                    }
                }

                // If it's not an external device reference, and it contains underscores,
                // it might be a local key (allowed) or a broken device prefix.
                if (!hasKnownDevice && segment.Contains("_"))
                {
                    // Check if it's potentially a local key of the current device.
                    // If the segment doesn't start with *any* known device ID, we assume it's local.
                    bool startsWithOtherDevice = allDeviceIds.Any(id => segment.StartsWith(id + "_"));
                    
                    if (startsWithOtherDevice)
                    {
                        errors.Add($"{context}: Formula references unknown device in point '{segment}'.");
                    }
                }
            }
        }
    }
}
