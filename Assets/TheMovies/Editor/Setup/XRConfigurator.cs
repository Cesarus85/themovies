using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheMovies.EditorTools
{
    /// <summary>
    /// Enables OpenXR with Meta Quest support for Android (and OpenXR for Windows, so Quest Link works
    /// in the editor). Uses reflection on purpose: the XR packages change their editor APIs between
    /// versions, and a compile error here would block the whole project. If anything fails, the
    /// returned text tells the user what to tick manually.
    /// </summary>
    public static class XRConfigurator
    {
        const string k_Manual = "bitte manuell: Project Settings > XR Plug-in Management > Android: 'OpenXR' + Feature 'Meta Quest Support', Interaction Profile 'Oculus Touch Controller'.";
        const string k_LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        static readonly string[] k_AndroidFeatures =
        {
            "MetaQuestFeature",
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
        };

        static readonly string[] k_DesktopFeatures =
        {
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
        };

        public static string Configure()
        {
            try
            {
                var perTargetType = FindType("UnityEngine.XR.Management.XRGeneralSettingsPerBuildTarget");
                var metadataStore = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
                var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (perTargetType == null || metadataStore == null)
                    return "XR Plug-in Management nicht installiert – " + k_Manual;
                if (openXrSettingsType == null)
                    return "OpenXR-Paket nicht installiert – " + k_Manual;

                var perTarget = GetOrCreatePerTargetSettings(perTargetType);
                bool android = SetupLoader(perTarget, metadataStore, BuildTargetGroup.Android);
                bool desktop = SetupLoader(perTarget, metadataStore, BuildTargetGroup.Standalone);
                int androidFeatures = EnableFeatures(openXrSettingsType, BuildTargetGroup.Android, k_AndroidFeatures);
                EnableFeatures(openXrSettingsType, BuildTargetGroup.Standalone, k_DesktopFeatures);
                AssetDatabase.SaveAssets();

                if (!android)
                    return "OpenXR-Loader für Android konnte nicht gesetzt werden – " + k_Manual;
                if (androidFeatures < 2)
                    return "OpenXR aktiv, aber Meta-Quest-Features nicht gefunden – " + k_Manual;
                return "OpenXR + Meta Quest Support aktiviert" + (desktop ? " (auch für PC/Quest Link)" : string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] XR setup failed: " + e);
                return "Automatische XR-Einrichtung fehlgeschlagen – " + k_Manual;
            }
        }

        static Object GetOrCreatePerTargetSettings(Type perTargetType)
        {
            string key = "com.unity.xr.management.loader_settings";
            var generalType = FindType("UnityEngine.XR.Management.XRGeneralSettings");
            var keyField = generalType?.GetField("k_SettingsKey", BindingFlags.Public | BindingFlags.Static);
            if (keyField != null && keyField.GetValue(null) is string k)
                key = k;

            if (EditorBuildSettings.TryGetConfigObject(key, out Object existing) && existing != null)
                return existing;

            var created = ScriptableObject.CreateInstance(perTargetType);
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.Refresh();
            AssetDatabase.CreateAsset(created, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(key, created, true);
            return created;
        }

        static bool SetupLoader(Object perTarget, Type metadataStore, BuildTargetGroup group)
        {
            var t = perTarget.GetType();
            if (!(bool)Invoke(t, perTarget, "HasSettingsForBuildTarget", group))
                Invoke(t, perTarget, "CreateDefaultSettingsForBuildTarget", group);
            if (!(bool)Invoke(t, perTarget, "HasManagerSettingsForBuildTarget", group))
                Invoke(t, perTarget, "CreateDefaultManagerSettingsForBuildTarget", group);

            var settings = Invoke(t, perTarget, "SettingsForBuildTarget", group);
            if (settings == null) return false;
            var initOnStart = settings.GetType().GetProperty("InitManagerOnStart");
            initOnStart?.SetValue(settings, true);
            var manager = settings.GetType().GetProperty("Manager")?.GetValue(settings) ??
                          settings.GetType().GetProperty("AssignedSettings")?.GetValue(settings);
            if (manager == null) return false;

            var assign = metadataStore.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "AssignLoader" && m.GetParameters().Length == 3);
            if (assign == null) return false;
            object result = assign.Invoke(null, new[] { manager, k_LoaderType, (object)group });
            EditorUtility.SetDirty(perTarget);
            if (manager is Object managerObject) EditorUtility.SetDirty(managerObject);
            return !(result is bool ok) || ok;
        }

        static int EnableFeatures(Type openXrSettingsType, BuildTargetGroup group, string[] featureTypeNames)
        {
            var get = openXrSettingsType.GetMethod("GetSettingsForBuildTargetGroup", BindingFlags.Public | BindingFlags.Static);
            var settings = get?.Invoke(null, new object[] { group });
            if (settings == null) return 0;
            var features = settings.GetType().GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(settings, null) as Array;
            if (features == null) return 0;

            int enabled = 0;
            foreach (var feature in features)
            {
                if (feature == null || !featureTypeNames.Contains(feature.GetType().Name)) continue;
                var prop = feature.GetType().GetProperty("enabled");
                if (prop == null) continue;
                prop.SetValue(feature, true);
                if (feature is Object o) EditorUtility.SetDirty(o);
                enabled++;
            }
            if (settings is Object s) EditorUtility.SetDirty(s);
            return enabled;
        }

        static object Invoke(Type type, object target, string method, BuildTargetGroup group)
        {
            var m = type.GetMethod(method, new[] { typeof(BuildTargetGroup) });
            if (m == null) throw new MissingMethodException(type.Name, method);
            return m.Invoke(target, new object[] { group });
        }

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
