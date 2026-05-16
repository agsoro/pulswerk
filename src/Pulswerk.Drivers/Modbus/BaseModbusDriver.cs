using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Modbus
{
    using DataPointValues = Dictionary<string, object>;

    public abstract class BaseModbusDriver : IDeviceDriver
    {
        public abstract string DriverName { get; }
        public virtual bool IsBusy => false;

        public abstract IEnumerable<string> GetDataPointKeys();
        public virtual IReadOnlyDictionary<string, string> GetDataPointUnits() => new Dictionary<string, string>();

        public abstract DataPointValues Read(ConnectionConfig connection, DeviceConfig device);

        public virtual AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var units = GetDataPointUnits();
            var keys = GetDataPointKeys();

            // Build the ParentPath (favorites back-link needs stable IDs)
            var parentPath = (device.Path ?? new List<string>())
                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                .ToList();

            var deviceNode = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = $"{DriverName} Device",
                IsView = true
            };

            foreach (var key in keys)
            {
                string pointKey = $"{device.Id}_{key}";   // globally unique
                string niceName = DataPointKeys.GetFriendlyName(key);
                if (niceName == key) niceName = key.Replace("_", " "); // Fallback formatting

                string unit = DataPointKeys.GetFriendlyUnit(key);
                if (string.IsNullOrEmpty(unit) && units.TryGetValue(key, out var u)) unit = u;

                var pDto = new DataPointDto
                {
                    Id = pointKey,
                    Name = niceName,
                    FullName = $"{device.Name} / {niceName}",
                    Description = $"{DriverName} point: {key}",
                    Units = unit,
                    Type = "Analog",
                    Key = pointKey,
                    IsWritable = this is IDeviceWriter writer && writer.IsWritable(key),
                    ParentPath = parentPath
                };

                if (device.Path != null && device.Path.Count > 0)
                {
                    pDto.ParentId = AssetNodeDto.PathSegmentId(device.Path.Last());
                }

                deviceNode.DataPoints.Add(pDto);
            }

            return deviceNode;
        }

        public virtual Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            // Default: no extended properties for basic Modbus
            return Task.FromResult(new List<PropertyDto>());
        }
    }
}
