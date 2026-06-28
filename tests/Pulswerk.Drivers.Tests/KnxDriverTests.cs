using System;
using Xunit;
using Pulswerk.Drivers.Knx;

namespace Pulswerk.Drivers.Tests
{
    public class KnxDriverTests
    {
        [Fact]
        public void TestGroupAddressParsingAndFormatting()
        {
            // Test 3-level format
            ushort addr1 = KnxConnection.ParseGroupAddress("1/2/3");
            Assert.Equal("1/2/3", KnxConnection.FormatGroupAddress(addr1));
            Assert.Equal((ushort)((1 << 11) | (2 << 8) | 3), addr1);

            ushort addr2 = KnxConnection.ParseGroupAddress("31/7/255");
            Assert.Equal("31/7/255", KnxConnection.FormatGroupAddress(addr2));
            Assert.Equal((ushort)((31 << 11) | (7 << 8) | 255), addr2);

            // Test 2-level format
            ushort addr3 = KnxConnection.ParseGroupAddress("1/2000");
            Assert.Equal((ushort)((1 << 11) | 2000), addr3);

            // Test 1-level / Free format
            ushort addr4 = KnxConnection.ParseGroupAddress("65535");
            Assert.Equal(65535, addr4);

            // Invalid address formats
            Assert.Throws<FormatException>(() => KnxConnection.ParseGroupAddress("32/0/0")); // Main out of range
            Assert.Throws<FormatException>(() => KnxConnection.ParseGroupAddress("1/8/0"));  // Middle out of range
            Assert.Throws<FormatException>(() => KnxConnection.ParseGroupAddress("1/0/256")); // Sub out of range
            Assert.Throws<FormatException>(() => KnxConnection.ParseGroupAddress("1/2/3/4")); // Too many parts
        }

        [Fact]
        public void TestDpt1Boolean()
        {
            // True
            byte[] encodedTrue = KnxDpt.Encode(true, "1.001", out bool isSmallTrue);
            Assert.True(isSmallTrue);
            Assert.Single(encodedTrue);
            Assert.Equal(1, encodedTrue[0]);
            Assert.Equal(true, KnxDpt.Decode(encodedTrue, "1.001"));

            // False
            byte[] encodedFalse = KnxDpt.Encode(false, "1.001", out bool isSmallFalse);
            Assert.True(isSmallFalse);
            Assert.Single(encodedFalse);
            Assert.Equal(0, encodedFalse[0]);
            Assert.Equal(false, KnxDpt.Decode(encodedFalse, "1.001"));
        }

        [Theory]
        [InlineData("1.001", "Off", "On")]
        [InlineData("1.008", "Up", "Down")]
        [InlineData("1.009", "Open", "Close")]
        [InlineData("1.011", "Inactive", "Active")]
        [InlineData("1.018", "Not occupied", "Occupied")]
        [InlineData("1.019", "Closed", "Open")]
        [InlineData("1.100", "Cooling", "Heating")]
        // Different DPT-string spellings should normalize to the same sub-type.
        [InlineData("DPST-1-9", "Open", "Close")]
        public void TestDpt1NamedStates(string dpt, string zeroLabel, string oneLabel)
        {
            var states = KnxDpt.GetDpt1States(dpt);
            Assert.NotNull(states);
            Assert.Equal(zeroLabel, states!.Value.Zero);
            Assert.Equal(oneLabel, states.Value.One);

            Assert.Equal(zeroLabel, KnxDpt.GetDpt1StateLabel(false, dpt));
            Assert.Equal(oneLabel, KnxDpt.GetDpt1StateLabel(true, dpt));
        }

        [Fact]
        public void TestDpt1UnknownSubTypeFallsBackToBool()
        {
            // An unknown 1.xxx sub-type still resolves to a generic boolean meaning.
            var states = KnxDpt.GetDpt1States("1.999");
            Assert.NotNull(states);
            Assert.Equal("False", states!.Value.Zero);
            Assert.Equal("True", states.Value.One);

            // A non-boolean DPT has no named states.
            Assert.Null(KnxDpt.GetDpt1States("9.001"));
            Assert.Null(KnxDpt.GetDpt1StateLabel(true, "9.001"));
        }

