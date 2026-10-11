#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// The Quest only shows passthrough to apps that declare "com.oculus.feature.PASSTHROUGH" in their manifest.
    /// The Meta OpenXR package normally adds it; if no manifest of the generated Gradle project has it, this adds it
    /// (required="false", so the merge with a library declaring it stays conflict-free).
    /// </summary>
    sealed class PassthroughManifest : IPostGenerateGradleAndroidProject
    {
        const string k_Feature = "com.oculus.feature.PASSTHROUGH";

        // Run after the XR packages' own manifest processing.
        public int callbackOrder => 10000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (!XRConfigurator.IsMetaOpenXrInstalled) return;

            // "path" is the unityLibrary folder; the launcher sits next to it.
            string root = Path.GetDirectoryName(path.TrimEnd('/', '\\')) ?? path;
            foreach (string manifest in Directory.GetFiles(root, "AndroidManifest.xml", SearchOption.AllDirectories))
            {
                if (manifest.Contains(Path.DirectorySeparatorChar + "build" + Path.DirectorySeparatorChar)) continue;
                if (File.ReadAllText(manifest).Contains(k_Feature)) return;
            }

            string target = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(target))
            {
                Debug.LogWarning("[TheMovies] Passthrough: AndroidManifest.xml not found in " + path);
                return;
            }
            string xml = File.ReadAllText(target);
            var application = new Regex(@"<application[\s>]");
            var match = application.Match(xml);
            if (!match.Success)
            {
                Debug.LogWarning("[TheMovies] Passthrough: no <application> in " + target);
                return;
            }
            xml = xml.Insert(match.Index, "<uses-feature android:name=\"" + k_Feature + "\" android:required=\"false\" />\n  ");
            File.WriteAllText(target, xml);
            Debug.Log("[TheMovies] Added " + k_Feature + " to the Android manifest (passthrough).");
        }
    }
}
#endif
