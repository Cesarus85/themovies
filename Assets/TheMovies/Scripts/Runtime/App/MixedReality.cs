using TheMovies.World;
using UnityEngine;
#if THEMOVIES_ARF
using UnityEngine.XR.ARFoundation;
#endif

namespace TheMovies.App
{
    /// <summary>
    /// Mixed reality mode: the cinema hall disappears, the passthrough camera shows your own room and the
    /// screen floats in it at TV-like distance; the other participants stay as avatars next to you.
    /// Passthrough on Quest needs the "Unity OpenXR: Meta" package (com.unity.xr.meta-openxr, brings AR
    /// Foundation) with the OpenXR features "Meta Quest: Session" and "Meta Quest: Camera (Passthrough)".
    /// </summary>
    public static class MixedReality
    {
        public static bool Enabled { get; private set; }

        /// <summary>False if the passthrough packages are not installed (the menu explains how to add them).</summary>
        public static bool Available
        {
            get
            {
#if THEMOVIES_ARF
                return true;
#else
                return false;
#endif
            }
        }

#if THEMOVIES_ARF
        static ARSession s_Session;
        static ARCameraManager s_CameraManager;
#endif

        public static void SetEnabled(bool enabled, CinemaEnvironment environment, Camera camera)
        {
            if (enabled && !Available) enabled = false;
            Enabled = enabled;

            environment.SetHallVisible(!enabled);
            environment.Screen.SetMixedRealityLayout(enabled);

            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                // Passthrough shows wherever the rendered image is transparent.
                camera.backgroundColor = enabled ? new Color(0, 0, 0, 0) : Color.black;
            }

#if THEMOVIES_ARF
            if (enabled && s_Session == null)
            {
                s_Session = new GameObject("AR Session").AddComponent<ARSession>();
                Object.DontDestroyOnLoad(s_Session.gameObject);
            }
            if (camera != null && s_CameraManager == null && enabled)
                s_CameraManager = camera.gameObject.AddComponent<ARCameraManager>();
            if (s_Session != null) s_Session.enabled = enabled;
            if (s_CameraManager != null) s_CameraManager.enabled = enabled;
#endif
        }
    }
}
