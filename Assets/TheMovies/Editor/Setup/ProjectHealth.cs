using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Repairs on editor start what makes Android builds fail:
    /// <list type="bullet">
    /// <item>Active Input Handling "Both": since Unity 6.3 a build from the editor cancels with it. The app only
    /// uses the Input System, so it is switched to that (takes effect after a restart).</item>
    /// <item>XR Simulation settings stuck in Assets/XR/Temp: AR Foundation moves them there for a build and back
    /// afterwards; after a cancelled build they stay, get created anew, and every later build fails to move them.
    /// The originals are moved back (the build settings refer to their GUIDs).</item>
    /// </list>
    /// </summary>
    [InitializeOnLoad]
    static class ProjectHealth
    {
        const string k_XRTemp = "Assets/XR/Temp";

        static readonly (string temp, string original)[] k_SimulationAssets =
        {
            (k_XRTemp + "/XRSimulationPreferences.asset", "Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset"),
            (k_XRTemp + "/XRSimulationRuntimeSettings.asset", "Assets/XR/Resources/XRSimulationRuntimeSettings.asset"),
        };

        static ProjectHealth()
        {
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (BuildPipeline.isBuildingPlayer) return; // the files are in Temp on purpose during a build
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            RestoreSimulationSettings();
            if (UseInputSystemOnly() && !Application.isBatchMode &&
                EditorUtility.DisplayDialog("The Movies",
                    "Active Input Handling war auf 'Both' – damit bricht Unity 6.3 Android-Builds ab. Umgestellt auf 'Input System Package'.\n\n" +
                    "Unity muss dafür neu starten.", "Jetzt neu starten", "Später"))
            {
                EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            }
        }

        /// <summary>Sets Active Input Handling to the Input System package only.</summary>
        /// <returns>True if it was changed (Unity needs a restart to apply it).</returns>
        public static bool UseInputSystemOnly()
        {
            try
            {
                var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
                var so = new SerializedObject(settings);
                var prop = so.FindProperty("activeInputHandler");
                if (prop == null || prop.intValue == 1) return false; // 0 = old Input Manager, 1 = Input System, 2 = both
                prop.intValue = 1;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                Debug.Log("[TheMovies] Active Input Handling set to 'Input System Package' (Unity 6.3 cancels Android builds with 'Both'). Restart Unity to apply.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not set Active Input Handling: " + e.Message);
                return false;
            }
        }

        static void RestoreSimulationSettings()
        {
            bool any = false;
            foreach (var (temp, original) in k_SimulationAssets)
            {
                if (!File.Exists(temp)) continue;
                any = true;
                if (File.Exists(original) && !AssetDatabase.DeleteAsset(original))
                {
                    Debug.LogWarning("[TheMovies] XR Simulation: could not replace " + original);
                    continue;
                }
                string error = AssetDatabase.MoveAsset(temp, original);
                if (!string.IsNullOrEmpty(error))
                    Debug.LogWarning("[TheMovies] XR Simulation: could not move " + temp + " back: " + error);
            }
            if (!any) return;
            if (AssetDatabase.IsValidFolder(k_XRTemp) && AssetDatabase.FindAssets(string.Empty, new[] { k_XRTemp }).Length == 0)
                AssetDatabase.DeleteAsset(k_XRTemp);
            Debug.Log("[TheMovies] XR Simulation settings left over from a cancelled build were moved back.");
        }
    }
}
