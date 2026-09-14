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
            "force_power",
            "charging_phases"
        };

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            ["power"] = Units.Kilowatt,
            ["energy_import"] = Units.KilowattHour,
            ["current"] = Units.Ampere,
            ["voltage"] = Units.Volt,
            ["active_user"] = Units.None,
            ["force_power"] = Units.Kilowatt,
            ["charging_phases"] = Units.None
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
                    "force_power" => "Force Power Setpoint",
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
            "force_power" => "Charging power setpoint in kW",
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
            var manager = OcppManagerService.Instance;
            if (key == "force_power" || key == TelemetryKeys.ForcePowerKw)
            {
                int connectorId = 1;
                if (value < 0.0)
                {
                    // Negative: Unrestricted mode (16A, 3 phases = 11.04 kW) & clear profiles
                    _ = manager.ClearChargingProfileAsync(device.Id, 0);
                    manager.SetChargingLimitAsync(device.Id, connectorId, OcppManagerService.DefaultMaxCurrentAmps, OcppManagerService.MaxPhases, 11.04).GetAwaiter().GetResult();
                }
                else
                {
                    var (amps, phases) = OcppManagerService.ResolveForcePower(value);
                    manager.SetChargingLimitAsync(device.Id, connectorId, amps, phases, value).GetAwaiter().GetResult();
                }
            }
            else if (key == "charging_phases")
            {
                int connectorId = 1;
                int newPhases = Math.Clamp((int)value, 1, 3);
                var telemetry = manager.GetTelemetry(device.Id);
                double currentPowerKw = 11.04;
                if (telemetry.TryGetValue("force_power", out var fpObj) && fpObj is double fp)
                {
                    currentPowerKw = fp;
                }
                var (amps, phases) = OcppManagerService.ResolveForcePower(currentPowerKw, newPhases);
                manager.SetChargingLimitAsync(device.Id, connectorId, amps, phases, currentPowerKw).GetAwaiter().GetResult();
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
            return key == "force_power" || key == TelemetryKeys.ForcePowerKw || key == "charging_phases";
        }
    }
}
