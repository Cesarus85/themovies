using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// After every script reload (editor start, git pull, package change) checks the XR settings of a set-up
    /// project and switches back on what the Quest needs: OpenXR for Android, Meta Quest Support, the Touch
    /// controller profiles and – if the Meta OpenXR package is installed – the passthrough features.
    /// Can be turned off in the menu "The Movies".
    /// </summary>
    [InitializeOnLoad]
    static class XRAutoRepair
    {
        const string k_MenuPath = "The Movies/XR-Haken automatisch reparieren";
        const string k_PrefKey = "TheMovies.XRAutoRepair.Disabled";

        static bool Enabled
        {
            get => !EditorPrefs.GetBool(k_PrefKey, false);
            set => EditorPrefs.SetBool(k_PrefKey, !value);
        }

        static XRAutoRepair()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            if (!Enabled || !File.Exists(ProjectSetup.ScenePath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            var changed = XRConfigurator.RepairQuest();
            if (changed.Count > 0)
                Debug.LogWarning("[TheMovies] XR-Einstellungen waren zurückgesetzt und wurden wieder aktiviert: " + string.Join(", ", changed) +
                                 "\n(Abschalten: Menü The Movies > XR-Haken automatisch reparieren)");
        }

        [MenuItem(k_MenuPath, priority = 32)]
        static void Toggle()
        {
            Enabled = !Enabled;
            if (Enabled) Run();
        }

        [MenuItem(k_MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(k_MenuPath, Enabled);
            return true;
        }
    }
}
