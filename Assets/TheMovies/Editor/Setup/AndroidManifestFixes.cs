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
    /// </summary>
    sealed class AndroidManifestFixes : IPostGenerateGradleAndroidProject
    {
        const string k_Passthrough = "com.oculus.feature.PASSTHROUGH";

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

            // Passthrough feature
            if (XRConfigurator.IsMetaOpenXrInstalled && File.Exists(libraryManifest))
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
