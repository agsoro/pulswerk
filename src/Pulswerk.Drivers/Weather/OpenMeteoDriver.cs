using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Pulswerk.Core;

namespace Pulswerk.Drivers.Weather
{
    using TelemetryValues = Dictionary<string, object>;

    /// <summary>
    /// Reads current weather from Open-Meteo, a free weather API hosted in Germany.
    /// No API key is required for non-commercial use.
    /// </summary>
    public sealed class OpenMeteoDriver : IDeviceDriver
    {
        private static readonly HttpClient _httpClient = new()
        {
            BaseAddress = new Uri("https://api.open-meteo.com/"),
            Timeout = TimeSpan.FromSeconds(15)
        };
        private double? _latitude;
        private double? _longitude;
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private TelemetryValues? _cachedValues;

        public string DriverName => "open-meteo";
        public bool IsBusy => false;

        public IEnumerable<string> GetTelemetryKeys() => new[]
        {
            "temperature",
            "precipitation",
            "radiance"
        };

        public IReadOnlyDictionary<string, string> GetTelemetryUnits() => new Dictionary<string, string>
        {
            ["temperature"] = Units.Celsius,
            ["precipitation"] = Units.Millimeter,
            ["radiance"] = Units.WattPerSquareMeter
        };

        public void ConfigureLocation(double? latitude, double? longitude)
        {
            _latitude = latitude;
            _longitude = longitude;
        }

        public TelemetryValues Read(ConnectionConfig connection, DeviceConfig device)
        {
            if (!_latitude.HasValue || !_longitude.HasValue)
                throw new InvalidOperationException(
                    "Global latitude and longitude are required for the Open-Meteo driver.");

            int intervalSeconds = Math.Clamp(device.PollIntervalSeconds ?? 900, 300, 1800);
            if (_cachedValues != null && DateTime.UtcNow - _lastFetchUtc < TimeSpan.FromSeconds(intervalSeconds))
                return new TelemetryValues(_cachedValues);

            string latitude = _latitude.Value.ToString(CultureInfo.InvariantCulture);
            string longitude = _longitude.Value.ToString(CultureInfo.InvariantCulture);
            string url = "v1/forecast?latitude=" + Uri.EscapeDataString(latitude)
                + "&longitude=" + Uri.EscapeDataString(longitude)
                + "&current=temperature_2m,precipitation,shortwave_radiation"
                + "&timezone=auto";

            using var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var values = ParseCurrentResponse(json);
            _cachedValues = values;
            _lastFetchUtc = DateTime.UtcNow;
            return new TelemetryValues(values);
        }

        public static TelemetryValues ParseCurrentResponse(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("current", out var current))
                throw new FormatException("Open-Meteo response did not contain current weather data.");

            var values = new TelemetryValues();
            AddNumber(current, "temperature_2m", "temperature", values);
            AddNumber(current, "precipitation", "precipitation", values);
            AddNumber(current, "shortwave_radiation", "radiance", values);
            return values;
        }

        private static void AddNumber(JsonElement source, string sourceName, string key, TelemetryValues target)
        {
            if (source.TryGetProperty(sourceName, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out var number))
                target[key] = number;
        }

        public AssetNodeDto GetAssetHierarchy(DeviceConfig device)
        {
            var parentPath = (device.Path ?? new List<string>())
                .Select(segment => new PathSegmentDto
                {
                    Id = AssetNodeDto.PathSegmentId(segment),
                    Name = segment
                })
                .ToList();

            var node = new AssetNodeDto
            {
                Id = device.Id,
                Name = device.Name,
                Type = device.AssetType ?? "Weather",
                IsView = true
            };

            var names = new Dictionary<string, string>
            {
                ["temperature"] = "Temperature",
                ["precipitation"] = "Precipitation",
                ["radiance"] = "Solar Radiance"
            };

            foreach (var key in GetTelemetryKeys())
            {
                string name = names[key];
                node.Telemetries.Add(new TelemetryDto
                {
                    Id = $"{device.Id}_{key}",
                    Name = name,
                    FullName = $"{device.Name} / {name}",
                    Description = $"Open-Meteo weather point: {key}",
                    Units = GetTelemetryUnits()[key],
                    Type = "Analog",
                    Key = $"{device.Id}_{key}",
                    IsWritable = false,
                    ParentPath = parentPath
                });

                if (device.Path != null && device.Path.Count > 0)
                    node.Telemetries[^1].ParentId = AssetNodeDto.PathSegmentId(device.Path[^1]);
            }

            return node;
        }

        public Task<List<PropertyDto>> GetExtendedPropertiesAsync(
            ConnectionConfig connection, DeviceConfig device, string key)
            => Task.FromResult(new List<PropertyDto>());
    }
}