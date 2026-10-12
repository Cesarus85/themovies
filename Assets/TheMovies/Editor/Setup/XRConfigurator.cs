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
            "PassthroughFeature", // The Movies: own passthrough (XR_FB_passthrough)
            "OculusTouchControllerProfile",
            "MetaQuestTouchPlusControllerProfile",
            "MetaQuestTouchProControllerProfile",
            "HandInteractionProfile", // hand tracking: aim pose + pinch (XR_EXT_hand_interaction)
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
                var perTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
                var metadataStore = FindType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
                var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (perTargetType == null || metadataStore == null)
                    return "XR Plug-in Management nicht installiert – " + k_Manual;
                if (openXrSettingsType == null)
                    return "OpenXR-Paket nicht installiert – " + k_Manual;

                var perTarget = GetOrCreatePerTargetSettings(perTargetType);
                bool android = SetupLoader(perTarget, metadataStore, BuildTargetGroup.Android);
                bool desktop = SetupLoader(perTarget, metadataStore, BuildTargetGroup.Standalone);
                RefreshFeatureList();
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

        public enum Readiness
        {
            Ready,
            NotReady,

            /// <summary>The XR package APIs could not be inspected (unknown version); don't block anything.</summary>
            Unknown,
        }

        /// <summary>Checks whether an Android build would start in VR: OpenXR loader active and Meta Quest Support enabled.</summary>
        public static Readiness CheckQuest(out string problem) => CheckQuest(out problem, out _);

        /// <param name="details">What was found (loaders, Meta features), for the diagnostics dialog.</param>
        public static Readiness CheckQuest(out string problem, out string details)
        {
            problem = null;
            details = string.Empty;
            try
            {
                var perTargetType = FindType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
                var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (perTargetType == null || openXrSettingsType == null)
                {
                    bool managementInstalled = IsPackageInstalled("com.unity.xr.management");
                    bool openXrInstalled = IsPackageInstalled("com.unity.xr.openxr");
                    if (!managementInstalled || !openXrInstalled)
                    {
                        problem = !managementInstalled ? "Paket 'XR Plug-in Management' fehlt." : "Paket 'OpenXR Plugin' fehlt.";
                        return Readiness.NotReady;
                    }
                    // Packages are there but their classes moved: don't block the build on our own guesswork.
                    return Readiness.Unknown;
                }

                string key = "com.unity.xr.management.loader_settings";
                if (!EditorBuildSettings.TryGetConfigObject(key, out Object perTarget) || perTarget == null)
                {
                    problem = "XR Plug-in Management ist noch nicht eingerichtet.";
                    return Readiness.NotReady;
                }

                var t = perTarget.GetType();
                var settings = t.GetMethod("SettingsForBuildTarget", new[] { typeof(BuildTargetGroup) })?.Invoke(perTarget, new object[] { BuildTargetGroup.Android });
                var manager = Prop(settings, "Manager")?.GetValue(settings);
                var loaders = (Prop(manager, "activeLoaders")?.GetValue(manager) ??
                               Prop(manager, "loaders")?.GetValue(manager)) as System.Collections.IEnumerable;
                if (settings == null || manager == null || loaders == null)
                {
                    problem = "Für Android ist kein XR-Plug-in ausgewählt.";
                    return settings == null ? Readiness.NotReady : Readiness.Unknown;
                }

                bool openXr = false;
                var loaderNames = new System.Collections.Generic.List<string>();
                foreach (var loader in loaders)
                {
                    if (loader == null) continue;
                    loaderNames.Add(loader.GetType().Name);
                    if (IsOrDerivesFrom(loader.GetType(), "OpenXR"))
                        openXr = true;
                }
                details = "Android-Loader: " + (loaderNames.Count > 0 ? string.Join(", ", loaderNames) : "(keine)");
                if (!openXr)
                {
                    problem = "Für Android ist 'OpenXR' nicht angehakt.";
                    return Readiness.NotReady;
                }

                var openXrSettings = StaticMethod(openXrSettingsType, "GetSettingsForBuildTargetGroup", typeof(BuildTargetGroup))
                    ?.Invoke(null, new object[] { BuildTargetGroup.Android });
                var features = openXrSettings?.GetType().GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(openXrSettings, null) as Array;
                if (features == null) return Readiness.Unknown;
                bool sawQuestFeature = false;
                var metaFeatures = new System.Collections.Generic.List<string>();
                foreach (var feature in features)
                {
                    if (feature == null) continue;
                    string name = feature.GetType().Name;
                    if (name.IndexOf("Meta", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool on = Prop(feature, "enabled")?.GetValue(feature) is bool b && b;
                    metaFeatures.Add(name + (on ? " ✓" : " –"));
                    if (name == "MetaQuestFeature")
                    {
                        sawQuestFeature = true;
                        if (on)
                        {
                            details += "\nMeta/Oculus-Features: " + string.Join(", ", metaFeatures);
                            return Readiness.Ready;
                        }
                    }
                }
                details += "\nMeta/Oculus-Features: " + (metaFeatures.Count > 0 ? string.Join(", ", metaFeatures) : "(keine)");
                if (!sawQuestFeature)
                    return Readiness.Unknown; // feature renamed in this OpenXR version: can't tell
                problem = "OpenXR-Feature 'Meta Quest Support' ist für Android nicht aktiviert.";
                return Readiness.NotReady;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] XR check failed: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return Readiness.Unknown;
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
            var initOnStart = Prop(settings, "InitManagerOnStart");
            initOnStart?.SetValue(settings, true);
            var manager = Prop(settings, "Manager")?.GetValue(settings) ??
                          Prop(settings, "AssignedSettings")?.GetValue(settings);
            if (manager == null) return false;

            var assign = metadataStore.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "AssignLoader" && m.GetParameters().Length == 3);
            if (assign == null) return false;
            object result = assign.Invoke(null, new[] { manager, k_LoaderType, (object)group });
            EditorUtility.SetDirty(perTarget);
            if (manager is Object managerObject) EditorUtility.SetDirty(managerObject);
            return !(result is bool ok) || ok;
        }

        /// <summary>
        /// Turns back on what the Quest build needs if something switched it off (OpenXR loader, Meta Quest Support,
        /// controller profiles, and the passthrough features when the Meta OpenXR package is installed).
        /// </summary>
        /// <returns>Names of what was changed; empty if everything was fine.</returns>
        public static System.Collections.Generic.List<string> RepairQuest()
        {
            var changed = new System.Collections.Generic.List<string>();
            try
            {
                if (CheckQuest(out _) == Readiness.NotReady)
                {
                    Configure();
                    if (CheckQuest(out _) == Readiness.Ready)
                        changed.Add("OpenXR-Loader / Meta Quest Support");
                }
                RefreshFeatureList();
                var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
                if (openXrSettingsType != null)
                {
                    EnableMissingFeatures(openXrSettingsType, BuildTargetGroup.Android, k_AndroidFeatures, changed, "Android");
                    EnableMissingFeatures(openXrSettingsType, BuildTargetGroup.Standalone, k_DesktopFeatures, changed, "PC");
                }
                if (IsMetaOpenXrInstalled && EnablePassthroughFeatures() > 0)
                    changed.Add("Passthrough (Meta Quest: Session/Camera)");
                if (EnsureMetaSession() > 0)
                    changed.Add("Meta Quest: Session");
                if (changed.Count > 0)
                    AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] XR repair failed: " + e.Message);
            }
            return changed;
        }

        /// <summary>Makes OpenXR pick up feature classes added by a pull (e.g. our passthrough feature).</summary>
        static void RefreshFeatureList()
        {
            try
            {
                var helpers = FindType("UnityEditor.XR.OpenXR.Features.FeatureHelpers");
                var refresh = helpers?.GetMethod("RefreshFeatures", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(BuildTargetGroup) }, null);
                refresh?.Invoke(null, new object[] { BuildTargetGroup.Android });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not refresh the OpenXR feature list: " + e.Message);
            }
        }

        static void EnableMissingFeatures(Type openXrSettingsType, BuildTargetGroup group, string[] featureTypeNames,
            System.Collections.Generic.List<string> changed, string label)
        {
            var get = StaticMethod(openXrSettingsType, "GetSettingsForBuildTargetGroup", typeof(BuildTargetGroup));
            var settings = get?.Invoke(null, new object[] { group });
            var features = settings?.GetType().GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(settings, null) as Array;
            if (features == null) return;
            foreach (var feature in features)
            {
                if (feature == null || !featureTypeNames.Contains(feature.GetType().Name) || IsEnabled(feature)) continue;
                var prop = Prop(feature, "enabled");
                if (prop == null) continue;
                prop.SetValue(feature, true);
                if (feature is Object o) EditorUtility.SetDirty(o);
                if (settings is Object s) EditorUtility.SetDirty(s);
                changed.Add(DisplayName(feature) + " (" + label + ")");
            }
        }

        public const string MetaOpenXrPackage = "com.unity.xr.meta-openxr";

        public static bool IsMetaOpenXrInstalled => IsPackageInstalled(MetaOpenXrPackage);

        /// <summary>Enables the Meta OpenXR session and passthrough camera features for Android (mixed reality).</summary>
        public static int EnablePassthroughFeatures() =>
            SetMetaFeatures(name => name.StartsWith("Meta Quest: Session", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("Meta Quest: Camera", StringComparison.OrdinalIgnoreCase) ||
                                    name.IndexOf("Passthrough", StringComparison.OrdinalIgnoreCase) >= 0,
                onlyIfAnyMetaEnabled: false);

        /// <summary>
        /// Every "Meta Quest: …" feature from the Meta OpenXR package needs "Meta Quest: Session"; turns it on
        /// if another one is enabled (otherwise the OpenXR build validation fails).
        /// </summary>
        public static int EnsureMetaSession() =>
            SetMetaFeatures(name => name.StartsWith("Meta Quest: Session", StringComparison.OrdinalIgnoreCase), onlyIfAnyMetaEnabled: true);

        static int SetMetaFeatures(Func<string, bool> wanted, bool onlyIfAnyMetaEnabled)
        {
            var features = AndroidFeatures(out var settings);
            if (features == null) return 0;

            if (onlyIfAnyMetaEnabled)
            {
                bool anyMeta = false;
                foreach (var feature in features)
                    if (feature != null && DisplayName(feature).StartsWith("Meta Quest:", StringComparison.OrdinalIgnoreCase) && IsEnabled(feature))
                        anyMeta = true;
                if (!anyMeta) return 0;
            }

            int changed = 0;
            foreach (var feature in features)
            {
                if (feature == null || !wanted(DisplayName(feature)) || IsEnabled(feature)) continue;
                var prop = Prop(feature, "enabled");
                if (prop == null) continue;
                prop.SetValue(feature, true);
                if (feature is Object o) EditorUtility.SetDirty(o);
                changed++;
            }
            if (changed > 0)
            {
                if (settings is Object s) EditorUtility.SetDirty(s);
                AssetDatabase.SaveAssets();
            }
            return changed;
        }

        static Array AndroidFeatures(out object settings)
        {
            settings = null;
            var openXrSettingsType = FindType("UnityEngine.XR.OpenXR.OpenXRSettings");
            if (openXrSettingsType == null) return null;
            var get = StaticMethod(openXrSettingsType, "GetSettingsForBuildTargetGroup", typeof(BuildTargetGroup));
            settings = get?.Invoke(null, new object[] { BuildTargetGroup.Android });
            return settings?.GetType().GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(settings, null) as Array;
        }

        /// <summary>The name shown in the OpenXR settings ("Meta Quest: Session"), falling back to the class name.</summary>
        static string DisplayName(object feature)
        {
            var type = feature.GetType();
            object value = type.GetField("nameUi", BindingFlags.Public | BindingFlags.Instance)?.GetValue(feature) ??
                           Prop(feature, "nameUi")?.GetValue(feature);
            return value as string ?? type.Name;
        }

        static bool IsEnabled(object feature) =>
            Prop(feature, "enabled")?.GetValue(feature) is bool on && on;

        static int EnableFeatures(Type openXrSettingsType, BuildTargetGroup group, string[] featureTypeNames)
        {
            var get = StaticMethod(openXrSettingsType, "GetSettingsForBuildTargetGroup", typeof(BuildTargetGroup));
            var settings = get?.Invoke(null, new object[] { group });
            if (settings == null) return 0;
            var features = settings.GetType().GetMethod("GetFeatures", Type.EmptyTypes)?.Invoke(settings, null) as Array;
            if (features == null) return 0;

            int enabled = 0;
            foreach (var feature in features)
            {
                if (feature == null || !featureTypeNames.Contains(feature.GetType().Name)) continue;
                var prop = Prop(feature, "enabled");
                if (prop == null) continue;
                prop.SetValue(feature, true);
                if (feature is Object o) EditorUtility.SetDirty(o);
                enabled++;
            }
            if (settings is Object s) EditorUtility.SetDirty(s);
            return enabled;
        }

        /// <summary>
        /// Public instance property of the object's class, the most derived one if a subclass hides a base
        /// property ("new"): a plain GetProperty throws "Ambiguous match found" then.
        /// </summary>
        static PropertyInfo Prop(object target, string name)
        {
            if (target == null) return null;
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (p.Name == name && p.GetIndexParameters().Length == 0)
                        return p;
            }
            return null;
        }

        /// <summary>Public static method with exactly these parameters (other overloads don't make it ambiguous).</summary>
        static MethodInfo StaticMethod(Type type, string name, params Type[] parameters) =>
            type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);

        static object Invoke(Type type, object target, string method, BuildTargetGroup group)
        {
            var m = type.GetMethod(method, new[] { typeof(BuildTargetGroup) });
            if (m == null) throw new MissingMethodException(type.Name, method);
            return m.Invoke(target, new object[] { group });
        }

        static bool IsOrDerivesFrom(Type type, string namePart)
        {
            for (var t = type; t != null; t = t.BaseType)
                if (t.Name.IndexOf(namePart, StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }

        static bool IsPackageInstalled(string name) =>
            UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + name) != null;

        /// <summary>Finds a type by full name, or by its simple name if the namespace differs between package versions.</summary>
        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }

            string simpleName = fullName.Substring(fullName.LastIndexOf('.') + 1);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name;
                if (!asmName.StartsWith("Unity.XR", StringComparison.Ordinal)) continue;
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                foreach (var t in types)
                    if (t != null && t.Name == simpleName)
                        return t;
            }
            return null;
        }
    }
}