        [Theory]
        [InlineData("1.009", "Open", false)]   // DPT_OpenClose: 0 = Open, 1 = Close
        [InlineData("1.009", "Close", true)]
        [InlineData("1.008", "Down", true)]
        [InlineData("1.008", "Up", false)]
        [InlineData("1.001", "On", true)]
        [InlineData("1.001", "off", false)]   // case-insensitive
        [InlineData("1.001", "1", true)]
        [InlineData("1.001", "0", false)]
        [InlineData("1.001", "true", true)]
        [InlineData("1.001", "false", false)]
        public void TestDpt1EncodeFromStateLabel(string dpt, string input, bool expectedBit)
        {
            byte[] encoded = KnxDpt.Encode(input, dpt, out bool isSmall);
            Assert.True(isSmall);
            Assert.Single(encoded);
            Assert.Equal(expectedBit ? 1 : 0, encoded[0]);
        }

        [Fact]
        public void TestDpt1EncodeRejectsUnknownState()
        {
            Assert.Throws<FormatException>(() => KnxDpt.Encode("Frobnicate", "1.001", out _));
        }

        // ── Unsolicited GroupValueWrite -> push telemetry decoding ────────────

        private static Pulswerk.Core.DeviceConfig MakeKnxDevice(params Pulswerk.Core.KnxPointConfig[] points) =>
            new(
                Id: "knx-dev",
                Name: "KNX Dev",
                DeviceType: "knx",
                ConnectionId: "knx-1",
                KnxPoints: new System.Collections.Generic.List<Pulswerk.Core.KnxPointConfig>(points)
            );

        [Fact]
        public void TestDecodePushUpdate_BooleanProducesLowercaseStateUnderBothKeys()
        {
            var driver = new KnxDriver();
            var device = MakeKnxDevice(
                new Pulswerk.Core.KnxPointConfig("rain", "1/2/3", "1.001"));

            ushort ga = KnxConnection.ParseGroupAddress("1/2/3");

            var onValues = driver.DecodePushUpdate(device, ga, new byte[] { 1 });
            // Emitted under both the unscoped and device-scoped key, as "on" (not "True").
            Assert.Equal("on", onValues["rain"]);
            Assert.Equal("on", onValues["knx-dev_rain"]);

            var offValues = driver.DecodePushUpdate(device, ga, new byte[] { 0 });
            Assert.Equal("off", offValues["rain"]);
            Assert.Equal("off", offValues["knx-dev_rain"]);
        }

        [Fact]
        public void TestDecodePushUpdate_AnalogProducesNumericValue()
        {
            var driver = new KnxDriver();
            var device = MakeKnxDevice(
                new Pulswerk.Core.KnxPointConfig("temp", "2/0/1", "9.001"));

            ushort ga = KnxConnection.ParseGroupAddress("2/0/1");
            byte[] payload = KnxDpt.Encode(21.5, "9.001", out _);

            var values = driver.DecodePushUpdate(device, ga, payload);
            Assert.Equal(21.5, values["knx-dev_temp"]);
        }

        [Fact]
        public void TestDecodePushUpdate_UnknownAddressYieldsNothing()
        {
            var driver = new KnxDriver();
            var device = MakeKnxDevice(
                new Pulswerk.Core.KnxPointConfig("temp", "2/0/1", "9.001"));

            ushort otherGa = KnxConnection.ParseGroupAddress("7/7/7");
            var values = driver.DecodePushUpdate(device, otherGa, new byte[] { 1 });
            Assert.Empty(values);
        }

        [Fact]
        public void TestDpt5Scaling()
        {
            byte[] encoded = KnxDpt.Encode(128, "5.001", out bool isSmall);
            Assert.False(isSmall);
            Assert.Single(encoded);
            Assert.Equal(128, encoded[0]);
            Assert.Equal(128.0, KnxDpt.Decode(encoded, "5.001"));
        }

