using System;
using System.Security.Cryptography;
using System.Text;

namespace Pulswerk.Drivers.Knx
{
    /// <summary>
    /// Spec-compliant cryptographic primitives for KNXnet/IP Secure (KNX AN159).
    ///
    /// <para>The KNX IP Secure handshake and frame protection are <i>not</i> a single
    /// AEAD mode — the spec composes them from:</para>
    /// <list type="bullet">
    ///   <item>PBKDF2-HMAC-SHA256 to turn a human password into a 16-byte key.</item>
    ///   <item>X25519 (Curve25519 ECDH) for the session key agreement.</item>
    ///   <item>SHA-256 of the shared secret (first 16 bytes) as the session key.</item>
    ///   <item>AES-128 CBC-MAC for authentication and AES-128 CTR for encryption.</item>
    /// </list>
    ///
    /// <para>These helpers were cross-checked against the worked examples in the KNX
    /// specification (AN159v06) and the xknx reference implementation.</para>
    /// </summary>
    public static class KnxSecureCrypto
    {
        // Counter block used for the handshake MACs (SESSION_RESPONSE / SESSION_AUTHENTICATE).
        // 14 zero bytes followed by 0xFF, 0x00.
        public static readonly byte[] Counter0Handshake =
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0x00 };

        /// <summary>
        /// Derives the 16-byte device authentication code from the commissioning
        /// password printed on the device. Salt per KNX spec.
        /// </summary>
        public static byte[] DeriveDeviceAuthenticationCode(string commissioningPassword)
            => Pbkdf2(commissioningPassword, "device-authentication-code.1.secure.ip.knx.org");

        /// <summary>
        /// Derives the 16-byte user (tunnel) password hash. Salt per KNX spec.
        /// </summary>
        public static byte[] DeriveUserPasswordHash(string userPassword)
            => Pbkdf2(userPassword, "user-password.1.secure.ip.knx.org");

        private static byte[] Pbkdf2(string password, string salt)
        {
            // KNX uses latin-1 (ISO-8859-1) encoding for passwords, SHA-256, 65536 iterations, 16-byte output.
            byte[] pwdBytes = Encoding.Latin1.GetBytes(password ?? string.Empty);
            byte[] saltBytes = Encoding.ASCII.GetBytes(salt);
            return Rfc2898DeriveBytes.Pbkdf2(pwdBytes, saltBytes, 65536, HashAlgorithmName.SHA256, 16);
        }

        /// <summary>SHA-256 of <paramref name="data"/>, truncated to the first 16 bytes (the session key).</summary>
        public static byte[] SessionKeyFromSharedSecret(byte[] sharedSecret)
        {
            byte[] hash = SHA256.HashData(sharedSecret);
            byte[] key = new byte[16];
            Array.Copy(hash, 0, key, 0, 16);
            return key;
        }

        /// <summary>XOR of two equal-length byte arrays (used to combine the ECDH public keys).</summary>
        public static byte[] Xor(byte[] a, byte[] b)
        {
            int len = Math.Min(a.Length, b.Length);
            byte[] result = new byte[len];
            for (int i = 0; i < len; i++) result[i] = (byte)(a[i] ^ b[i]);
            return result;
        }

        /// <summary>
        /// Computes the KNX Secure CBC-MAC over (block0 || len(AD) || AD || payload),
        /// zero-padded to a 16-byte boundary, AES-128-CBC with a zero IV, taking the
        /// last cipher block (16 bytes).
        /// </summary>
        public static byte[] CalculateMacCbc(byte[] key, byte[] additionalData, byte[]? payload = null, byte[]? block0 = null)
        {
            block0 ??= new byte[16];
            payload ??= Array.Empty<byte>();

            int adLen = additionalData.Length;
            // block0 + 2-byte AD length (big-endian) + AD + payload, zero padded to /16.
            int unpadded = block0.Length + 2 + adLen + payload.Length;
            int padded = ((unpadded + 15) / 16) * 16;
            byte[] blocks = new byte[padded];

            int pos = 0;
            Array.Copy(block0, 0, blocks, pos, block0.Length); pos += block0.Length;
            blocks[pos++] = (byte)((adLen >> 8) & 0xFF);
            blocks[pos++] = (byte)(adLen & 0xFF);
            Array.Copy(additionalData, 0, blocks, pos, adLen); pos += adLen;
            Array.Copy(payload, 0, blocks, pos, payload.Length);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.IV = new byte[16];
            using var enc = aes.CreateEncryptor();
            byte[] cipher = enc.TransformFinalBlock(blocks, 0, blocks.Length);

            byte[] mac = new byte[16];
            Array.Copy(cipher, cipher.Length - 16, mac, 0, 16);
            return mac;
        }

        /// <summary>
        /// AES-128-CTR. The KNX spec encrypts the MAC with counter block S0 and the
        /// payload with S1, S2, ... Returns (cipherPayload, encryptedMac).
        /// Because CTR is symmetric, the same routine decrypts.
        /// </summary>
        public static (byte[] payload, byte[] mac) Ctr(byte[] key, byte[] counter0, byte[] payload, byte[] mac)
        {
            // First keystream block (S0) encrypts the MAC.
            byte[] ctr = (byte[])counter0.Clone();
            byte[] encMac = XorWithKeystream(key, ref ctr, mac, 16, advanceFirst: false);

            // Subsequent keystream blocks encrypt the payload.
            byte[] encPayload = XorWithKeystream(key, ref ctr, payload, payload.Length, advanceFirst: true);
            return (encPayload, encMac);
        }

        private static byte[] XorWithKeystream(byte[] key, ref byte[] counter, byte[] data, int length, bool advanceFirst)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            using var enc = aes.CreateEncryptor();

            byte[] result = new byte[length];
            byte[] keystream = new byte[16];
            for (int offset = 0; offset < length; offset += 16)
            {
                if (advanceFirst) IncrementCounter(counter);
                enc.TransformBlock(counter, 0, 16, keystream, 0);
                if (!advanceFirst) advanceFirst = true; // S0 used as-is, then increment for the rest.

                int block = Math.Min(16, length - offset);
                for (int i = 0; i < block; i++)
                    result[offset + i] = (byte)(data[offset + i] ^ keystream[i]);
            }
            return result;
        }

        private static void IncrementCounter(byte[] counter)
        {
            for (int i = counter.Length - 1; i >= 0; i--)
            {
                if (++counter[i] != 0) break;
            }
        }
    }
}
