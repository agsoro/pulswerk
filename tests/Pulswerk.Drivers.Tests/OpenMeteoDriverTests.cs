using System.Collections.Generic;
using Pulswerk.Core;
using Pulswerk.Drivers.Weather;
using Xunit;

namespace Pulswerk.Drivers.Tests
{
    public class OpenMeteoDriverTests
    {
        [Fact]
        public void ParseCurrentResponse_MapsWeatherValues()
        {
            const string json = """
            {
              "current": {
                "temperature_2m": 18.4,
                "precipitation": 0.2,
                "shortwave_radiation": 512.0
              }
            }
            """;

            var values = OpenMeteoDriver.ParseCurrentResponse(json);

            Assert.Equal(18.4, (double)values["temperature"]);
            Assert.Equal(0.2, (double)values["precipitation"]);
            Assert.Equal(512.0, (double)values["radiance"]);
        }

        [Fact]
        public void Driver_ExposesExpectedKeysAndUnits()
        {
            var driver = new OpenMeteoDriver();
            Assert.Equal(new[] { "temperature", "precipitation", "radiance" }, driver.GetTelemetryKeys());

            var units = driver.GetTelemetryUnits();
            Assert.Equal(Units.Celsius, units["temperature"]);
            Assert.Equal(Units.Millimeter, units["precipitation"]);
            Assert.Equal(Units.WattPerSquareMeter, units["radiance"]);
        }

        [Fact]
        public void AssetHierarchy_UsesLocationDevicePoints()
        {
            var driver = new OpenMeteoDriver();
            var device = new DeviceConfig("weather", "Weather Station", "open-meteo",
                Path: new List<string> { "Weather" });

            var node = driver.GetAssetHierarchy(device);

            Assert.Equal("Weather", node.Type);
            Assert.Equal(3, node.Telemetries.Count);
            Assert.Contains(node.Telemetries, point => point.Id == "weather_radiance");
        }
    }
}