        [Fact]
        public void TestDpt9Temperature()
        {
            // Positive value: 21.5
            byte[] encoded1 = KnxDpt.Encode(21.5, "9.001", out bool isSmall1);
            Assert.False(isSmall1);
            Assert.Equal(2, encoded1.Length);
            double decoded1 = (double)KnxDpt.Decode(encoded1, "9.001");
            Assert.Equal(21.5, decoded1);

            // Negative value: -5.25
            byte[] encoded2 = KnxDpt.Encode(-5.25, "9.001", out bool isSmall2);
            Assert.False(isSmall2);
            Assert.Equal(2, encoded2.Length);
            double decoded2 = (double)KnxDpt.Decode(encoded2, "9.001");
            Assert.Equal(-5.25, decoded2);

            // Zero: 0.0
            byte[] encoded3 = KnxDpt.Encode(0.0, "9.001", out bool isSmall3);
            Assert.False(isSmall3);
            Assert.Equal(2, encoded3.Length);
            double decoded3 = (double)KnxDpt.Decode(encoded3, "9.001");
            Assert.Equal(0.0, decoded3);
        }

        [Fact]
        public void TestDpt12Unsigned4Byte()
        {
            uint original = 123456789U;
            byte[] encoded = KnxDpt.Encode(original, "12.001", out bool isSmall);
            Assert.False(isSmall);
            Assert.Equal(4, encoded.Length);
            object decoded = KnxDpt.Decode(encoded, "12.001");
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void TestDpt13Signed4Byte()
        {
            int original = -987654321;
            byte[] encoded = KnxDpt.Encode(original, "13.001", out bool isSmall);
            Assert.False(isSmall);
            Assert.Equal(4, encoded.Length);
            object decoded = KnxDpt.Decode(encoded, "13.001");
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void TestDpt14Float4Byte()
        {
            double original = 4567.89;
            byte[] encoded = KnxDpt.Encode(original, "14.056", out bool isSmall);
            Assert.False(isSmall);
            Assert.Equal(4, encoded.Length);
            object decoded = KnxDpt.Decode(encoded, "14.056");
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void TestKnxSecureValidation()
        {
            var config = new Pulswerk.Core.ConnectionConfig(
                Id: "knx-secure-test",
                Type: "knx-ip",
                Address: "127.0.0.1",
                KnxSecureEnabled: true,
                KnxCommissioningPassword: "" // Invalid empty commissioning password
            );

            var cfg = new Pulswerk.Core.AppConfig(
                InfluxDb: null,
                Database: null,
                Polling: null,
                Connections: new System.Collections.Generic.List<Pulswerk.Core.ConnectionConfig> { config },
                Devices: new System.Collections.Generic.List<Pulswerk.Core.DeviceConfig>(),
                Server: null
            );

            Assert.ThrowsAny<Exception>(() => Pulswerk.Core.ConfigValidator.Validate(cfg));
        }

        // ── KNX IP Secure crypto primitives ──────────────────────────────────
        // All vectors below are taken from the worked examples in the KNX spec
        // (Application Note AN159 v06 "KNXnet/IP Secure"), so a passing test proves
        // our implementation matches the official reference, not just itself.

        private static byte[] Hex(string s) =>
            Convert.FromHexString(s.Replace(" ", "").Replace("\n", ""));

        [Fact]
        public void TestKnxSecure_DeriveUserPassword()
        {
            // PBKDF2(user password "secret") from the spec example.
            byte[] expected = Hex("03 fc ed b6 66 60 25 1e c8 1a 1a 71 69 01 69 6a");
            Assert.Equal(expected, KnxSecureCrypto.DeriveUserPasswordHash("secret"));
        }

        [Fact]
        public void TestKnxSecure_DeriveDeviceAuthenticationCode()
        {
            // PBKDF2(device authentication / commissioning password "trustme").
            byte[] expected = Hex("e1 58 e4 01 20 47 bd 6c c4 1a af bc 5c 04 c1 fc");
            Assert.Equal(expected, KnxSecureCrypto.DeriveDeviceAuthenticationCode("trustme"));
        }

        [Fact]
        public void TestKnxSecure_CbcMac_RoutingIndicationExample()
        {
            // CBC-MAC of a RoutingIndication from the spec example.
            byte[] key = Hex("00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f");
            byte[] additionalData = Hex("06 10 09 50 00 37 00 00");
            byte[] payload = Hex("06 10 05 30 00 11 29 00 bc d0 11 59 0a de 01 00 81");
            byte[] block0 = Hex("c0 c1 c2 c3 c4 c5 00 fa 12 34 56 78 af fe 00 11");

            byte[] mac = KnxSecureCrypto.CalculateMacCbc(key, additionalData, payload, block0);
            Assert.Equal(Hex("bd 0a 29 4b 95 25 54 b2 35 39 20 4c 22 71 d2 6b"), mac);
        }

        [Fact]
        public void TestKnxSecure_Ctr_EncryptThenDecrypt()
        {
            // CTR encryption from the spec example, then verify it round-trips.
            byte[] key = Hex("00 01 02 03 04 05 06 07 08 09 0a 0b 0c 0d 0e 0f");
            byte[] counter0 = Hex("c0 c1 c2 c3 c4 c5 00 fa 12 34 56 78 af fe ff 00");
            byte[] macCbc = Hex("bd 0a 29 4b 95 25 54 b2 35 39 20 4c 22 71 d2 6b");
            byte[] payload = Hex("06 10 05 30 00 11 29 00 bc d0 11 59 0a de 01 00 81");

            var (encPayload, encMac) = KnxSecureCrypto.Ctr(key, counter0, payload, macCbc);
            Assert.Equal(Hex("b7 ee 7e 8a 1c 2f 7b ba be c7 75 fd 6e 10 d0 bc 4b"), encPayload);
            Assert.Equal(Hex("72 12 a0 3a aa e4 9d a8 56 89 77 4c 1d 2b 4d a4"), encMac);

            // CTR is symmetric: feeding the ciphertext back recovers the plaintext + MAC.
            var (decPayload, decMac) = KnxSecureCrypto.Ctr(key, counter0, encPayload, encMac);
            Assert.Equal(payload, decPayload);
            Assert.Equal(macCbc, decMac);
        }

        [Fact]
        public void TestKnxSecure_CommissioningPasswordNormalization()
        {
            // The device authentication code (FDSK) is printed as hyphen groups over several
            // lines. Pasting it with line breaks/whitespace must produce the same hash as the
            // clean single-line form (hyphens are kept, only line breaks/whitespace stripped).
            byte[] clean = KnxSecureCrypto.DeriveDeviceAuthenticationCode("ABCDE-ABCDE-ABCDE-ABCDE");

            byte[] withNewlines = KnxSecureCrypto.DeriveDeviceAuthenticationCode(
                KnxTcpSession.NormalizeForTesting("ABCDE-ABCDE\nABCDE-ABCDE"));
            byte[] withWhitespace = KnxSecureCrypto.DeriveDeviceAuthenticationCode(
                KnxTcpSession.NormalizeForTesting("  ABCDE-ABCDE\r\nABCDE-ABCDE\t"));

            Assert.Equal(clean, withNewlines);
            Assert.Equal(clean, withWhitespace);

            // Hyphens are significant: removing them yields a different (wrong) hash.
            byte[] noHyphens = KnxSecureCrypto.DeriveDeviceAuthenticationCode("ABCDEABCDEABCDEABCDE");
            Assert.NotEqual(clean, noHyphens);
        }

        [Fact]
        public void TestKnxSecure_SessionKeyDerivation()
        {
            // ECDH(X25519) shared secret -> SHA256[:16] session key must match on both peers.
            byte[] clientPriv = new byte[32];
            byte[] clientPub = new byte[32];
            var rnd = new Org.BouncyCastle.Security.SecureRandom();
            rnd.NextBytes(clientPriv);
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.GeneratePublicKey(clientPriv, 0, clientPub, 0);

            byte[] serverPriv = new byte[32];
            byte[] serverPub = new byte[32];
            rnd.NextBytes(serverPriv);
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.GeneratePublicKey(serverPriv, 0, serverPub, 0);

            byte[] clientShared = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(clientPriv, 0, serverPub, 0, clientShared, 0);
            byte[] serverShared = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(serverPriv, 0, clientPub, 0, serverShared, 0);

            Assert.Equal(clientShared, serverShared);
            Assert.Equal(
                KnxSecureCrypto.SessionKeyFromSharedSecret(clientShared),
                KnxSecureCrypto.SessionKeyFromSharedSecret(serverShared));
        }
    }
}
