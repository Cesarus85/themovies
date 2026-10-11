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
#if THEMOVIES_ARF || THEMOVIES_OPENXR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The own OpenXR passthrough feature (XR_FB_passthrough) is in the build and running.</summary>
        static bool OwnPassthrough
        {
            get
            {
#if THEMOVIES_OPENXR
                return TheMovies.XR.PassthroughFeature.Status != "nicht gestartet";
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
#if THEMOVIES_OPENXR
            if (OwnPassthrough)
            {
                bool ext = PassthroughExtensionEnabled(out string rt);
                state = $"Eigenes Passthrough: {TheMovies.XR.PassthroughFeature.Status}, Bilder {TheMovies.XR.PassthroughFeature.FramesWithPassthrough}, " +
                        $"XR_FB_passthrough {(ext ? "aktiv" : "AUS")}, Manifest {ManifestPassthrough()}, {EyeTextureInfo()}, {rt}";
                if (!TheMovies.XR.PassthroughFeature.Ready)
                    problem = "Passthrough: " + TheMovies.XR.PassthroughFeature.Status;
                LogDiagnosis(problem, state);
                return;
            }
#endif
#if THEMOVIES_ARF
            bool extension = PassthroughExtensionEnabled(out string runtime);
            bool hasSubsystem = s_CameraManager != null && s_CameraManager.subsystem != null;
            bool running = hasSubsystem && s_CameraManager.subsystem.running;
            string manifest = ManifestPassthrough();
            state = $"Session {ARSession.state}, Kamera {(hasSubsystem ? (running ? "läuft" : "gestoppt") : "fehlt")}, " +
                    $"XR_FB_passthrough {(extension ? "aktiv" : "AUS")}, Manifest {manifest}, {EyeTextureInfo()}, Test {TestModeName}, {runtime}";
            if (!extension)
                problem = "Passthrough war beim Bauen nicht aktiv: OpenXR/Android 'Meta Quest: Camera (Passthrough)' + 'Meta Quest: Session' anhaken und neu bauen.";
            else if (ARSession.state == ARSessionState.Unsupported)
                problem = "Passthrough: Session 'Unsupported' – OpenXR-Feature 'Meta Quest: Session' aktiv?";
            else if (s_CameraManager == null)
                problem = "Passthrough startet, sobald das Tracking läuft …";
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
            LogDiagnosis(problem, state);
        }

        static void LogDiagnosis(string problem, string state)
        {
            // Frame counters change constantly: log only when the rest changes.
            string line = System.Text.RegularExpressions.Regex.Replace(problem + " | " + state, @"Bilder \d+", "Bilder …");
            if (line != s_LastLogged)
            {
                s_LastLogged = line;
                Debug.Log("[TheMovies] Passthrough: " + problem + " | " + state);
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
        /// Adds the AR camera manager (which starts passthrough) only once head tracking runs, and never switches it
        /// off again: started too early it stays black, and the Meta OpenXR package has a known problem bringing
        /// passthrough back after it was stopped. "Cinema" mode simply covers it with an opaque background.
        /// </summary>
        sealed class PassthroughKeeper : MonoBehaviour
        {
            void Update()
            {
                if (!Enabled || s_CameraManager != null) return;
                var rig = TheMovies.XR.XRRig.Instance;
                if (rig != null && !rig.Ready) return;
                var manager = gameObject.GetComponent<ARCameraManager>();
                if (manager == null) manager = gameObject.AddComponent<ARCameraManager>();
                s_CameraManager = manager;
                s_CameraManager.enabled = true;
                Debug.Log("[TheMovies] Passthrough camera started. Subsystem: " +
                          (s_CameraManager.subsystem != null ? s_CameraManager.subsystem.GetType().Name : "none"));
            }
        }
#endif

        /// <summary>Shows the own passthrough as soon as its layer exists (the XR session may start after the app).</summary>
        sealed class OwnPassthroughUpdater : MonoBehaviour
        {
            void Update()
            {
#if THEMOVIES_OPENXR
                if (Enabled && TheMovies.XR.PassthroughFeature.Ready && !s_OwnShown)
                {
                    TheMovies.XR.PassthroughFeature.SetVisible(true);
                    s_OwnShown = true;
                }
                else if (!Enabled)
                {
                    s_OwnShown = false;
                }
#endif
            }
        }

        static bool s_OwnShown;

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

        // ---------------- Test variants (to narrow down a black passthrough on the device) ----------------

        static int s_TestMode;
        static Camera s_Camera;
        static GameObject s_ClearQuad;

        public static int TestMode => s_TestMode;
        public const int TestModes = 3;

        public static string TestModeName
        {
            get
            {
                switch (s_TestMode)
                {
                    case 1: return "ohne MSAA";
                    case 2: return "ohne MSAA + Klar-Ebene";
                    default: return "normal";
                }
            }
        }

        /// <summary>Cycles: normal → without MSAA → without MSAA plus a full-view alpha-0 layer.</summary>
        public static void CycleTestMode()
        {
            s_TestMode = (s_TestMode + 1) % TestModes;
            ApplyTestMode();
            Debug.Log("[TheMovies] Mixed reality test mode: " + TestModeName);
        }

        static void ApplyTestMode()
        {
            bool mr = Enabled;
            bool noMsaa = mr && s_TestMode >= 1;
            QualitySettings.antiAliasing = noMsaa ? 0 : 4;
            if (s_Camera != null) s_Camera.allowMSAA = !noMsaa;

            bool quad = mr && s_TestMode >= 2 && s_Camera != null;
            if (quad && s_ClearQuad == null)
            {
                s_ClearQuad = new GameObject("MR Clear Layer");
                s_ClearQuad.transform.SetParent(s_Camera.transform, false);
                var mesh = new Mesh { name = "Fullscreen Triangle" };
                mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                s_ClearQuad.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = s_ClearQuad.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = new Material(Shader.Find("TheMovies/ClearAlpha"));
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            if (s_ClearQuad != null) s_ClearQuad.SetActive(quad);
        }

        static string EyeTextureInfo()
        {
            try
            {
                var desc = UnityEngine.XR.XRSettings.eyeTextureDesc;
                return $"Augenbild {desc.graphicsFormat} ×{desc.msaaSamples}";
            }
            catch (System.Exception)
            {
                return "Augenbild ?";
            }
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
                s_Camera = camera;
            }
            ApplyTestMode();

            if (camera != null && camera.GetComponent<OwnPassthroughUpdater>() == null)
                camera.gameObject.AddComponent<OwnPassthroughUpdater>();
            if (OwnPassthrough)
            {
#if THEMOVIES_OPENXR
                TheMovies.XR.PassthroughFeature.SetVisible(enabled);
                s_OwnShown = enabled && TheMovies.XR.PassthroughFeature.Ready;
                if (enabled) Debug.Log("[TheMovies] Mixed reality on (own passthrough: " + TheMovies.XR.PassthroughFeature.Status + ").");
#endif
                return;
            }

#if THEMOVIES_ARF
            if (enabled && s_Session == null)
            {
                s_Session = new GameObject("AR Session").AddComponent<ARSession>();
                Object.DontDestroyOnLoad(s_Session.gameObject);
            }
            // The camera manager is added by the keeper once tracking runs and then stays on.
            if (camera != null && enabled && camera.GetComponent<PassthroughKeeper>() == null)
                camera.gameObject.AddComponent<PassthroughKeeper>();
            if (enabled)
                Debug.Log("[TheMovies] Mixed reality on.");
#endif
        }
    }
}
