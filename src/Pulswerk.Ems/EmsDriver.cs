using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Ems
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Protocol driver representing the central Energy Management System (EMS).
    /// Publishes live power balance, uncontrollable base loads, surplus pools,
    /// 24h rolling energy metrics, and per-consumer allocation telemetries.
    /// </summary>
    public class EmsDriver : IDeviceDriver, IDeviceWriter
    {
        public string DriverName => "ems";
        public bool IsBusy => false;

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            return EmsService.Instance.GetTelemetryValues();
        }

        public IEnumerable<string> GetTelemetryKeys() => EmsService.Instance.GetTelemetryKeys();

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => EmsService.Instance.GetTelemetryUnits();

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var units = GetTelemetryUnits();
            var keys = GetTelemetryKeys().ToList();

            var parentPath = (device.Path ?? new List<string>())
                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                .ToList();

            var deviceNode = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = "Energy Management System",
                IsView = true
            };

            foreach (var key in keys)
            {
                string pointKey = $"{device.Id}_{key}";
                string name = EmsService.Instance.GetTelemetryFriendlyName(key);
                string unit = units.GetValueOrDefault(key, "");

                var pDto = new TelemetryDto
                {
                    Id = pointKey,
                    Name = name,
                    FullName = $"{device.Name} / {name}",
                    Description = $"EMS telemetry: {name}",
                    Units = unit,
                    Type = "Analog",
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

        public bool IsWritable(string key) => key switch
        {
            "enabled" or "grid_max_import" => true,
            _ => false
        };

        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            if (key == "enabled")
            {
                EmsService.Instance.SetEnabled(value > 0.5);
            }
            else if (key == "grid_max_import")
            {
                var cfg = EmsService.Instance.SourcesConfig;
                cfg.GridMaxImportKw = value;
                EmsService.Instance.SaveConfiguration(EmsService.Instance.Enabled, cfg, EmsService.Instance.Consumers);
            }
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value) { }
    }
}
