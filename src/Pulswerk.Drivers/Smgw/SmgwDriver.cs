// SmgwDriver.cs – Smart Meter Gateway HAN driver for Pulswerk
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Smgw
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Driver for reading live electricity meter readings from a Smart Meter Gateway
    /// via its local HAN (Home Area Network) HTTPS / Digest Auth interface.
    /// </summary>
    public class SmgwDriver : IDeviceDriver
    {
        public string DriverName => "smgw";

        public bool IsBusy => false;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _discoveredDeviceKeys = new(StringComparer.OrdinalIgnoreCase);

        public static readonly string[] AllSupportedKeys = new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyExportKwh,
            TelemetryKeys.EnergyImportKwh,
            "power_l1",
            "power_l2",
            "power_l3",
            "voltage_l1",
            "voltage_l2",
            "voltage_l3",
            "current_l1",
            "current_l2",
            "current_l3",
            "frequency"
        };

        public IEnumerable<string> GetTelemetryKeys()
        {
            if (!_discoveredDeviceKeys.IsEmpty)
            {
                var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var keys in _discoveredDeviceKeys.Values)
                {
                    foreach (var k in keys) union.Add(k);
                }
                if (union.Count > 0) return union;
            }

            return AllSupportedKeys;
        }

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.EnergyExportKwh] = Units.KilowattHour,
            [TelemetryKeys.EnergyImportKwh] = Units.KilowattHour,
            ["power_l1"] = Units.Kilowatt,
            ["power_l2"] = Units.Kilowatt,
            ["power_l3"] = Units.Kilowatt,
            ["voltage_l1"] = Units.Volt,
            ["voltage_l2"] = Units.Volt,
            ["voltage_l3"] = Units.Volt,
            ["current_l1"] = Units.Ampere,
            ["current_l2"] = Units.Ampere,
            ["current_l3"] = Units.Ampere,
            ["frequency"] = Units.Hertz
        };

        public static IEnumerable<string> GetEffectiveKeys(DeviceConfig device, IEnumerable<string>? newlyDiscovered = null)
        {
            // 1. Explicitly configured in pulswerk.json for this device
            if (device.TelemetryKeys != null && device.TelemetryKeys.Count > 0)
            {
                return device.TelemetryKeys;
            }

            // 2. Newly discovered from current read
            if (newlyDiscovered != null)
            {
                var discovered = newlyDiscovered.Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                if (discovered.Count > 0)
                {
                    _discoveredDeviceKeys[device.Id] = new HashSet<string>(discovered, StringComparer.OrdinalIgnoreCase);
                    return discovered;
                }
            }

            // 3. Previously discovered on this device instance
            if (_discoveredDeviceKeys.TryGetValue(device.Id, out var cached) && cached.Count > 0)
            {
                return cached;
            }

            // 4. Default fallback: only energy_export (the standard TAF-7 billing meter point)
            return new[] { TelemetryKeys.EnergyExportKwh };
        }

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            var smgw = SmgwConnection.GetOrCreate(connection);
            string targetMeter = device.MeterId ?? (device.DeviceId.HasValue ? device.DeviceId.Value.ToString() : "");

            try
            {
                string html = smgw.GetLiveMeterProfileHtmlAsync(targetMeter).GetAwaiter().GetResult();
                var rawValues = SmgwParser.ParseMeterReadings(html);
                if (rawValues.Count == 0)
                {
                    string snippet = html.Length > 300 ? html.Substring(0, 300) : html;
                    Log.Warning($"[SMGW] No meter readings parsed for device '{device.Name}' from {connection.Address} (HTML len {html.Length}): {snippet.Replace("\n", " ").Replace("\r", " ")}");
                    return rawValues;
                }

                // Filter to only values available / configured for this instance
                var activeKeys = GetEffectiveKeys(device, rawValues.Keys);
                var values = new TelemetryValues(StringComparer.OrdinalIgnoreCase);

                foreach (var k in activeKeys)
                {
                    if (rawValues.TryGetValue(k, out var v))
                    {
                        values[k] = v;
                    }
                }

                return values;
            }
            catch (Exception ex)
            {
                Log.Error($"[SMGW] Error reading device '{device.Name}' on {connection.Address}: {ex.Message}");
                throw;
            }
        }

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var units = GetTelemetryUnits();
            var keys = GetEffectiveKeys(device);

            var parentPath = (device.Path ?? new List<string>())
                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                .ToList();

            var deviceNode = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = device.AssetType ?? "Smart Meter Gateway",
                IsView = true
            };

            foreach (var key in keys)
            {
                string pointKey = $"{device.Id}_{key}";
                string niceName = TelemetryKeys.GetFriendlyName(key);
                if (niceName == key) niceName = key.Replace("_", " ");

                string unit = TelemetryKeys.GetFriendlyUnit(key);
                if (string.IsNullOrEmpty(unit) && units.TryGetValue(key, out var u)) unit = u;

                var pDto = new TelemetryDto
                {
                    Id = pointKey,
                    Name = niceName,
                    FullName = $"{device.Name} / {niceName}",
                    Description = $"SMGW point: {key}",
                    Units = unit,
                    Type = "Analog",
                    Key = pointKey,
                    IsWritable = false,
                    ParentPath = parentPath
                };

                if (device.Path != null && device.Path.Count > 0)
                {
                    pDto.ParentId = AssetNodeDto.PathSegmentId(device.Path.Last());
                }

                deviceNode.Telemetries.Add(pDto);
            }

            return deviceNode;
        }

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            var props = new List<PropertyDto>
            {
                new() { Name = "Driver", Value = "Smart Meter Gateway HAN" },
                new() { Name = "Gateway IP", Value = connection.Address ?? "192.168.1.200" },
                new() { Name = "Zählernummer", Value = device.MeterId ?? "1 EFR 24 75081296" },
                new() { Name = "Messlokation (MeLo)", Value = "DE0010689433310000000000003394391" },
                new() { Name = "Tarifanwendungsfall", Value = "TAF-7: Zählerstandgangmessung" },
                new() { Name = "Tarifbezeichnung", Value = "IM4G_TAF07_EINSP_15MI_WIRK_NEU" }
            };

            return Task.FromResult(props);
        }
    }
}
