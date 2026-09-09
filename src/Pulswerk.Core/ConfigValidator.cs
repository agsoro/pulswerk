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

                    if (conn.Type != "modbus-tcp" && conn.Type != "bacnet-ip" && conn.Type != "ocpp-ws" && conn.Type != "knx-ip" && conn.Type != "smgw-http")
                        errors.Add($"Connection '{conn.Id}' has unsupported type '{conn.Type}'. Supported: 'modbus-tcp', 'bacnet-ip', 'ocpp-ws', 'knx-ip', 'smgw-http'.");

                    if (conn.Type == "bacnet-ip")
                    {
                        if (conn.LocalPort == null)
                            errors.Add($"BACnet connection '{conn.Id}' is missing 'localPort'.");
                    }
                    else if (conn.Type == "modbus-tcp")
                    {
                        if (string.IsNullOrWhiteSpace(conn.Address) && !(cfg.Devices?.Any(d => d.ConnectionId == conn.Id && !string.IsNullOrWhiteSpace(d.Address)) ?? false))
                            errors.Add($"Modbus connection '{conn.Id}' has no address and no devices provide one.");
                    }
                    else if (conn.Type == "ocpp-ws")
                    {
                        if (conn.LocalPort == null)
                            errors.Add($"OCPP connection '{conn.Id}' is missing 'localPort'.");
                        if (string.IsNullOrWhiteSpace(conn.LocalAddress))
                            errors.Add($"OCPP connection '{conn.Id}' is missing 'localAddress' (path).");
                        else if (!conn.LocalAddress.StartsWith('/') || !conn.LocalAddress.EndsWith('/'))
                            errors.Add($"OCPP connection '{conn.Id}' has invalid 'localAddress' '{conn.LocalAddress}'. It must start and end with a '/' (e.g., '/plswk/ocpp/').");

                        if (cfg.Devices == null || !cfg.Devices.Any(d => d.DeviceType.Equals("ocpp-master", StringComparison.OrdinalIgnoreCase) && d.ConnectionId == conn.Id))
                            errors.Add($"OCPP connection '{conn.Id}' is missing a required 'ocpp-master' device (a device with deviceType 'ocpp-master' and connectionId '{conn.Id}').");
                    }
                    else if (conn.Type == "knx-ip")
                    {
                        // KNX is always tunnelling over TCP.
                        if (string.IsNullOrWhiteSpace(conn.Address))
                            errors.Add($"KNX connection '{conn.Id}' is missing 'address' (IP of the KNX IP Gateway).");

                        if (conn.KnxSecureEnabled && string.IsNullOrWhiteSpace(conn.KnxCommissioningPassword))
                            errors.Add($"KNX connection '{conn.Id}' has secure enabled but is missing 'knxCommissioningPassword' (the code printed on the IP bridge).");
                    }
                    else if (conn.Type == "smgw-http")
                    {
                        if (string.IsNullOrWhiteSpace(conn.Address))
                            errors.Add($"SMGW connection '{conn.Id}' is missing 'address' (IP of the Smart Meter Gateway).");

                        if (string.IsNullOrWhiteSpace(conn.Username))
                            errors.Add($"SMGW connection '{conn.Id}' is missing 'username'.");

                        if (string.IsNullOrWhiteSpace(conn.Password))
                            errors.Add($"SMGW connection '{conn.Id}' is missing 'password'.");
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

                    if (dev.DeviceType != "virtual" && dev.DeviceType != "ocpp" && dev.DeviceType != "ocpp-master" && dev.DeviceType != "knx" && dev.DeviceType != "smgw")
                    {
                        if (string.IsNullOrWhiteSpace(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' is missing a 'connectionId'.");
                        else if (!connections.ContainsKey(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' references unknown connectionId '{dev.ConnectionId}'.");

                        if (dev.DeviceId == null)
                            errors.Add($"Device '{dev.Id}' is missing 'deviceId' (Slave ID or Instance ID).");
                    }
                    else if (dev.DeviceType == "ocpp" || dev.DeviceType == "ocpp-master" || dev.DeviceType == "knx" || dev.DeviceType == "smgw")
                    {
                        if (string.IsNullOrWhiteSpace(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' is missing a 'connectionId'.");
                        else if (!connections.ContainsKey(dev.ConnectionId))
                            errors.Add($"Device '{dev.Id}' references unknown connectionId '{dev.ConnectionId}'.");
                    }

                    if (dev.DeviceType == "knx")
                    {
                        if (string.IsNullOrWhiteSpace(dev.KnxGroupAddressXml) && (dev.KnxPoints == null || dev.KnxPoints.Count == 0))
                        {
                            errors.Add($"KNX Device '{dev.Id}' must define 'knxGroupAddressXml' or at least one datapoint in 'knxPoints'.");
                        }

                        if (!string.IsNullOrWhiteSpace(dev.KnxGroupAddressXml))
                        {
                            string? resolved = ResolveConfigRelativePath(dev.KnxGroupAddressXml);
                            if (resolved == null || !System.IO.File.Exists(resolved))
                            {
                                errors.Add($"KNX Device '{dev.Id}' has 'knxGroupAddressXml' path '{dev.KnxGroupAddressXml}' but the file could not be found.");
                            }
                        }

                        if (dev.KnxPoints != null && dev.KnxPoints.Count > 0)
                        {
                            var pointKeys = new HashSet<string>();
                            foreach (var dp in dev.KnxPoints)
                            {
                                string context = $"Device '{dev.Id}' KNX datapoint '{dp.Key}'";
                                if (string.IsNullOrWhiteSpace(dp.Key))
                                    errors.Add($"Device '{dev.Id}' KNX datapoint has missing or empty 'key'.");
                                else if (!pointKeys.Add(dp.Key))
                                    errors.Add($"Device '{dev.Id}' has duplicate KNX datapoint key '{dp.Key}'.");

                                if (string.IsNullOrWhiteSpace(dp.GroupAddress))
                                    errors.Add($"{context}: Missing 'groupAddress'.");
                                else if (!System.Text.RegularExpressions.Regex.IsMatch(dp.GroupAddress, @"^\d+(/\d+){0,2}$"))
                                    errors.Add($"{context}: Invalid group address format '{dp.GroupAddress}'. Expected formats: 'X/Y/Z', 'X/Y', or 'X'.");
                                else
                                {
                                    try
                                    {
                                        var parts = dp.GroupAddress.Split('/');
                                        if (parts.Length == 3)
                                        {
                                            uint main = uint.Parse(parts[0]);
                                            uint middle = uint.Parse(parts[1]);
                                            uint sub = uint.Parse(parts[2]);
                                            if (main > 31 || middle > 7 || sub > 255)
                                                errors.Add($"{context}: KNX group address parts out of range (main <= 31, middle <= 7, sub <= 255).");
                                        }
                                        else if (parts.Length == 2)
                                        {
                                            uint main = uint.Parse(parts[0]);
                                            uint sub = uint.Parse(parts[1]);
                                            if (main > 31 || sub > 2047)
                                                errors.Add($"{context}: KNX group address parts out of range (main <= 31, sub <= 2047).");
                                        }
                                        else if (parts.Length == 1)
                                        {
                                            uint val = uint.Parse(parts[0]);
                                            if (val > 65535)
                                                errors.Add($"{context}: KNX group address out of range (value <= 65535).");
                                        }
                                    }
                                    catch
                                    {
                                        errors.Add($"{context}: Failed to parse group address '{dp.GroupAddress}'.");
                                    }
                                }

                                if (string.IsNullOrWhiteSpace(dp.Dpt))
                                    errors.Add($"{context}: Missing 'dpt' (Datapoint Type).");
                                else
                                {
                                    bool dptOk = dp.Dpt.StartsWith("1.") || dp.Dpt.StartsWith("5.") || dp.Dpt.StartsWith("9.") ||
                                                 dp.Dpt.StartsWith("12.") || dp.Dpt.StartsWith("13.") || dp.Dpt.StartsWith("14.");
                                    if (!dptOk)
                                        errors.Add($"{context}: Unsupported DPT '{dp.Dpt}'. Supported: DPT 1 (boolean), 5 (scaling), 9 (2-byte float), 12 (4-byte uint), 13 (4-byte int), 14 (4-byte float).");
                                }
                            }
                        }
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

                    if (dev.TelemetryKeys != null)
                    {
                        for (int i = 0; i < dev.TelemetryKeys.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(dev.TelemetryKeys[i]))
                                errors.Add($"Device '{dev.Id}' telemetryKeys[{i}]: Key cannot be empty.");
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

        private static string? ResolveConfigRelativePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath)) return null;
            if (System.IO.Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return System.IO.File.Exists(relativeOrAbsolutePath) ? relativeOrAbsolutePath : null;
            }

            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var targetFile = System.IO.Path.Combine(dir.FullName, relativeOrAbsolutePath);
                if (System.IO.File.Exists(targetFile)) return targetFile;

                var configJson = System.IO.Path.Combine(dir.FullName, "pulswerk.json");
                if (System.IO.File.Exists(configJson))
                {
                    var targetNearJson = System.IO.Path.Combine(dir.FullName, relativeOrAbsolutePath);
                    if (System.IO.File.Exists(targetNearJson)) return targetNearJson;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}
