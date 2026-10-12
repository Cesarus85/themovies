#if UNITY_ANDROID
using System.Collections.Generic;
using System.IO;
using TheMovies.Core.Build;
using UnityEditor.Android;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Final touches on the generated Gradle project:
    /// - Cleartext HTTP must be allowed: the video player reads NAS files and the host stream from the local proxy
    ///   on http://127.0.0.1, DLNA servers only speak http. Android blocks that by default ("Cannot read file").
    /// - The Quest only shows passthrough to apps declaring "com.oculus.feature.PASSTHROUGH" (normally added by
    ///   the Meta OpenXR package; added here if no manifest has it).
    /// - The own video player (Plugins/Android/TheMoviesPlayer.java) needs ExoPlayer/Media3 and, for AC3/DTS/TrueHD
    ///   sound, the FFmpeg audio decoder built by Jellyfin; its class must survive minification.
    /// </summary>
    sealed class AndroidManifestFixes : IPostGenerateGradleAndroidProject
    {
        const string k_Passthrough = "com.oculus.feature.PASSTHROUGH";
        public const string Media3Version = "1.5.0";
        public const string FfmpegDecoder = "org.jellyfin.media3:media3-ffmpeg-decoder:1.5.0+1";

        // After the XR packages' own manifest processing.
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // "path" is the unityLibrary folder; the launcher sits next to it.
            string root = Path.GetDirectoryName(path.TrimEnd('/', '\\')) ?? path;
            string libraryManifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            var manifests = new List<string>();
            foreach (string module in Directory.GetDirectories(root))
            {
                string m = Path.Combine(module, "src", "main", "AndroidManifest.xml");
                if (File.Exists(m)) manifests.Add(m);
            }
            if (!manifests.Contains(libraryManifest) && File.Exists(libraryManifest)) manifests.Add(libraryManifest);
            if (manifests.Count == 0)
            {
                Debug.LogWarning("[TheMovies] No AndroidManifest.xml found below " + root);
                return;
            }

            // Cleartext HTTP
            string configName = null;
            string configModule = null;
            foreach (string m in manifests)
            {
                string text = File.ReadAllText(m);
                string name = ManifestEdits.NetworkSecurityConfigName(text);
                if (name != null)
                {
                    configName = name;
                    configModule = Path.GetDirectoryName(m);
                }
                string changed = ManifestEdits.AllowCleartext(text, addIfMissing: false);
                if (changed != text) File.WriteAllText(m, changed);
            }
            if (configName != null)
            {
                string config = FindConfig(root, configModule, configName);
                if (config != null)
                {
                    string text = File.ReadAllText(config);
                    string changed = ManifestEdits.AllowCleartextInConfig(text);
                    if (changed != text)
                    {
                        File.WriteAllText(config, changed);
                        Debug.Log("[TheMovies] Allowed cleartext HTTP in " + config);
                    }
                }
                else
                {
                    Debug.LogWarning("[TheMovies] Network security config '" + configName + "' not found – HTTP to the NAS/proxy may be blocked.");
                }
            }
            else if (File.Exists(libraryManifest))
            {
                string text = File.ReadAllText(libraryManifest);
                string changed = ManifestEdits.AllowCleartext(text, addIfMissing: true);
                if (changed != text)
                {
                    File.WriteAllText(libraryManifest, changed);
                    Debug.Log("[TheMovies] Set android:usesCleartextTraffic=\"true\" (local stream proxy, DLNA).");
                }
            }

            AddPlayerDependencies(root, path);

            // Hand tracking (pinch to click); optional, controllers keep working without it.
            if (File.Exists(libraryManifest))
            {
                string text = File.ReadAllText(libraryManifest);
                string changed = ManifestEdits.AddPermission(text, "com.oculus.permission.HAND_TRACKING");
                changed = ManifestEdits.AddUsesFeature(changed, "oculus.software.handtracking", required: false);
                // The headset's controller models (XR_FB_render_model): without these the runtime hides the extension.
                changed = ManifestEdits.AddPermission(changed, "com.oculus.permission.RENDER_MODEL");
                changed = ManifestEdits.AddUsesFeature(changed, "com.oculus.feature.RENDER_MODEL", required: false);
                if (changed != text)
                {
                    File.WriteAllText(libraryManifest, changed);
                    Debug.Log("[TheMovies] Hand tracking and controller render models declared in the Android manifest.");
                }
            }

            // Passthrough feature
            if (File.Exists(libraryManifest)) // own passthrough feature or the Meta OpenXR package
            {
                bool declared = false;
                foreach (string m in manifests)
                    if (File.ReadAllText(m).Contains(k_Passthrough)) declared = true;
                if (!declared)
                {
                    string text = File.ReadAllText(libraryManifest);
                    File.WriteAllText(libraryManifest, ManifestEdits.AddUsesFeature(text, k_Passthrough, required: false));
                    Debug.Log("[TheMovies] Added " + k_Passthrough + " to the Android manifest.");
                }
            }
        }

        static void AddPlayerDependencies(string root, string libraryPath)
        {
            string gradle = Path.Combine(libraryPath, "build.gradle");
            if (!File.Exists(gradle))
            {
                Debug.LogWarning("[TheMovies] unityLibrary/build.gradle not found – the own video player will be missing.");
                return;
            }
            var deps = new List<string>
            {
                "androidx.media3:media3-exoplayer:" + Media3Version,
                "androidx.media3:media3-datasource:" + Media3Version,
                "androidx.media3:media3-common:" + Media3Version,
            };
            if (PlayerBuildOptions.FfmpegAudio) deps.Add(FfmpegDecoder);
            string text = File.ReadAllText(gradle);
            string changed = GradleEdits.AddDependencies(text, deps.ToArray());
            if (!PlayerBuildOptions.FfmpegAudio)
                changed = GradleEdits.RemoveDependency(changed, FfmpegDecoder.Substring(0, FfmpegDecoder.LastIndexOf(':')));
            if (changed != text) File.WriteAllText(gradle, changed);

            // Media3 must be compiled against API 35+; the target API (storage access) stays unchanged.
            int sdk = HighestInstalledPlatform();
            if (sdk > 0 && sdk < k_MinCompileSdk)
                Debug.LogWarning($"[TheMovies] Android SDK platform {k_MinCompileSdk}+ fehlt (höchste installierte: {sdk}). " +
                                 "Bitte im Unity Hub die Android-SDK-Module aktualisieren.");
            int compileSdk = sdk >= k_MinCompileSdk ? sdk : k_MinCompileSdk;
            foreach (string module in Directory.GetDirectories(root))
            {
                string moduleGradle = Path.Combine(module, "build.gradle");
                if (!File.Exists(moduleGradle)) continue;
                string g = File.ReadAllText(moduleGradle);
                int current = GradleEdits.CompileSdk(g);
                if (current < 0 || current >= k_MinCompileSdk) continue;
                File.WriteAllText(moduleGradle, GradleEdits.RaiseCompileSdk(g, compileSdk));
                Debug.Log($"[TheMovies] {Path.GetFileName(module)}: compileSdk {current} → {compileSdk} (für Media3).");
            }

            string properties = Path.Combine(root, "gradle.properties");
            string props = File.Exists(properties) ? File.ReadAllText(properties) : string.Empty;
            string newProps = GradleEdits.SetProperty(props, "android.useAndroidX", "true");
            if (newProps != props) File.WriteAllText(properties, newProps);

            const string keep = "-keep class com.themovies.player.** { *; }";
            foreach (string name in new[] { "proguard-unity.txt", "proguard-user.txt" })
            {
                string rules = Path.Combine(libraryPath, name);
                if (!File.Exists(rules)) continue;
                string r = File.ReadAllText(rules);
                string nr = GradleEdits.AddRule(r, keep);
                if (nr != r) File.WriteAllText(rules, nr);
            }
            Debug.Log("[TheMovies] Video player: Media3 " + Media3Version + (PlayerBuildOptions.FfmpegAudio ? " + FFmpeg audio (AC3/DTS)" : string.Empty));
        }

        const int k_MinCompileSdk = 35;

        static int HighestInstalledPlatform()
        {
            try
            {
                string sdkRoot = AndroidExternalToolsSettings.sdkRootPath;
                string platforms = Path.Combine(sdkRoot ?? string.Empty, "platforms");
                if (!Directory.Exists(platforms)) return -1;
                int best = -1;
                foreach (string dir in Directory.GetDirectories(platforms))
                {
                    string name = Path.GetFileName(dir); // android-35, android-36, android-36-ext…
                    if (!name.StartsWith("android-")) continue;
                    string number = name.Substring(8);
                    int dash = number.IndexOf('-');
                    if (dash >= 0) number = number.Substring(0, dash);
                    if (int.TryParse(number, out int api) && api > best && File.Exists(Path.Combine(dir, "android.jar")))
                        best = api;
                }
                return best;
            }
            catch (System.Exception)
            {
                return -1;
            }
        }

        static string FindConfig(string root, string preferredModule, string name)
        {
            if (preferredModule != null)
            {
                string direct = Path.Combine(preferredModule, "res", "xml", name + ".xml");
                if (File.Exists(direct)) return direct;
            }
            foreach (string file in Directory.GetFiles(root, name + ".xml", SearchOption.AllDirectories))
            {
                string dir = Path.GetFileName(Path.GetDirectoryName(file));
                if (dir == "xml" && !file.Contains(Path.DirectorySeparatorChar + "build" + Path.DirectorySeparatorChar))
                    return file;
            }
            return null;
        }
    }
}
#endif
