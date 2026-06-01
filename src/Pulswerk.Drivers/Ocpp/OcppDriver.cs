using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Ocpp
{
    using TelemetryValues = Dictionary<string, object>;

    public class OcppDriver : IDeviceDriver, IDeviceWriter
    {
        public string DriverName => "ocpp";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[]
        {
            "power",
            "energy_import",
            "status",
            "current",
            "voltage",
            "active_user",
            "power_limit",
            "charging_phases"
        };

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            ["power"] = "kW",
            ["energy_import"] = "kWh",
            ["current"] = "A",
            ["voltage"] = "V",
            ["active_user"] = "",
            ["power_limit"] = "A",
            ["charging_phases"] = ""
        };

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            // Returns the in-memory telemetry gathered from WebSocket messages
            return OcppManagerService.Instance.GetTelemetry(device.Id);
        }

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var units = GetTelemetryUnits();
            var keys = GetTelemetryKeys();

            var parentPath = (device.Path ?? new List<string>())
                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                .ToList();

            var deviceNode = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = "OCPP Wallbox",
                IsView = true
            };

            foreach (var key in keys)
            {
                string pointKey = $"{device.Id}_{key}";
                string niceName = key switch
                {
                    "power" => "Charging Power",
                    "energy_import" => "Imported Energy",
                    "status" => "Status",
                    "current" => "Charging Current",
                    "voltage" => "Grid Voltage",
                    "active_user" => "Active User RFID",
                    "power_limit" => "Max Charge Current Limit",
                    "charging_phases" => "Charging Phases",
                    _ => key.Replace("_", " ")
                };

                string unit = units.GetValueOrDefault(key, "");

                var pDto = new TelemetryDto
                {
                    Id = pointKey,
                    Name = niceName,
                    FullName = $"{device.Name} / {niceName}",
                    Description = styleDescription(key),
                    Units = unit,
                    Type = key == "status" || key == "active_user" ? "Discrete" : "Analog",
                    Key = pointKey,
                    IsWritable = IsWritable(key),
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

        private static string styleDescription(string key) => key switch
        {
            "charging_phases" => "Target number of charging phases (1 or 3)",
            _ => $"OCPP wallbox telemetry: {key}"
        };

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            return Task.FromResult(new List<PropertyDto>());
        }

        // IDeviceWriter methods
        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (key == "power_limit")
            {
                int connectorId = 1; // Default to connector 1
                OcppManagerService.Instance.SetChargingLimitAsync(device.Id, connectorId, value).GetAwaiter().GetResult();
            }
            else if (key == "charging_phases")
            {
                int connectorId = 1;
                // Retrieve current limit
                var telemetry = OcppManagerService.Instance.GetTelemetry(device.Id);
                double currentLimit = 16.0;
                if (telemetry.TryGetValue("power_limit", out var limObj) && limObj is double lim)
                {
                    currentLimit = lim;
                }
                OcppManagerService.Instance.SetChargingLimitAsync(device.Id, connectorId, currentLimit, (int)value).GetAwaiter().GetResult();
            }
            else
            {
                throw new NotSupportedException($"Writing to key '{key}' is not supported.");
            }
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported by the OCPP driver.");
        }

        public bool IsWritable(string key)
        {
            return key == "power_limit" || key == "charging_phases";
        }
    }
}
