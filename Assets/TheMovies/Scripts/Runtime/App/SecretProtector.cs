using System;
using TheMovies.Core;
using UnityEngine;

namespace TheMovies.App
{
    /// <summary>
    /// Protects saved passwords and tokens. On the Quest with the Android Keystore (com.themovies.player.SecretStore,
    /// AES-GCM, the key never leaves the system), elsewhere with <see cref="LocalSecret"/> (only hidden, not secure).
    /// Stored texts carry a prefix telling which way they were protected.
    /// </summary>
    public static class SecretProtector
    {
        const string KeystorePrefix = "ks1:";
        const string LocalPrefix = "lo1:";

        static string DeviceKey => SystemInfo.deviceUniqueIdentifier + "/" + Application.identifier;

        /// <returns>The protected text, or null if protecting failed (the caller then keeps the old format).</returns>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var store = new AndroidJavaClass("com.themovies.player.SecretStore"))
                {
                    string encrypted = store.CallStatic<string>("encrypt", plain);
                    if (!string.IsNullOrEmpty(encrypted)) return KeystorePrefix + encrypted;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Android Keystore not available, passwords only hidden: " + e.Message);
            }
#endif
            try
            {
                return LocalPrefix + LocalSecret.Protect(plain, DeviceKey);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not protect a password: " + e.Message);
                return null;
            }
        }

        /// <returns>The original text, or null if it can't be read (e.g. after reinstalling the app).</returns>
        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            try
            {
                if (stored.StartsWith(KeystorePrefix, StringComparison.Ordinal))
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    using (var store = new AndroidJavaClass("com.themovies.player.SecretStore"))
                        return store.CallStatic<string>("decrypt", stored.Substring(KeystorePrefix.Length));
#else
                    return null;
#endif
                }
                if (stored.StartsWith(LocalPrefix, StringComparison.Ordinal))
                    return LocalSecret.Unprotect(stored.Substring(LocalPrefix.Length), DeviceKey);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not read a saved password: " + e.Message);
            }
            return null;
        }
    }
}
