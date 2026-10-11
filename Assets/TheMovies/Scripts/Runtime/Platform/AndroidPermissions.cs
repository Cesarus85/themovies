using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace TheMovies.Platform
{
    /// <summary>Asks for storage (movie files) and microphone (voice chat) access on the Quest.</summary>
    public static class AndroidPermissions
    {
        const string k_ReadMediaVideo = "android.permission.READ_MEDIA_VIDEO";

        /// <summary>Requests storage access; <paramref name="onGranted"/> runs once access was granted.</summary>
        public static void RequestStorage(Action onGranted)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (HasStorage())
            {
                onGranted?.Invoke();
                return;
            }
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => onGranted?.Invoke();
            Permission.RequestUserPermissions(new[] { Permission.ExternalStorageRead, k_ReadMediaVideo }, callbacks);
#else
            onGranted?.Invoke();
#endif
        }

        public static bool HasStorage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.ExternalStorageRead) ||
                   Permission.HasUserAuthorizedPermission(k_ReadMediaVideo);
#else
            return true;
#endif
        }

        public static void RequestMicrophone()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                Permission.RequestUserPermission(Permission.Microphone);
#endif
        }
    }
}
