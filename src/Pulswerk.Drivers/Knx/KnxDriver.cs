using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Knx
{
    using TelemetryValues = Dictionary<string, object>;

    public class KnxDriver : IDeviceDriver, IDeviceWriter
    {
        public string DriverName => "knx";
        public bool IsBusy => false;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWrite, List<ParsedKnxPoint> Points)> _xmlCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime, List<ParsedKnxPoint>)>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> GetTelemetryKeys()
        {
            return Array.Empty<string>(); // Managed dynamically per-device configuration
        }

        public IReadOnlyDictionary<string, string> GetTelemetryUnits()
        {
            return new Dictionary<string, string>(); // Managed dynamically per-device
        }

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            var telemetryValues = new TelemetryValues();
            var points = GetEffectivePoints(device);

            if (points == null || points.Count == 0)
                return telemetryValues;

            if (!KnxConnection.TryGetConnection(connection.Id, out var knxConn) || knxConn == null)
            {
                Log.Warning($"[KNX] No active connection found for ID '{connection.Id}' to read device '{device.Name}'");
                return telemetryValues;
            }

            foreach (var point in points)
            {
                try
                {
                    ushort groupAddr = KnxConnection.ParseGroupAddress(point.GroupAddress);
                    byte[]? rawBytes = knxConn.GetCachedValue(groupAddr);

                    if (rawBytes != null)
                    {
                        object decodedValue = KnxDpt.Decode(rawBytes, point.Dpt);
                        telemetryValues[point.Key] = decodedValue;
                    }
                    else
                    {
                        // Trigger an asynchronous read request on the bus to populate the cache for next poll
                        _ = Task.Run(async () =>
                        {
                            try { await knxConn.SendGroupRead(groupAddr); }
                            catch (Exception ex) { Log.Debug($"[KNX] Auto-poll read request failed for {point.GroupAddress}: {ex.Message}"); }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[KNX] Error reading point '{point.Key}' ({point.GroupAddress}) on device '{device.Name}': {ex.Message}");
                }
            }

            return telemetryValues;
        }

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var points = GetEffectivePoints(device);
            var parentPath = (device.Path ?? new List<string>())
                .Select(seg => new PathSegmentDto { Id = AssetNodeDto.PathSegmentId(seg), Name = seg })
                .ToList();

            var deviceNode = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = "KNX Device",
                IsView = true
            };

            // Parse XML map for hierarchy paths
            var xmlPointPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(device.KnxGroupAddressXml))
            {
                string? resolvedPath = ResolveConfigRelativePath(device.KnxGroupAddressXml);
                if (resolvedPath != null && File.Exists(resolvedPath))
                {
                    var parsed = GetParsedXmlPoints(resolvedPath);
                    foreach (var p in parsed)
                    {
                        xmlPointPaths[p.Address] = p.Path;
                    }
                }
            }

            // Cache of created folder nodes keyed by their accumulated sub-path under the device node.
            var folderCache = new Dictionary<string, AssetNodeDto>(StringComparer.OrdinalIgnoreCase);

            if (points != null)
            {
                foreach (var point in points)
                {
                    string pointKey = $"{device.Id}_{point.Key}";   // globally unique key

                    string niceName = !string.IsNullOrWhiteSpace(point.Name)
                        ? point.Name
                        : System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(point.Key.Replace("_", " "));

                    string desc = $"KNX Group Address: {point.GroupAddress} (DPT {point.Dpt})";
                    if (!string.IsNullOrWhiteSpace(point.Description))
                    {
                        desc = $"{point.Description} ({desc})";
                    }

                    // Resolve the folder chain (group ranges) for this point from the XML export.
                    var xmlSubPath = xmlPointPaths.TryGetValue(point.GroupAddress, out var sub) && sub != null
                        ? sub
                        : new List<string>();

                    // Walk/create the folder nodes under the device node so the tree nests properly.
                    var parentNode = deviceNode;
                    var pathAccum = new List<PathSegmentDto>(parentPath)
                    {
                        new PathSegmentDto { Id = device.Id, Name = device.Name }
                    };
                    string pathKey = "";

                    foreach (var rawSegment in xmlSubPath)
                    {
                        var segment = rawSegment?.Trim();
                        if (string.IsNullOrWhiteSpace(segment)) continue;

                        pathKey = string.IsNullOrEmpty(pathKey) ? segment : $"{pathKey}/{segment}";
                        string folderId = $"{device.Id}_{AssetNodeDto.PathSegmentId(pathKey)}";

                        if (!folderCache.TryGetValue(pathKey, out var folderNode))
                        {
                            folderNode = new AssetNodeDto
                            {
                                Id = folderId,
                                Name = segment,
                                Type = "Folder",
                                IsView = true
                            };
                            folderCache[pathKey] = folderNode;
                            parentNode.Children.Add(folderNode);
                        }

                        pathAccum = new List<PathSegmentDto>(pathAccum)
                        {
                            new PathSegmentDto { Id = folderId, Name = segment }
                        };
                        parentNode = folderNode;
                    }

                    var pDto = new TelemetryDto
                    {
                        Id = pointKey,
                        Name = niceName,
                        FullName = $"{device.Name} / {niceName}",
                        Description = desc,
                        Units = point.Units ?? "",
                        Type = point.Dpt.StartsWith("1.") ? "Binary" : "Analog",
                        Key = pointKey,
                        IsWritable = point.Writable,
                        ParentId = parentNode.Id,
                        ParentPath = pathAccum
                    };

                    parentNode.Telemetries.Add(pDto);
                }
            }

            return deviceNode;
        }

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(ConnectionConfig connection, DeviceConfig device, string key)
        {
            var properties = new List<PropertyDto>();
            var points = GetEffectivePoints(device);
            
            if (points == null || points.Count == 0)
                return Task.FromResult(properties);

            // Strip the device ID prefix from the telemetry key
            string pointKey = key.StartsWith(device.Id + "_") 
                ? key.Substring(device.Id.Length + 1) 
                : key;

            var point = points.FirstOrDefault(p => p.Key.Equals(pointKey, StringComparison.OrdinalIgnoreCase));
            if (point != null)
            {
                properties.Add(new PropertyDto { Name = "Group Address", Value = point.GroupAddress });
                properties.Add(new PropertyDto { Name = "Datapoint Type (DPT)", Value = point.Dpt });
                properties.Add(new PropertyDto { Name = "Writable", Value = point.Writable ? "Yes" : "No" });
                if (!string.IsNullOrWhiteSpace(point.Description))
                {
                    properties.Add(new PropertyDto { Name = "Description", Value = point.Description });
                }
            }

            return Task.FromResult(properties);
        }

        // IDeviceWriter methods
        public void Write(ConnectionConfig connection, DeviceConfig device, string key, double value)
        {
            var points = GetEffectivePoints(device);
            if (points == null || points.Count == 0)
                throw new InvalidOperationException($"Device '{device.Name}' has no KNX points configured.");

            var point = points.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (point == null)
                throw new KeyNotFoundException($"Datapoint key '{key}' not found on device '{device.Name}'");

            if (!point.Writable)
                throw new InvalidOperationException($"Datapoint '{point.Key}' ({point.GroupAddress}) is read-only.");

            if (!KnxConnection.TryGetConnection(connection.Id, out var knxConn) || knxConn == null)
                throw new InvalidOperationException($"No active connection found for ID '{connection.Id}' to write value.");

            try
            {
                ushort groupAddr = KnxConnection.ParseGroupAddress(point.GroupAddress);
                byte[] encodedBytes = KnxDpt.Encode(value, point.Dpt, out bool isSmall);

                knxConn.SendGroupWrite(groupAddr, encodedBytes, isSmall).GetAwaiter().GetResult();
                Log.Info($"[KNX] Wrote value {value} to {point.GroupAddress} ({point.Key}) via connection '{connection.Id}'");
            }
            catch (Exception ex)
            {
                Log.Error($"[KNX] Write failed for point '{point.Key}' ({point.GroupAddress}): {ex.Message}");
                throw;
            }
        }

        public void WriteComplex(ConnectionConfig connection, DeviceConfig device, string key, object value)
        {
            throw new NotSupportedException("Complex writes are not supported by the KNX driver.");
        }

        public bool IsWritable(string key)
        {
            return true; 
        }

        // ── XML Parsing Helpers ────────────────────────────────────────────────

        private List<KnxPointConfig> GetEffectivePoints(DeviceConfig device)
        {
            var effectivePoints = new List<KnxPointConfig>();
            var xmlPoints = new Dictionary<string, ParsedKnxPoint>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(device.KnxGroupAddressXml))
            {
                string? resolvedPath = ResolveConfigRelativePath(device.KnxGroupAddressXml);
                if (resolvedPath != null && File.Exists(resolvedPath))
                {
                    var parsed = GetParsedXmlPoints(resolvedPath);
                    foreach (var p in parsed)
                    {
                        xmlPoints[p.Address] = p;
                    }
                }
            }

            // 1. If explicit points are defined in config, merge with XML metadata
            if (device.KnxPoints != null && device.KnxPoints.Count > 0)
            {
                foreach (var pt in device.KnxPoints)
                {
                    string key = pt.Key;
                    string groupAddr = pt.GroupAddress;
                    string dpt = pt.Dpt;
                    string? units = pt.Units;
                    bool writable = pt.Writable;
                    string? name = pt.Name;
                    string? description = pt.Description;

                    if (xmlPoints.TryGetValue(groupAddr, out var xmlPt))
                    {
                        if (string.IsNullOrWhiteSpace(name)) name = xmlPt.Name;
                        if (string.IsNullOrWhiteSpace(description)) description = xmlPt.Description;
                        if (string.IsNullOrWhiteSpace(dpt)) dpt = xmlPt.Dpt;
                    }

                    effectivePoints.Add(new KnxPointConfig(
                        Key: key,
                        GroupAddress: groupAddr,
                        Dpt: dpt,
                        Units: units,
                        Writable: writable,
                        Name: name,
                        Description: description
                    ));
                }
            }
            // 2. If no explicit points configured, import all points from XML
            else if (xmlPoints.Count > 0)
            {
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var xmlPt in xmlPoints.Values)
                {
                    string baseKey = SanitizeKey(xmlPt.Name ?? xmlPt.Address);
                    string key = baseKey;
                    int counter = 1;
                    while (!keys.Add(key))
                    {
                        key = $"{baseKey}_{counter++}";
                    }

                    effectivePoints.Add(new KnxPointConfig(
                        Key: key,
                        GroupAddress: xmlPt.Address,
                        Dpt: xmlPt.Dpt,
                        Units: GetDefaultUnitsForDpt(xmlPt.Dpt),
                        Writable: true, // Default to writable for dynamic XML discovery
                        Name: xmlPt.Name,
                        Description: xmlPt.Description
                    ));
                }
            }

            return effectivePoints;
        }

        private static List<ParsedKnxPoint> GetParsedXmlPoints(string filePath)
        {
            try
            {
                var writeTime = File.GetLastWriteTime(filePath);
                if (_xmlCache.TryGetValue(filePath, out var cached) && cached.LastWrite == writeTime)
                {
                    return cached.Points;
                }

                var points = KnxXmlParser.Parse(filePath);
                _xmlCache[filePath] = (writeTime, points);
                return points;
            }
            catch (Exception ex)
            {
                Log.Error($"[KNX] Error caching XML export file: {ex.Message}");
                return new List<ParsedKnxPoint>();
            }
        }

        private string? ResolveConfigRelativePath(string relativeOrAbsolutePath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath)) return null;
            if (Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return File.Exists(relativeOrAbsolutePath) ? relativeOrAbsolutePath : null;
            }

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var targetFile = Path.Combine(dir.FullName, relativeOrAbsolutePath);
                if (File.Exists(targetFile)) return targetFile;

                var configJson = Path.Combine(dir.FullName, "pulswerk.json");
                if (File.Exists(configJson))
                {
                    var targetNearJson = Path.Combine(dir.FullName, relativeOrAbsolutePath);
                    if (File.Exists(targetNearJson)) return targetNearJson;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private static string SanitizeKey(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "point_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var chars = input.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            string key = new string(chars).ToLowerInvariant();

            while (key.Contains("__")) key = key.Replace("__", "_");
            key = key.Trim('_');

            if (string.IsNullOrWhiteSpace(key))
                return "point_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            return key;
        }

        private static string? GetDefaultUnitsForDpt(string dpt)
        {
            if (string.IsNullOrWhiteSpace(dpt)) return null;
            if (dpt.StartsWith("9."))
            {
                if (dpt == "9.001") return "°C";
                if (dpt == "9.007") return "%";
            }
            else if (dpt.StartsWith("14."))
            {
                if (dpt == "14.056") return "W";
                if (dpt == "14.060") return "Hz";
            }
            return null;
        }
    }
}
