using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Ocpp
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Driver representing the central OCPP Master.
    /// Exposes aggregate telemetry across all connected wallboxes (total power,
    /// total energy import, number of active sessions) and a writable force_power
    /// setpoint (kW) that allocates power across all active sessions like the Solis battery.
    /// </summary>
    public class OcppMasterDriver : IDeviceDriver, IDeviceWriter
    {
        public string DriverName => "ocpp-master";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[]
        {
            TelemetryKeys.PowerKw,
            TelemetryKeys.EnergyImportKwh,
            TelemetryKeys.ActiveSessions,
            TelemetryKeys.ForcePowerKw
        };

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            [TelemetryKeys.PowerKw] = Units.Kilowatt,
            [TelemetryKeys.EnergyImportKwh] = Units.KilowattHour,
            [TelemetryKeys.ActiveSessions] = Units.None,
            [TelemetryKeys.ForcePowerKw] = Units.Kilowatt
        };

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            return OcppManagerService.Instance.GetServerTelemetry();
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
                Type = "OCPP Master",
                IsView = true
            };

            foreach (var key in keys)
            {
                string pointKey = $"{device.Id}_{key}";
                string niceName = key switch
                {
                    TelemetryKeys.PowerKw => "Total Charging Power",
                    TelemetryKeys.EnergyImportKwh => "Total Imported Energy",
                    TelemetryKeys.ActiveSessions => "Active Charging Sessions",
                    TelemetryKeys.ForcePowerKw => "Force Power Setpoint",
                    _ => key.Replace("_", " ")
                };

                string unit = units.GetValueOrDefault(key, "");

                var pDto = new TelemetryDto
                {
                    Id = pointKey,
                    Name = niceName,
                    FullName = $"{device.Name} / {niceName}",
                    Description = key switch
                    {
                        TelemetryKeys.ForcePowerKw => "Aggregate charging power setpoint in kW across all active sessions",
                        TelemetryKeys.ActiveSessions => "Number of currently active charging sessions",
                        _ => $"OCPP Master aggregate telemetry: {key}"
                    },
                    Units = unit,
                    Type = key == TelemetryKeys.ActiveSessions ? "Discrete" : "Analog",
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

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            return Task.FromResult(new List<PropertyDto>());
        }

        // IDeviceWriter methods
        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (key == TelemetryKeys.ForcePowerKw)
            {
                OcppManagerService.Instance.SetForcePowerAsync(value).GetAwaiter().GetResult();
            }
            else
            {
                throw new NotSupportedException($"Writing to key '{key}' is not supported on OCPP master.");
            }
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported by the OCPP master driver.");
        }

        public bool IsWritable(string key)
        {
            return key == TelemetryKeys.ForcePowerKw;
        }
    }
}
