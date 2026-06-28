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
                    knxConn.RegisterAddress(groupAddr); // ensure the throttled read sweep covers this point
                    byte[]? rawBytes = knxConn.GetCachedValue(groupAddr);

                    if (rawBytes != null)
                    {
                        telemetryValues[point.Key] = DecodePointValue(rawBytes, point.Dpt);
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

            // Now that all configured addresses are registered, kick off the throttled
            // read sweep (no-op if it already ran for this connection).
            knxConn.TriggerReadSweep();

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

        // ── Value decoding ─────────────────────────────────────────────────────

        /// <summary>
        /// Decodes a raw KNX payload for a given DPT into the telemetry value as it should
        /// be surfaced to the dashboard. DPT 1.xxx booleans are converted to their
        /// standardized lower-case state label (e.g. "on"/"off", "open"/"close") instead of
        /// a raw boolean (which would otherwise render as "True"/"False").
        /// </summary>
        private static object DecodePointValue(byte[] rawBytes, string dpt)
        {
            object decodedValue = KnxDpt.Decode(rawBytes, dpt);

            if (decodedValue is bool boolValue)
            {
                string? stateLabel = KnxDpt.GetDpt1StateLabel(boolValue, dpt);
                if (stateLabel != null)
                    decodedValue = stateLabel.ToLowerInvariant();
            }

            return decodedValue;
        }

        /// <summary>
        /// Decodes an unsolicited bus telegram (a spontaneous <c>GroupValueWrite</c>) for the
        /// given device into the set of telemetry key/value pairs it affects. A single group
        /// address may back more than one configured point, so the result can contain
        /// multiple entries. For parity with the normal poll path
        /// (<see cref="DevicePoller"/>), each affected point is emitted under both its
        /// unscoped key (<c>pointKey</c>) and its globally-unique device-scoped key
        /// (<c>"{deviceId}_{pointKey}"</c>, matching <see cref="GetAssetHierarchy"/>).
        /// Returns an empty dictionary when no point of the device maps to
        /// <paramref name="groupAddress"/>.
        /// </summary>
        public Dictionary<string, object> DecodePushUpdate(DeviceConfig device, ushort groupAddress, byte[] payload)
        {
            var result = new Dictionary<string, object>();
            if (payload == null || payload.Length == 0)
                return result;

            var points = GetEffectivePoints(device);
            if (points == null || points.Count == 0)
                return result;

            foreach (var point in points)
            {
                ushort pointAddr;
                try { pointAddr = KnxConnection.ParseGroupAddress(point.GroupAddress); }
                catch { continue; }

                if (pointAddr != groupAddress)
                    continue;

                try
                {
                    object value = DecodePointValue(payload, point.Dpt);
                    result[point.Key] = value;
                    result[$"{device.Id}_{point.Key}"] = value;
                }
                catch (Exception ex)
                {
                    Log.Debug($"[KNX] Failed to decode pushed value for point '{point.Key}' ({point.GroupAddress}): {ex.Message}");
                }
            }

            return result;
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
            dpt = dpt.Trim();

            // Exact sub-type match first (units as defined by the KNX DPT specification).
            if (DptUnits.TryGetValue(dpt, out var unit))
                return unit;

            // DPT 1.xxx binary datapoints have no engineering unit. The value itself is
            // rendered as a meaningful state label (e.g. "on"/"off") in KnxDriver.Read,
            // so no unit should be attached.
            if (KnxDpt.GetDpt1States(dpt) != null)
                return null;

            return null;
        }

        // Engineering units for the individual KNX datapoint sub-types (DPT_xx.yyy),
        // as defined by the KNX Association specification.
        private static readonly Dictionary<string, string> DptUnits = new(StringComparer.OrdinalIgnoreCase)
        {
            // 5.xxx — 8-bit unsigned value
            ["5.001"] = "%",       // DPT_Scaling
            ["5.003"] = "°",       // DPT_Angle
            ["5.004"] = "%",       // DPT_Percent_U8
            ["5.005"] = "ratio",   // DPT_DecimalFactor
            ["5.006"] = "tariff",  // DPT_Tariff

            // 6.xxx — 8-bit signed value
            ["6.001"] = "%",       // DPT_Percent_V8

            // 7.xxx — 2-byte unsigned value
            ["7.002"] = "ms",      // DPT_TimePeriodMsec
            ["7.003"] = "ms",      // DPT_TimePeriod10Msec (10 ms resolution)
            ["7.004"] = "ms",      // DPT_TimePeriod100Msec (100 ms resolution)
            ["7.005"] = "s",       // DPT_TimePeriodSec
            ["7.006"] = "min",     // DPT_TimePeriodMin
            ["7.007"] = "h",       // DPT_TimePeriodHrs
            ["7.011"] = "mm",      // DPT_Length_mm
            ["7.012"] = "mA",      // DPT_UElCurrentmA
            ["7.013"] = "lx",      // DPT_Brightness
            ["7.600"] = "K",       // DPT_Absolute_Colour_Temperature

            // 8.xxx — 2-byte signed value
            ["8.002"] = "ms",      // DPT_DeltaTimeMsec
            ["8.003"] = "ms",      // DPT_DeltaTime10Msec
            ["8.004"] = "ms",      // DPT_DeltaTime100Msec
            ["8.005"] = "s",       // DPT_DeltaTimeSec
            ["8.006"] = "min",     // DPT_DeltaTimeMin
            ["8.007"] = "h",       // DPT_DeltaTimeHrs
            ["8.010"] = "%",       // DPT_Percent_V16
            ["8.011"] = "°",       // DPT_Rotation_Angle
            ["8.012"] = "m",       // DPT_Length_m

            // 9.xxx — 2-byte float
            ["9.001"] = "°C",      // DPT_Value_Temp
            ["9.002"] = "K",       // DPT_Value_Tempd
            ["9.003"] = "K/h",     // DPT_Value_Tempa
            ["9.004"] = "lx",      // DPT_Value_Lux
            ["9.005"] = "m/s",     // DPT_Value_Wsp
            ["9.006"] = "Pa",      // DPT_Value_Pres
            ["9.007"] = "%",       // DPT_Value_Humidity
            ["9.008"] = "ppm",     // DPT_Value_AirQuality
            ["9.009"] = "m³/h",    // DPT_Value_AirFlow
            ["9.010"] = "s",       // DPT_Value_Time1
            ["9.011"] = "ms",      // DPT_Value_Time2
            ["9.020"] = "mV",      // DPT_Value_Volt
            ["9.021"] = "mA",      // DPT_Value_Curr
            ["9.022"] = "W/m²",    // DPT_PowerDensity
            ["9.023"] = "K/%",     // DPT_KelvinPerPercent
            ["9.024"] = "kW",      // DPT_Power
            ["9.025"] = "l/h",     // DPT_Value_Volume_Flow
            ["9.026"] = "l/h",     // DPT_Rain_Amount
            ["9.027"] = "°F",      // DPT_Value_Temp_F
            ["9.028"] = "km/h",    // DPT_Value_Wsp_kmh
            ["9.029"] = "g/m³",    // DPT_Value_Absolute_Humidity
            ["9.030"] = "µg/m³",   // DPT_Concentration_µgm3

            // 12.xxx — 4-byte unsigned value
            ["12.100"] = "s",      // DPT_LongDeltaTimeSec
            ["12.1200"] = "l",     // DPT_VolumeLiquid_Litre
            ["12.1201"] = "m³",    // DPT_Volume_m3

            // 13.xxx — 4-byte signed value
            ["13.002"] = "m³/h",   // DPT_FlowRate_m3/h
            ["13.010"] = "Wh",     // DPT_ActiveEnergy
            ["13.011"] = "VAh",    // DPT_ApparantEnergy
            ["13.012"] = "VARh",   // DPT_ReactiveEnergy
            ["13.013"] = "kWh",    // DPT_ActiveEnergy_kWh
            ["13.014"] = "kVAh",   // DPT_ApparantEnergy_kVAh
            ["13.015"] = "kVARh",  // DPT_ReactiveEnergy_kVARh
            ["13.016"] = "MWh",    // DPT_ActiveEnergy_MWh
            ["13.100"] = "s",      // DPT_LongDeltaTimeSec

            // 14.xxx — 4-byte float (IEEE 754)
            ["14.000"] = "m/s²",   // DPT_Value_Acceleration
            ["14.001"] = "rad/s²", // DPT_Value_Acceleration_Angular
            ["14.002"] = "J/mol",  // DPT_Value_Activation_Energy
            ["14.003"] = "1/s",    // DPT_Value_Activity
            ["14.004"] = "mol",    // DPT_Value_Mol
            ["14.005"] = "ratio",  // DPT_Value_Amplitude
            ["14.006"] = "rad",    // DPT_Value_AngleRad
            ["14.007"] = "°",      // DPT_Value_AngleDeg
            ["14.008"] = "J·s",    // DPT_Value_Angular_Momentum
            ["14.009"] = "rad/s",  // DPT_Value_Angular_Velocity
            ["14.010"] = "m²",     // DPT_Value_Area
            ["14.011"] = "F",      // DPT_Value_Capacitance
            ["14.012"] = "C/m²",   // DPT_Value_Charge_DensitySurface
            ["14.013"] = "C/m³",   // DPT_Value_Charge_DensityVolume
            ["14.014"] = "m²/N",   // DPT_Value_Compressibility
            ["14.015"] = "S",      // DPT_Value_Conductance
            ["14.016"] = "S/m",    // DPT_Value_Electrical_Conductivity
            ["14.017"] = "kg/m³",  // DPT_Value_Density
            ["14.018"] = "C",      // DPT_Value_Electric_Charge
            ["14.019"] = "A",      // DPT_Value_Electric_Current
            ["14.020"] = "A/m²",   // DPT_Value_Electric_CurrentDensity
            ["14.021"] = "C·m",    // DPT_Value_Electric_DipoleMoment
            ["14.022"] = "C/m²",   // DPT_Value_Electric_Displacement
            ["14.023"] = "V/m",    // DPT_Value_Electric_FieldStrength
            ["14.024"] = "C",      // DPT_Value_Electric_Flux
            ["14.025"] = "C/m²",   // DPT_Value_Electric_FluxDensity
            ["14.026"] = "C/m²",   // DPT_Value_Electric_Polarization
            ["14.027"] = "V",      // DPT_Value_Electric_Potential
            ["14.028"] = "V",      // DPT_Value_Electric_PotentialDifference
            ["14.029"] = "A·m²",   // DPT_Value_ElectromagneticMoment
            ["14.030"] = "V",      // DPT_Value_Electromotive_Force
            ["14.031"] = "J",      // DPT_Value_Energy
            ["14.032"] = "N",      // DPT_Value_Force
            ["14.033"] = "Hz",     // DPT_Value_Frequency
            ["14.034"] = "rad/s",  // DPT_Value_Angular_Frequency
            ["14.035"] = "J/K",    // DPT_Value_Heat_Capacity
            ["14.036"] = "W",      // DPT_Value_Heat_FlowRate
            ["14.037"] = "J",      // DPT_Value_Heat_Quantity
            ["14.038"] = "Ω",      // DPT_Value_Impedance
            ["14.039"] = "m",      // DPT_Value_Length
            ["14.040"] = "J",      // DPT_Value_Light_Quantity
            ["14.041"] = "cd/m²",  // DPT_Value_Luminance
            ["14.042"] = "lm",     // DPT_Value_Luminous_Flux
            ["14.043"] = "cd",     // DPT_Value_Luminous_Intensity
            ["14.044"] = "A/m",    // DPT_Value_Magnetic_FieldStrength
            ["14.045"] = "Wb",     // DPT_Value_Magnetic_Flux
            ["14.046"] = "T",      // DPT_Value_Magnetic_FluxDensity
            ["14.047"] = "A·m²",   // DPT_Value_Magnetic_Moment
            ["14.048"] = "T",      // DPT_Value_Magnetic_Polarization
            ["14.049"] = "A/m",    // DPT_Value_Magnetization
            ["14.050"] = "A",      // DPT_Value_MagnetomotiveForce
            ["14.051"] = "kg",     // DPT_Value_Mass
            ["14.052"] = "kg/s",   // DPT_Value_MassFlux
            ["14.053"] = "kg·m/s", // DPT_Value_Momentum
            ["14.054"] = "rad",    // DPT_Value_Phase_AngleRad
            ["14.055"] = "°",      // DPT_Value_Phase_AngleDeg
            ["14.056"] = "W",      // DPT_Value_Power
            ["14.057"] = "cosΦ",   // DPT_Value_Power_Factor
            ["14.058"] = "Pa",     // DPT_Value_Pressure
            ["14.059"] = "Ω",      // DPT_Value_Reactance
            ["14.060"] = "Ω",      // DPT_Value_Resistance
            ["14.061"] = "Ω·m",    // DPT_Value_Resistivity
            ["14.062"] = "H",      // DPT_Value_SelfInductance
            ["14.063"] = "sr",     // DPT_Value_SolidAngle
            ["14.064"] = "W/m²",   // DPT_Value_Sound_Intensity
            ["14.065"] = "m/s",    // DPT_Value_Speed
            ["14.066"] = "Pa",     // DPT_Value_Stress
            ["14.067"] = "N/m",    // DPT_Value_Surface_Tension
            ["14.068"] = "°C",     // DPT_Value_Common_Temperature
            ["14.069"] = "K",      // DPT_Value_Absolute_Temperature
            ["14.070"] = "K",      // DPT_Value_TemperatureDifference
            ["14.071"] = "J/K",    // DPT_Value_Thermal_Capacity
            ["14.072"] = "W/(m·K)",// DPT_Value_Thermal_Conductivity
            ["14.073"] = "V/K",    // DPT_Value_ThermoelectricPower
            ["14.074"] = "s",      // DPT_Value_Time
            ["14.075"] = "N·m",    // DPT_Value_Torque
            ["14.076"] = "m³",     // DPT_Value_Volume
            ["14.077"] = "m³/s",   // DPT_Value_Volume_Flux
            ["14.078"] = "N",      // DPT_Value_Weight
            ["14.079"] = "J",      // DPT_Value_Work
            ["14.080"] = "VA",     // DPT_Value_ApparentPower
        };
    }
}
