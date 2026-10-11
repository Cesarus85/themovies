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

        /// <summary>Explains why passthrough does not show (empty when everything looks fine).</summary>
        public static string Problem
        {
            get
            {
                Diagnose(out string problem, out _);
                return problem;
            }
        }

        /// <summary>One line for the settings page: what the passthrough is doing right now.</summary>
        public static string Status
        {
            get
            {
                Diagnose(out string problem, out string state);
                return problem.Length > 0 ? problem + "  [" + state + "]" : "Passthrough läuft  [" + state + "]";
            }
        }

        static string s_LastLogged;

        static void Diagnose(out string problem, out string state)
        {
            problem = string.Empty;
            state = string.Empty;
            if (!Enabled) return;
#if THEMOVIES_ARF
            bool extension = PassthroughExtensionEnabled(out string runtime);
            bool hasSubsystem = s_CameraManager != null && s_CameraManager.subsystem != null;
            bool running = hasSubsystem && s_CameraManager.subsystem.running;
            string manifest = ManifestPassthrough();
            state = $"Session {ARSession.state}, Kamera {(hasSubsystem ? (running ? "läuft" : "gestoppt") : "fehlt")}, " +
                    $"XR_FB_passthrough {(extension ? "aktiv" : "AUS")}, Manifest {manifest}, Neustarts {s_Restarts}, {runtime}";
            if (!extension)
                problem = "Passthrough war beim Bauen nicht aktiv: OpenXR/Android 'Meta Quest: Camera (Passthrough)' + 'Meta Quest: Session' anhaken und neu bauen.";
            else if (ARSession.state == ARSessionState.Unsupported)
                problem = "Passthrough: Session 'Unsupported' – OpenXR-Feature 'Meta Quest: Session' aktiv?";
            else if (!hasSubsystem)
                problem = "Passthrough: kein Kamera-Subsystem – 'Meta Quest: Camera (Passthrough)' in OpenXR/Android aktiv?";
            else if (!running)
                problem = "Passthrough: Kamera startet noch …";
            else if (manifest == "NEIN")
                problem = "Passthrough fehlt im Manifest (com.oculus.feature.PASSTHROUGH) – mit dem neuen Stand neu bauen.";
#else
            problem = "Mixed Reality: Paket 'Unity OpenXR: Meta' fehlt.";
            state = "kein AR Foundation";
#endif
            string line = problem + " | " + state;
            if (line != s_LastLogged)
            {
                s_LastLogged = line;
                Debug.Log("[TheMovies] Passthrough: " + line);
            }
        }

        static bool PassthroughExtensionEnabled(out string runtime)
        {
#if THEMOVIES_OPENXR
            try
            {
                runtime = UnityEngine.XR.OpenXR.OpenXRRuntime.name + " " + UnityEngine.XR.OpenXR.OpenXRRuntime.version;
                return UnityEngine.XR.OpenXR.OpenXRRuntime.IsExtensionEnabled("XR_FB_passthrough");
            }
            catch (System.Exception e)
            {
                runtime = "OpenXR: " + e.GetType().Name;
                return true; // can't tell; don't blame the build
            }
#else
            runtime = "OpenXR unbekannt";
            return true;
#endif
        }

#if THEMOVIES_ARF
        static ARSession s_Session;
        static ARCameraManager s_CameraManager;

        /// <summary>
        /// Starts the passthrough camera again once the headset is tracking, and after the app got the focus back
        /// (Quest menu, standby). Enabled at startup the camera can start before the OpenXR session is ready
        /// and then stays black.
        /// </summary>
        sealed class PassthroughKeeper : MonoBehaviour
        {
            float m_RestartAt = -1f;
            bool m_RestartedAfterTracking;

            void OnEnable() => m_RestartedAfterTracking = false;

            void OnApplicationFocus(bool focused)
            {
                if (focused) m_RestartAt = Time.unscaledTime + 0.5f;
            }

            void Update()
            {
                if (!Enabled || s_CameraManager == null) return;
                var rig = TheMovies.XR.XRRig.Instance;
                if (!m_RestartedAfterTracking && rig != null && rig.Ready)
                {
                    m_RestartedAfterTracking = true;
                    m_RestartAt = Time.unscaledTime;
                }
                if (m_RestartAt >= 0 && Time.unscaledTime >= m_RestartAt)
                {
                    m_RestartAt = -1f;
                    Restart();
                }
            }
        }

        /// <summary>Switches the passthrough camera off and on again (also offered in the settings).</summary>
        public static void Restart()
        {
            if (!Enabled || s_CameraManager == null) return;
            s_Restarts++;
            s_CameraManager.enabled = false;
            s_CameraManager.enabled = true;
            Debug.Log("[TheMovies] Passthrough camera restarted.");
        }

        static int s_Restarts;
#else
        public static void Restart() { }
#endif

        static string s_ManifestPassthrough;

        /// <summary>Whether the installed APK declares the Quest passthrough feature ("ja"/"NEIN"/"?").</summary>
        static string ManifestPassthrough()
        {
            if (s_ManifestPassthrough != null) return s_ManifestPassthrough;
            s_ManifestPassthrough = "?";
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var info = pm.Call<AndroidJavaObject>("getPackageInfo", activity.Call<string>("getPackageName"), 16384)) // GET_CONFIGURATIONS
                {
                    var features = info.Get<AndroidJavaObject[]>("reqFeatures");
                    bool found = false;
                    if (features != null)
                        foreach (var f in features)
                        {
                            if (f == null) continue;
                            if (f.Get<string>("name") == "com.oculus.feature.PASSTHROUGH") found = true;
                            f.Dispose();
                        }
                    s_ManifestPassthrough = found ? "ja" : "NEIN";
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not read the manifest features: " + e.Message);
            }
#endif
            return s_ManifestPassthrough;
        }

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
                camera.allowHDR = false;
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
            if (camera != null && enabled && camera.GetComponent<PassthroughKeeper>() == null)
                camera.gameObject.AddComponent<PassthroughKeeper>();
            if (enabled)
                Debug.Log("[TheMovies] Mixed reality on. Camera subsystem: " +
                          (s_CameraManager != null && s_CameraManager.subsystem != null ? s_CameraManager.subsystem.GetType().Name : "none"));
#endif
        }
    }
}
