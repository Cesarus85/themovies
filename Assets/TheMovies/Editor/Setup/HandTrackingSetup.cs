using UnityEditor;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Menu "The Movies > Handtracking einrichten": installs Unity's XR Hands package and switches on its OpenXR
    /// features "Hand Tracking Subsystem" and "Meta Hand Tracking Aim" for Android – the way Unity documents hand
    /// input on the Quest. The app then gets an aim pose and the pinch strength per hand (device MetaAimHand).
    /// </summary>
    [InitializeOnLoad]
    public static class HandTrackingSetup
    {
        public const string Package = "com.unity.xr.hands";
        const string k_PendingKey = "TheMovies.HandTrackingSetupPending";

        static UnityEditor.PackageManager.Requests.AddRequest s_Add;

        static bool PackageInstalled => UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + Package) != null;

        static HandTrackingSetup()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += ContinueAfterReload;
        }

        static void ContinueAfterReload()
        {
            if (!SessionState.GetBool(k_PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ContinueAfterReload;
                return;
            }
            if (!PackageInstalled) return;
            SessionState.SetBool(k_PendingKey, false);
            EnableFeatures();
        }

        [MenuItem("The Movies/Handtracking einrichten", priority = 35)]
        public static void Setup()
        {
            if (s_Add != null) return;
            if (PackageInstalled)
            {
                EnableFeatures();
                return;
            }
            if (!EditorUtility.DisplayDialog("The Movies",
                    "Für Handtracking auf der Quest braucht das Projekt Unitys Paket „XR Hands“ (" + Package + "). " +
                    "Danach werden die OpenXR-Features „Hand Tracking Subsystem“ und „Meta Hand Tracking Aim“ angeschaltet.\n\nJetzt installieren?",
                    "Installieren", "Abbrechen"))
                return;
            SessionState.SetBool(k_PendingKey, true);
            s_Add = UnityEditor.PackageManager.Client.Add(Package);
            EditorApplication.update += WatchAdd;
        }

        static void WatchAdd()
        {
            if (s_Add == null || !s_Add.IsCompleted) return;
            EditorApplication.update -= WatchAdd;
            var request = s_Add;
            s_Add = null;
            if (request.Status != UnityEditor.PackageManager.StatusCode.Success)
            {
                SessionState.SetBool(k_PendingKey, false);
                string error = request.Error != null ? request.Error.message : "unbekannter Fehler";
                Debug.LogError("[TheMovies] Installing " + Package + " failed: " + error);
                EditorUtility.DisplayDialog("The Movies", "Paket-Installation fehlgeschlagen:\n" + error, "OK");
                return;
            }
            Debug.Log("[TheMovies] Installed " + request.Result.packageId);
            // Usually Unity reloads the scripts now and ContinueAfterReload takes over.
        }

        static void EnableFeatures()
        {
            var changed = XRConfigurator.RepairQuest();
            Debug.Log("[TheMovies] Hand tracking setup, enabled: " + (changed.Count > 0 ? string.Join(", ", changed) : "(war schon an)"));
            EditorUtility.DisplayDialog("The Movies",
                "Handtracking eingerichtet" + (changed.Count > 0 ? " (" + string.Join(", ", changed) + ")" : string.Empty) + ".\n\n" +
                "Bitte prüfen: Project Settings > XR Plug-in Management > OpenXR (Android): „Hand Tracking Subsystem“ und " +
                "„Meta Hand Tracking Aim“ angehakt. Dann neu bauen. Auf der Quest muss Handtracking in den Einstellungen an sein.", "OK");
        }
    }
}
