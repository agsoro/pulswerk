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
                KnxPassword: "" // Invalid empty password
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

        [Fact]
        public void TestKnxSecureCryptoFlow()
        {
            byte[] clientPrivateKey = new byte[32];
            byte[] clientPublicKey = new byte[32];
            var secureRandom = new Org.BouncyCastle.Security.SecureRandom();
            secureRandom.NextBytes(clientPrivateKey);
            clientPrivateKey[0] &= 248;
            clientPrivateKey[31] &= 127;
            clientPrivateKey[31] |= 64;

            Org.BouncyCastle.Math.EC.Rfc7748.X25519.GeneratePublicKey(clientPrivateKey, 0, clientPublicKey, 0);

            byte[] serverPrivateKey = new byte[32];
            byte[] serverPublicKey = new byte[32];
            secureRandom.NextBytes(serverPrivateKey);
            serverPrivateKey[0] &= 248;
            serverPrivateKey[31] &= 127;
            serverPrivateKey[31] |= 64;

            Org.BouncyCastle.Math.EC.Rfc7748.X25519.GeneratePublicKey(serverPrivateKey, 0, serverPublicKey, 0);

            byte[] clientSharedSecret = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(clientPrivateKey, 0, serverPublicKey, 0, clientSharedSecret, 0);

            byte[] serverSharedSecret = new byte[32];
            Org.BouncyCastle.Math.EC.Rfc7748.X25519.ScalarMult(serverPrivateKey, 0, clientPublicKey, 0, serverSharedSecret, 0);

            Assert.Equal(clientSharedSecret, serverSharedSecret);

            byte[] keyMaterial;
            using (var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes("test-password")))
            {
                keyMaterial = hmac.ComputeHash(clientSharedSecret);
            }
            byte[] sessionKey = new byte[16];
            Array.Copy(keyMaterial, 0, sessionKey, 0, 16);

            byte[] nonce = new byte[12];
            secureRandom.NextBytes(nonce);

            byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, secure KNX IP!");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] mac = new byte[16];
            byte[] associatedData = new byte[14];
            secureRandom.NextBytes(associatedData);

            using (var aesCcm = new System.Security.Cryptography.AesCcm(sessionKey))
            {
                aesCcm.Encrypt(nonce, plaintext, ciphertext, mac, associatedData);
            }

            byte[] decrypted = new byte[plaintext.Length];
            using (var aesCcm = new System.Security.Cryptography.AesCcm(sessionKey))
            {
                aesCcm.Decrypt(nonce, ciphertext, mac, decrypted, associatedData);
            }

            Assert.Equal(plaintext, decrypted);
        }
    }
}
