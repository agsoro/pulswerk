using System.IO.BACnet;
using Pulswerk.Drivers.BACnet;
using Xunit;

namespace Pulswerk.Drivers.Tests;

public class BacnetValueConverterTests
{
    // ═══════════════════════════════════════════════════════════════════════
    //  IsBinary
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_INPUT, true)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_OUTPUT, true)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE, true)]
    [InlineData(BacnetObjectTypes.OBJECT_CALENDAR, true)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_INPUT, false)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, false)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_VALUE, false)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, false)]
    [InlineData(BacnetObjectTypes.OBJECT_INTEGER_VALUE, false)]
    public void IsBinary_ClassifiesCorrectly(BacnetObjectTypes type, bool expected)
    {
        Assert.Equal(expected, BacnetValueConverter.IsBinary(type));
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT, true)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT, true)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, true)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_VALUE, false)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE, false)]
    public void IsMultiState_ClassifiesCorrectly(BacnetObjectTypes type, bool expected)
    {
        Assert.Equal(expected, BacnetValueConverter.IsMultiState(type));
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE, true)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, true)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_VALUE, false)]
    public void IsEnumerated_CoversBinaryAndMultiState(BacnetObjectTypes type, bool expected)
    {
        Assert.Equal(expected, BacnetValueConverter.IsEnumerated(type));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TryToDouble
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryToDouble_Null_ReturnsFalse()
    {
        Assert.False(BacnetValueConverter.TryToDouble(null, out var r));
        Assert.Equal(0, r);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1, 1.0)]
    [InlineData(42, 42.0)]
    [InlineData(-5, -5.0)]
    public void TryToDouble_Int_Converts(int input, double expected)
    {
        Assert.True(BacnetValueConverter.TryToDouble(input, out var r));
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.5, 1.5)]
    [InlineData(23.456789, 23.456789)]
    public void TryToDouble_Double_Converts(double input, double expected)
    {
        Assert.True(BacnetValueConverter.TryToDouble(input, out var r));
        Assert.Equal(expected, r, 6);
    }

    [Theory]
    [InlineData((uint)0, 0.0)]
    [InlineData((uint)1, 1.0)]
    public void TryToDouble_UInt_Converts(uint input, double expected)
    {
        Assert.True(BacnetValueConverter.TryToDouble(input, out var r));
        Assert.Equal(expected, r);
    }

    [Theory]
    [InlineData((float)0.0f, 0.0)]
    [InlineData(3.14f, 3.14)]
    public void TryToDouble_Float_Converts(float input, double expected)
    {
        Assert.True(BacnetValueConverter.TryToDouble(input, out var r));
        Assert.Equal(expected, r, 2);
    }

    [Fact]
    public void TryToDouble_String_ReturnsFalse()
    {
        Assert.False(BacnetValueConverter.TryToDouble("hello", out _));
    }

    [Fact]
    public void TryToDouble_NumericString_Converts()
    {
        // Converter uses InvariantCulture – dot is always the decimal separator
        Assert.True(BacnetValueConverter.TryToDouble("42.5", out var r));
        Assert.Equal(42.5, r);
    }

    [Fact]
    public void TryToDouble_ClrEnum_ReturnsFalse()
    {
        // CLR enums (BacnetObjectTypes etc.) are metadata, not numeric values
        Assert.False(BacnetValueConverter.TryToDouble(BacnetObjectTypes.OBJECT_ANALOG_INPUT, out _));
    }

    [Fact]
    public void TryToDouble_BacnetBitString_ReturnsFalse()
    {
        // BacnetBitString is a bitmask (status flags), not a scalar value
        var bs = BacnetBitString.Parse("1010");
        Assert.False(BacnetValueConverter.TryToDouble(bs, out _));
    }

    [Fact]
    public void FormatValue_BacnetBitString_ReturnsIntBitmask()
    {
        // BacnetBitString → int bitmask (e.g. bits "1100" → 0b0011 = 3, LSB first)
        var bs = BacnetBitString.Parse("1100");
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, bs);
        Assert.IsType<int>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DefaultValue
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DefaultValue_Binary_ReturnsIntZero()
    {
        var val = BacnetValueConverter.DefaultValue(true);
        Assert.IsType<int>(val);
        Assert.Equal(0, val);
    }

    [Fact]
    public void DefaultValue_Analog_ReturnsDoubleZero()
    {
        var val = BacnetValueConverter.DefaultValue(false);
        Assert.IsType<double>(val);
        Assert.Equal(0.0, val);
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_INPUT)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_OUTPUT)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE)]
    public void DefaultValue_ByType_EnumeratedReturnsInt(BacnetObjectTypes type)
    {
        Assert.IsType<int>(BacnetValueConverter.DefaultValue(type));
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_INPUT)]
    public void DefaultValue_ByType_AnalogReturnsDouble(BacnetObjectTypes type)
    {
        Assert.IsType<double>(BacnetValueConverter.DefaultValue(type));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – Binary objects
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_INPUT)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_OUTPUT)]
    public void FormatValue_BinaryNull_ReturnsIntZero(BacnetObjectTypes type)
    {
        var result = BacnetValueConverter.FormatValue(type, BacnetPropertyIds.PROP_PRESENT_VALUE, null);
        Assert.IsType<int>(result);
        Assert.Equal(0, result);
    }

    [Fact]
    public void FormatValue_BinaryZero_ReturnsIntZero()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)0);
        Assert.IsType<int>(result);
        Assert.Equal(0, result);
    }

    [Fact]
    public void FormatValue_BinaryOne_ReturnsIntOne()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)1);
        Assert.IsType<int>(result);
        Assert.Equal(1, result);
    }

    [Fact]
    public void FormatValue_BinaryDouble_TruncatesToInt()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_OUTPUT,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 1.0);
        Assert.IsType<int>(result);
        Assert.Equal(1, result);
    }

    [Fact]
    public void FormatValue_BinaryWithStateText_ReturnsText()
    {
        var stateText = new List<string> { "Aus", "Ein" };
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)1, stateText);
        Assert.IsType<string>(result);
        Assert.Equal("Ein", result);
    }

    [Fact]
    public void FormatValue_BinaryWithStateText_ZeroIndex()
    {
        var stateText = new List<string> { "OFF", "ON" };
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)0, stateText);
        Assert.IsType<string>(result);
        Assert.Equal("OFF", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – Analog objects
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_AnalogNull_ReturnsDoubleZero()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, null);
        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    [Fact]
    public void FormatValue_AnalogFloat_ReturnsRoundedDouble()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 21.12345678f);
        Assert.IsType<double>(result);
        var d = (double)result;
        // Rounded to 4 decimal places
        Assert.Equal(Math.Round(21.12345678, 4), d, 4);
    }

    [Fact]
    public void FormatValue_AnalogZero_ReturnsDoubleZero()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_INPUT,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 0.0);
        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – MultiState objects (1-based StateText index)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_MultiState_UsesOneBasedIndex()
    {
        var stateText = new List<string> { "Auto", "Manual", "Off" };
        // MultiState value 1 → index 0 → "Auto"
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)1, stateText);
        Assert.IsType<string>(result);
        Assert.Equal("Auto", result);
    }

    [Fact]
    public void FormatValue_MultiState_Value3_ReturnsThirdEntry()
    {
        var stateText = new List<string> { "Auto", "Manual", "Off" };
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)3, stateText);
        Assert.IsType<string>(result);
        Assert.Equal("Off", result);
    }

    [Fact]
    public void FormatValue_MultiState_OutOfRange_FallsBackToNumeric()
    {
        var stateText = new List<string> { "Auto", "Manual" };
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)5, stateText);
        // Out of range → falls back to int (multistate is enumerated)
        Assert.IsType<int>(result);
        Assert.Equal(5, result);
    }

    [Fact]
    public void FormatValue_MultiState_Null_ReturnsIntZero()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, null);
        Assert.IsType<int>(result);
        Assert.Equal(0, result);
    }

    [Fact]
    public void FormatValue_MultiState_RawDouble_TruncatesToInt()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 3.0);
        Assert.IsType<int>(result);
        Assert.Equal(3, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – Error suppression
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_ErrorString_BinaryReturnsIntDefault()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE,
            "ERROR_CLASS_PROPERTY: ERROR_CODE_UNKNOWN_PROPERTY");
        Assert.IsType<int>(result);
        Assert.Equal(0, result);
    }

    [Fact]
    public void FormatValue_ErrorString_AnalogReturnsDoubleDefault()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE,
            "ERROR_CLASS_OBJECT: ERROR_CODE_UNKNOWN_OBJECT");
        Assert.IsType<double>(result);
        Assert.Equal(0.0, result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – String passthrough
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_NonNumericString_ReturnsAsIs()
    {
        var result = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, "SomeText");
        Assert.IsType<string>(result);
        Assert.Equal("SomeText", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatValue – BacnetObjectInfo overload
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_ObjectInfoOverload_DelegatesToMainMethod()
    {
        var obj = new BacnetObjectInfo(
            "dev1", 10,
            new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, 1),
            "TestObj", new List<string>(),
            StateText: new List<string> { "Off", "On" });

        var result = BacnetValueConverter.FormatValue(obj, BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)1);
        Assert.IsType<string>(result);
        Assert.Equal("On", result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ToWriteValue
    // ═══════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_OUTPUT)]
    [InlineData(BacnetObjectTypes.OBJECT_BINARY_INPUT)]
    public void ToWriteValue_Binary_UsesEnumeratedTag(BacnetObjectTypes type)
    {
        var bv = BacnetValueConverter.ToWriteValue(type, 1.0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, bv.Tag);
        Assert.Equal((uint)1, bv.Value);
    }

    [Fact]
    public void ToWriteValue_BinaryZero_ProducesEnumeratedZero()
    {
        var bv = BacnetValueConverter.ToWriteValue(BacnetObjectTypes.OBJECT_BINARY_VALUE, 0.0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, bv.Tag);
        Assert.Equal((uint)0, bv.Value);
    }

    [Fact]
    public void ToWriteValue_BinaryNonZero_ClampedToOne()
    {
        var bv = BacnetValueConverter.ToWriteValue(BacnetObjectTypes.OBJECT_BINARY_VALUE, 42.0);
        Assert.Equal((uint)1, bv.Value);
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT)]
    public void ToWriteValue_MultiState_UsesEnumeratedTag(BacnetObjectTypes type)
    {
        var bv = BacnetValueConverter.ToWriteValue(type, 3.0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, bv.Tag);
        Assert.Equal((uint)3, bv.Value);
    }

    [Theory]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_VALUE)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT)]
    [InlineData(BacnetObjectTypes.OBJECT_ANALOG_INPUT)]
    public void ToWriteValue_Analog_UsesRealTag(BacnetObjectTypes type)
    {
        var bv = BacnetValueConverter.ToWriteValue(type, 21.5);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, bv.Tag);
        Assert.Equal(21.5f, bv.Value);
    }

    [Fact]
    public void ToWriteValue_AnalogZero_ProducesRealZero()
    {
        var bv = BacnetValueConverter.ToWriteValue(BacnetObjectTypes.OBJECT_ANALOG_OUTPUT, 0.0);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, bv.Tag);
        Assert.Equal(0.0f, bv.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FormatSchedule
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FormatSchedule_Null_ReturnsEmptyWeeklySchedule()
    {
        var result = BacnetValueConverter.FormatSchedule(null);
        Assert.IsType<WeeklySchedule>(result);
        var ws = (WeeklySchedule)result;
        Assert.Empty(ws.Days);
    }

    [Fact]
    public void FormatSchedule_NonEnumerable_ReturnsToString()
    {
        var result = BacnetValueConverter.FormatSchedule(42);
        Assert.Equal("42", result);
    }

    [Fact]
    public void FormatSchedule_EmptyList_ReturnsEmptySchedule()
    {
        var result = BacnetValueConverter.FormatSchedule(new List<object>());
        Assert.IsType<WeeklySchedule>(result);
        var ws = (WeeklySchedule)result;
        Assert.Empty(ws.Days);
    }

    [Fact]
    public void FormatSchedule_WithEntries_ProducesStructuredDays()
    {
        // Simulate a 1-day schedule with 2 time-value pairs
        var timeEntries = new List<BacnetValue>
        {
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_TIME, new DateTime(2026, 1, 1, 8, 0, 0)),
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, (uint)1),
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_TIME, new DateTime(2026, 1, 1, 17, 0, 0)),
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, (uint)0),
        };

        var weeklyRaw = new List<BacnetValue>
        {
            new BacnetValue(BacnetApplicationTags.BACNET_APPLICATION_TAG_NULL, timeEntries)
        };

        var result = BacnetValueConverter.FormatSchedule(weeklyRaw);
        Assert.IsType<WeeklySchedule>(result);
        var ws = (WeeklySchedule)result;
        Assert.Single(ws.Days);
        Assert.Equal("Monday", ws.Days[0].Day);
        Assert.Equal(2, ws.Days[0].Entries.Count);
        Assert.Equal("08:00", ws.Days[0].Entries[0].Time);
        Assert.Equal(1.0, ws.Days[0].Entries[0].Value);
        Assert.Equal("17:00", ws.Days[0].Entries[1].Time);
        Assert.Equal(0.0, ws.Days[0].Entries[1].Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Bidirectional consistency (read → write round-trip)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_BinaryValue_ZeroPreserved()
    {
        // Read: raw (uint)0 → FormatValue → int 0
        var readResult = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)0);
        Assert.Equal(0, readResult);
        Assert.IsType<int>(readResult);

        // Write: int 0 → ToWriteValue → ENUMERATED uint 0
        var writeResult = BacnetValueConverter.ToWriteValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, (int)readResult);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, writeResult.Tag);
        Assert.Equal((uint)0, writeResult.Value);
    }

    [Fact]
    public void RoundTrip_BinaryValue_OnePreserved()
    {
        var readResult = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, (uint)1);
        Assert.IsType<int>(readResult);
        Assert.Equal(1, readResult);

        var writeResult = BacnetValueConverter.ToWriteValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, (int)readResult);
        Assert.Equal((uint)1, writeResult.Value);
    }

    [Fact]
    public void RoundTrip_AnalogValue_Preserved()
    {
        var readResult = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 21.5f);
        Assert.IsType<double>(readResult);

        var writeResult = BacnetValueConverter.ToWriteValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE, (double)readResult);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_REAL, writeResult.Tag);
        Assert.Equal(21.5f, writeResult.Value);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FromDisplayValue – reverse of FormatValue's state-text lookup
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void FromDisplayValue_BinaryStateText_ResolvesIndex()
    {
        var stateText = new List<string> { "Normal", "Alarm" };
        // "Normal" → index 0 (binary is 0-based)
        Assert.Equal(0.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, "Normal", stateText));
        // "Alarm" → index 1
        Assert.Equal(1.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, "Alarm", stateText));
    }

    [Fact]
    public void FromDisplayValue_BinaryStateText_CaseInsensitive()
    {
        var stateText = new List<string> { "Off", "On" };
        Assert.Equal(1.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_INPUT, "on", stateText));
        Assert.Equal(0.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_INPUT, "OFF", stateText));
    }

    [Fact]
    public void FromDisplayValue_MultistateStateText_ResolvesOneBased()
    {
        var stateText = new List<string> { "Auto", "Manual", "Off" };
        // Multistate is 1-based: "Auto" → 1, "Manual" → 2, "Off" → 3
        Assert.Equal(1.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "Auto", stateText));
        Assert.Equal(2.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "Manual", stateText));
        Assert.Equal(3.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "Off", stateText));
    }

    [Fact]
    public void FromDisplayValue_NumericPassthrough()
    {
        // Already-numeric values pass through without state text lookup
        Assert.Equal(42.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_ANALOG_VALUE, 42.0));
        Assert.Equal(1.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, 1));
    }

    [Fact]
    public void FromDisplayValue_NumericString_Parsed()
    {
        Assert.Equal(3.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "3"));
    }

    [Fact]
    public void FromDisplayValue_UnknownLabel_ReturnsZero()
    {
        var stateText = new List<string> { "Normal", "Alarm" };
        Assert.Equal(0.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, "Unknown", stateText));
    }

    [Fact]
    public void FromDisplayValue_Null_ReturnsZero()
    {
        Assert.Equal(0.0, BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, null));
    }

    [Fact]
    public void FromDisplayValue_ViaObjectInfo_Overload()
    {
        var info = new BacnetObjectInfo(
            "dev1", 10,
            new BacnetObjectId(BacnetObjectTypes.OBJECT_BINARY_VALUE, 1),
            "Test.Point", new List<string>(),
            StateText: new List<string> { "Closed", "Open" });

        Assert.Equal(0.0, BacnetValueConverter.FromDisplayValue(info, "Closed"));
        Assert.Equal(1.0, BacnetValueConverter.FromDisplayValue(info, "Open"));
    }

    [Fact]
    public void RoundTrip_BinaryWithStateText_DisplayToWriteToDisplay()
    {
        var stateText = new List<string> { "Normal", "Alarm" };

        // Display "Normal" → internal 0 → BacnetValue → FormatValue → "Normal"
        double internalVal = BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, "Normal", stateText);
        Assert.Equal(0.0, internalVal);

        var bv = BacnetValueConverter.ToWriteValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE, internalVal);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, bv.Tag);
        Assert.Equal(0u, bv.Value);

        // Read back through FormatValue with the same state text
        var displayResult = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_BINARY_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 0u, stateText);
        Assert.Equal("Normal", displayResult);
    }

    [Fact]
    public void RoundTrip_MultistateWithStateText_DisplayToWriteToDisplay()
    {
        var stateText = new List<string> { "Auto", "Manual", "Off" };

        // "Manual" → internal 2 → BacnetValue(ENUMERATED, 2) → FormatValue → "Manual"
        double internalVal = BacnetValueConverter.FromDisplayValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, "Manual", stateText);
        Assert.Equal(2.0, internalVal);

        var bv = BacnetValueConverter.ToWriteValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE, internalVal);
        Assert.Equal(BacnetApplicationTags.BACNET_APPLICATION_TAG_ENUMERATED, bv.Tag);
        Assert.Equal(2u, bv.Value);

        var displayResult = BacnetValueConverter.FormatValue(
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
            BacnetPropertyIds.PROP_PRESENT_VALUE, 2u, stateText);
        Assert.Equal("Manual", displayResult);
    }
}
