using System;
using System.Security.Cryptography;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>
    /// Fallback protection for saved passwords where no system key store is available (editor, PC):
    /// AES-256-CBC with a random IV plus HMAC-SHA256, keys derived from a device-specific value.
    /// This only hides the passwords from casual reading of the settings – the Quest uses the Android Keystore.
    /// </summary>
    public static class LocalSecret
    {
        public static string Protect(string plain, string deviceKey)
        {
            DeriveKeys(deviceKey, out byte[] encKey, out byte[] macKey);
            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.GenerateIV();
                byte[] data = Encoding.UTF8.GetBytes(plain ?? string.Empty);
                byte[] cipher;
                using (var encryptor = aes.CreateEncryptor())
                    cipher = encryptor.TransformFinalBlock(data, 0, data.Length);
                var payload = new byte[16 + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, payload, 0, 16);
                Buffer.BlockCopy(cipher, 0, payload, 16, cipher.Length);
                byte[] mac;
                using (var hmac = new HMACSHA256(macKey)) mac = hmac.ComputeHash(payload);
                var result = new byte[payload.Length + 32];
                Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
                Buffer.BlockCopy(mac, 0, result, payload.Length, 32);
                return Convert.ToBase64String(result);
            }
        }

        /// <returns>The original text, or null if it was changed or made with another device key.</returns>
        public static string Unprotect(string protectedText, string deviceKey)
        {
            try
            {
                byte[] all = Convert.FromBase64String(protectedText ?? string.Empty);
                if (all.Length < 16 + 16 + 32) return null;
                DeriveKeys(deviceKey, out byte[] encKey, out byte[] macKey);
                int payloadLength = all.Length - 32;
                byte[] mac;
                using (var hmac = new HMACSHA256(macKey)) mac = hmac.ComputeHash(all, 0, payloadLength);
                int diff = 0;
                for (int i = 0; i < 32; i++) diff |= mac[i] ^ all[payloadLength + i];
                if (diff != 0) return null;
                using (var aes = Aes.Create())
                {
                    aes.Key = encKey;
                    var iv = new byte[16];
                    Buffer.BlockCopy(all, 0, iv, 0, 16);
                    aes.IV = iv;
                    using (var decryptor = aes.CreateDecryptor())
                        return Encoding.UTF8.GetString(decryptor.TransformFinalBlock(all, 16, payloadLength - 16));
                }
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        static void DeriveKeys(string deviceKey, out byte[] encKey, out byte[] macKey)
        {
            using (var sha = SHA256.Create())
            {
                encKey = sha.ComputeHash(Encoding.UTF8.GetBytes("TheMovies/enc/" + deviceKey));
                macKey = sha.ComputeHash(Encoding.UTF8.GetBytes("TheMovies/mac/" + deviceKey));
            }
        }
    }
}
