using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Warns (in the console) when an Android build looks like it would start as a flat 2D window on the
    /// Quest because OpenXR or "Meta Quest Support" is not enabled. Never blocks the build: the check reads
    /// XR package internals via reflection and can be wrong for other package versions.
    /// </summary>
    sealed class QuestBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            // The host stream is served to the video player via http://127.0.0.1, which Android blocks by default.
            if (PlayerSettings.insecureHttpOption != InsecureHttpOption.AlwaysAllowed)
            {
                PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
                Debug.Log("[TheMovies] Player Settings: 'Allow downloads over HTTP' set to 'Always allowed'.");
            }

            // Ticks that got lost (controller profiles, passthrough …) are set again before they go missing in the APK.
            var repaired = XRConfigurator.RepairQuest();
            if (repaired.Count > 0)
                Debug.LogWarning("[TheMovies] Vor dem Bauen wieder aktiviert: " + string.Join(", ", repaired));

            if (XRConfigurator.CheckQuest(out string problem, out string details) == XRConfigurator.Readiness.NotReady)
            {
                Debug.LogWarning("[TheMovies] Möglicherweise startet die App auf der Quest nur als flaches Fenster: " + problem +
                                 "\n" + details +
                                 "\nBitte unter Edit > Project Settings > XR Plug-in Management (Android) prüfen: 'OpenXR' und 'Meta Quest Support'.");
            }
        }
    }
}
