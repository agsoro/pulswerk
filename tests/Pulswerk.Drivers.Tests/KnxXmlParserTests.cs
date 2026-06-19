using System;
using System.IO;
using Xunit;
using Pulswerk.Drivers.Knx;

namespace Pulswerk.Drivers.Tests
{
    public class KnxXmlParserTests
    {
        [Fact]
        public void TestDptNormalization()
        {
            Assert.Equal("1.001", KnxDpt.NormalizeDpt("DPST-1-1"));
            Assert.Equal("9.001", KnxDpt.NormalizeDpt("DPST-9-1"));
            Assert.Equal("14.056", KnxDpt.NormalizeDpt("DPST-14-56"));
            Assert.Equal("9.001", KnxDpt.NormalizeDpt("DPS-9-1"));
            Assert.Equal("9.001", KnxDpt.NormalizeDpt("9.001"));
            Assert.Equal("1.001", KnxDpt.NormalizeDpt(""));
            Assert.Equal("1.001", KnxDpt.NormalizeDpt(null));
            Assert.Equal("custom-type", KnxDpt.NormalizeDpt("custom-type"));
        }

        [Fact]
        public void TestXmlParserWithHierarchyAndDpt()
        {
            string xmlContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<GroupAddress-Export xmlns=""http://knx.org/xml/telecontrol/1.0"">
  <GroupRange Name=""Building A"" RangeStart=""0"" RangeEnd=""2047"">
    <GroupRange Name=""First Floor"" RangeStart=""0"" RangeEnd=""255"">
      <GroupAddress Name=""Temperature Sensor"" Address=""1/1/10"" Description=""Living room temp"" DPT=""DPST-9-1"" />
      <GroupAddress Name=""Ceiling Light"" Address=""1/2/1"" Description=""Main light switch"" DatapointType=""DPST-1-1"" />
      <GroupAddress Name=""Empty Address"" Address="""" DPT=""DPST-1-1"" />
    </GroupRange>
  </GroupRange>
</GroupAddress-Export>";

            string tempFile = Path.Combine(AppContext.BaseDirectory, $"temp_knx_export_{Guid.NewGuid():N}.xml");
            try
            {
                File.WriteAllText(tempFile, xmlContent);

                var points = KnxXmlParser.Parse(tempFile);

                Assert.NotNull(points);
                Assert.Equal(2, points.Count); // 2 valid addresses, Empty Address should be skipped

                // Verify Point 1: Temperature Sensor
                var p1 = points[0];
                Assert.Equal("1/1/10", p1.Address);
                Assert.Equal("Temperature Sensor", p1.Name);
                Assert.Equal("Living room temp", p1.Description);
                Assert.Equal("9.001", p1.Dpt);
                Assert.Equal(2, p1.Path.Count);
                Assert.Equal("Building A", p1.Path[0]);
                Assert.Equal("First Floor", p1.Path[1]);

                // Verify Point 2: Ceiling Light
                var p2 = points[1];
                Assert.Equal("1/2/1", p2.Address);
                Assert.Equal("Ceiling Light", p2.Name);
                Assert.Equal("Main light switch", p2.Description);
                Assert.Equal("1.001", p2.Dpt);
                Assert.Equal(2, p2.Path.Count);
                Assert.Equal("Building A", p2.Path[0]);
                Assert.Equal("First Floor", p2.Path[1]);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
