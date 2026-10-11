using System;
using System.IO;
using TheMovies.App;
using TheMovies.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// One-click project setup: generates the network prefabs, the scene and configures the player
    /// settings for Meta Quest. Safe to run repeatedly.
    /// </summary>
    public static class ProjectSetup
    {
        public const string GeneratedFolder = "Assets/TheMovies/Generated";
        public const string ScenePath = "Assets/Scenes/Cinema.unity";
        const string k_PlayerPrefabPath = GeneratedFolder + "/NetworkPlayer.prefab";
        const string k_SyncPrefabPath = GeneratedFolder + "/CinemaSync.prefab";
        const string k_PrefabListPath = GeneratedFolder + "/TheMoviesNetworkPrefabs.asset";
        const string k_ApkPath = "Builds/TheMovies.apk";

        [MenuItem("The Movies/1. Projekt einrichten", priority = 0)]
        public static void SetupAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            try
            {
                EditorUtility.DisplayProgressBar("The Movies", "Prefabs erzeugen …", 0.1f);
                var player = CreateNetworkPrefab<NetworkPlayer>("NetworkPlayer", k_PlayerPrefabPath);
                var sync = CreateNetworkPrefab<CinemaSync>("CinemaSync", k_SyncPrefabPath);
                var list = CreatePrefabList(player, sync);

                EditorUtility.DisplayProgressBar("The Movies", "Szene erzeugen …", 0.4f);
                CreateScene(player, sync, list);

                EditorUtility.DisplayProgressBar("The Movies", "Player Settings …", 0.7f);
                ConfigurePlayerSettings();

                EditorUtility.DisplayProgressBar("The Movies", "XR (OpenXR / Meta Quest) …", 0.85f);
                string xr = XRConfigurator.Configure();
                AssetDatabase.SaveAssets();
                EditorUtility.ClearProgressBar();

                bool android = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
                string message = "Fertig!\n\n" +
                                 "• Szene: " + ScenePath + "\n" +
                                 "• Prefabs: " + GeneratedFolder + "\n" +
                                 "• XR: " + xr + "\n\n" +
                                 (android
                                     ? "Mit 'The Movies > 2. APK für Quest bauen' erzeugst du die App."
                                     : "Die Build-Plattform ist noch nicht Android. Jetzt umstellen? (dauert einen Moment)");
                if (android)
                {
                    EditorUtility.DisplayDialog("The Movies", message, "OK");
                }
                else if (EditorUtility.DisplayDialog("The Movies", message, "Auf Android umstellen", "Später"))
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                }
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                EditorUtility.DisplayDialog("The Movies", "Einrichtung fehlgeschlagen:\n" + e.Message, "OK");
            }
        }

        [MenuItem("The Movies/2. APK für Quest bauen", priority = 20)]
        public static void BuildApk()
        {
            if (!File.Exists(ScenePath))
                SetupAll();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog("The Movies", "Bitte zuerst die Build-Plattform auf Android umstellen (File > Build Profiles).", "OK");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(k_ApkPath));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = k_ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.RevealInFinder(k_ApkPath);
                EditorUtility.DisplayDialog("The Movies",
                    $"APK erstellt ({report.summary.totalSize / (1024 * 1024)} MB):\n{Path.GetFullPath(k_ApkPath)}\n\n" +
                    "Installieren: Quest per USB verbinden und mit dem Meta Quest Developer Hub oder 'adb install -r' aufspielen.",
                    "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("The Movies", "Build fehlgeschlagen – Details in der Console.", "OK");
            }
        }

        [MenuItem("The Movies/VR-Einrichtung prüfen", priority = 30)]
        public static void CheckXR()
        {
            var state = XRConfigurator.CheckQuest(out string problem, out string details);
            string found = string.IsNullOrEmpty(details) ? string.Empty : "\n\nGefunden:\n" + details;
            switch (state)
            {
                case XRConfigurator.Readiness.Ready:
                    EditorUtility.DisplayDialog("The Movies", "Alles bereit: OpenXR und Meta Quest Support sind für Android aktiv." + found, "OK");
                    return;
                case XRConfigurator.Readiness.Unknown:
                    EditorUtility.DisplayDialog("The Movies",
                        "Die XR-Einstellungen ließen sich nicht eindeutig auslesen. Wenn unter XR Plug-in Management (Android) " +
                        "'OpenXR' und unter OpenXR 'Meta Quest Support' angehakt sind, passt es." + found, "OK");
                    return;
            }

            if (EditorUtility.DisplayDialog("The Movies", problem + found + "\n\nJetzt automatisch einrichten?", "Einrichten", "Abbrechen"))
            {
                string result = XRConfigurator.Configure();
                var after = XRConfigurator.CheckQuest(out problem, out details);
                EditorUtility.DisplayDialog("The Movies",
                    result + (after == XRConfigurator.Readiness.NotReady ? "\n\nNoch offen: " + problem + "\n" + details : string.Empty), "OK");
            }
        }

        [MenuItem("The Movies/Mixed Reality (Passthrough) einrichten", priority = 31)]
        public static void SetupMixedReality()
        {
            if (!XRConfigurator.IsMetaOpenXrInstalled)
            {
                if (EditorUtility.DisplayDialog("The Movies",
                        "Für Mixed Reality wird das Paket 'Unity OpenXR: Meta' (" + XRConfigurator.MetaOpenXrPackage + ") benötigt. " +
                        "Jetzt installieren?\n\nDanach diesen Menüpunkt noch einmal aufrufen.", "Installieren", "Abbrechen"))
                {
                    string id = XRConfigurator.MetaOpenXrPackage + "@" + MetaOpenXrVersionForThisEditor();
                    Debug.Log("[TheMovies] Installing " + id + " – run 'Mixed Reality einrichten' again afterwards.");
                    s_AddRequest = UnityEditor.PackageManager.Client.Add(id);
                    EditorApplication.update -= WatchAddRequest;
                    EditorApplication.update += WatchAddRequest;
                }
                return;
            }

            int enabled = XRConfigurator.EnablePassthroughFeatures();
            EditorUtility.DisplayDialog("The Movies", enabled > 0
                ? $"Passthrough eingerichtet ({enabled} OpenXR-Features aktiviert). Neu bauen – dann gibt es in den Einstellungen die Umgebung 'Mixed Reality'."
                : "Die Passthrough-Features wurden nicht gefunden. Bitte unter Project Settings > XR Plug-in Management > OpenXR (Android) " +
                  "'Meta Quest: Session' und 'Meta Quest: Camera (Passthrough)' anhaken.", "OK");
        }

        static UnityEditor.PackageManager.Requests.AddRequest s_AddRequest;

        /// <summary>
        /// Versions Unity's own AR Foundation samples use with each editor line (Unity 6.3 → 2.3.0 etc.);
        /// installing without a version can pick one that needs a newer editor.
        /// </summary>
        static string MetaOpenXrVersionForThisEditor()
        {
            string v = Application.unityVersion;
            if (v.StartsWith("6000.5", StringComparison.Ordinal)) return "2.5.0";
            if (v.StartsWith("6000.4", StringComparison.Ordinal)) return "2.4.0";
            return "2.3.0";
        }

        static void WatchAddRequest()
        {
            if (s_AddRequest == null || !s_AddRequest.IsCompleted) return;
            EditorApplication.update -= WatchAddRequest;
            var request = s_AddRequest;
            s_AddRequest = null;
            if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
            {
                EditorUtility.DisplayDialog("The Movies",
                    "Paket installiert: " + request.Result.packageId + "\n\nBitte 'The Movies > Mixed Reality (Passthrough) einrichten' jetzt noch einmal aufrufen.", "OK");
            }
            else
            {
                string error = request.Error != null ? request.Error.message : "unbekannter Fehler";
                Debug.LogError("[TheMovies] Installing the Meta OpenXR package failed: " + error);
                EditorUtility.DisplayDialog("The Movies",
                    "Installation fehlgeschlagen:\n" + error + "\n\nBitte diese Meldung an den Entwickler schicken.", "OK");
            }
        }

        [MenuItem("The Movies/Szene öffnen", priority = 40)]
        public static void OpenScene()
        {
            if (!File.Exists(ScenePath))
            {
                SetupAll();
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static GameObject CreateNetworkPrefab<T>(string name, string path) where T : Component
        {
            EnsureFolder(GeneratedFolder);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<NetworkObject>() != null && existing.GetComponent<T>() != null)
                return existing;

            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent<T>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static NetworkPrefabsList CreatePrefabList(params GameObject[] prefabs)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(k_PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, k_PrefabListPath);
            }
            foreach (var prefab in prefabs)
            {
                if (!list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });
            }
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
            return list;
        }

        static void CreateScene(GameObject playerPrefab, GameObject syncPrefab, NetworkPrefabsList list)
        {
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var networkGo = new GameObject("Network");
            var network = networkGo.AddComponent<NetworkManager>();
            var transport = networkGo.AddComponent<UnityTransport>();
            if (network.NetworkConfig == null)
                network.NetworkConfig = new NetworkConfig();
            network.NetworkConfig.NetworkTransport = transport;
            network.NetworkConfig.ConnectionApproval = true;
            network.NetworkConfig.PlayerPrefab = null;
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();
            network.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);

            var sessions = networkGo.AddComponent<SessionManager>();
            var so = new SerializedObject(sessions);
            so.FindProperty("m_PlayerPrefab").objectReferenceValue = playerPrefab;
            so.FindProperty("m_CinemaSyncPrefab").objectReferenceValue = syncPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            var appGo = new GameObject("The Movies");
            var app = appGo.AddComponent<AppController>();
            var appSo = new SerializedObject(app);
            appSo.FindProperty("m_Sessions").objectReferenceValue = sessions;
            appSo.ApplyModifiedPropertiesWithoutUndo();

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            EditorUtility.SetDirty(network);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "TheMovies";
            PlayerSettings.productName = "The Movies";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.themovies.cinema");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Quest OS: API 32 (Android 12L). Targeting 32 keeps classic storage access to /sdcard/Movies.
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.forceInternetPermission = true;
            // 32-bit color with alpha: passthrough shows where the rendered image is transparent.
            PlayerSettings.use32BitDisplayBuffer = true;
            PlayerSettings.Android.forceSDCardPermission = true;
            // http:// streams (and the local stream server on 127.0.0.1) need cleartext HTTP on Android.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            // 4x MSAA on all quality levels: text and edges look much better in VR.
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.antiAliasing = 4;
            }
            QualitySettings.SetQualityLevel(current, false);

            EnableBothInputHandlers();
        }

        /// <summary>Keyboard, mouse and controller input go through the Input System package, so it must be active.</summary>
        static void EnableBothInputHandlers()
        {
            try
            {
                var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue == 0)
                {
                    prop.intValue = 2; // Both
                    so.ApplyModifiedProperties();
                    Debug.Log("[TheMovies] Active Input Handling set to 'Both'. Unity may ask for a restart.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not set Active Input Handling: " + e.Message);
            }
        }
    }
}
