// Minimal UnityEditor API stubs (signatures as in Unity 6) for the editor setup scripts.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
        public int priority;
    }

    public enum BuildTarget { StandaloneWindows64 = 19, Android = 13 }
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1, Android = 7 }
    [Flags] public enum BuildOptions { None = 0, Development = 1 }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }
    [Flags] public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, All = -1 }
    public enum AndroidSdkVersions { AndroidApiLevelAuto = 0, AndroidApiLevel32 = 32 }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
    }

    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget => default;
        public static bool SwitchActiveBuildTarget(BuildTargetGroup group, BuildTarget target) => false;
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { }
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
        public static bool TryGetConfigObject<T>(string name, out T result) where T : Object { result = null; return false; }
        public static void AddConfigObject(string name, Object obj, bool overwrite) { }
    }

    public static class EditorUtility
    {
        public static void SetDirty(Object target) { }
        public static bool DisplayDialog(string title, string message, string ok) => true;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
        public static void RevealInFinder(string path) { }
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => null;
        public static void CreateAsset(Object asset, string path) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parentFolder, string newFolderName) => null;
    }

    public static class PrefabUtility
    {
        public static GameObject SaveAsPrefabAsset(GameObject instanceRoot, string assetPath) => null;
    }

    public class SerializedProperty
    {
        public Object objectReferenceValue { get; set; }
        public int intValue { get; set; }
    }

    public class SerializedObject
    {
        public SerializedObject(Object obj) { }
        public SerializedProperty FindProperty(string propertyPath) => null;
        public bool ApplyModifiedProperties() => true;
        public bool ApplyModifiedPropertiesWithoutUndo() => true;
    }

    public static class Unsupported
    {
        public static Object GetSerializedAssetInterfaceSingleton(string className) => null;
    }

    public static class PlayerSettings
    {
        public static string companyName { get; set; }
        public static string productName { get; set; }
        public static ColorSpace colorSpace { get; set; }
        public static bool runInBackground { get; set; }
        public static void SetApplicationIdentifier(Build.NamedBuildTarget buildTarget, string identifier) { }
        public static void SetScriptingBackend(Build.NamedBuildTarget buildTarget, ScriptingImplementation backend) { }
        public static void SetUseDefaultGraphicsAPIs(BuildTarget platform, bool automatic) { }
        public static void SetGraphicsAPIs(BuildTarget platform, GraphicsDeviceType[] apis) { }

        public static class Android
        {
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
            public static bool forceInternetPermission { get; set; }
            public static bool forceSDCardPermission { get; set; }
        }
    }
}

namespace UnityEditor.Build
{
    public readonly struct NamedBuildTarget
    {
        public static readonly NamedBuildTarget Android = default;
        public static readonly NamedBuildTarget Standalone = default;
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public struct BuildSummary
    {
        public BuildResult result => default;
        public ulong totalSize => 0;
    }
    public class BuildReport { public BuildSummary summary => default; }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static bool SaveScene(Scene scene, string dstScenePath) => true;
        public static bool MarkSceneDirty(Scene scene) => true;
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
        public static Scene OpenScene(string scenePath) => default;
    }
}
