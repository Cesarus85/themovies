// API stubs for AR Foundation (only what the mixed reality mode uses).
namespace UnityEngine.XR.ARFoundation
{
    public enum ARSessionState { None, Unsupported, CheckingAvailability, NeedsInstall, Installing, Ready, SessionInitializing, SessionTracking }

    public class ARSession : MonoBehaviour
    {
        public static ARSessionState state => default;
    }

    public class CameraSubsystemStub
    {
        public bool running => false;
    }

    public class ARCameraManager : MonoBehaviour
    {
        public CameraSubsystemStub subsystem => null;
    }
}

namespace UnityEngine.XR.OpenXR
{
    public static class OpenXRRuntime
    {
        public static string name => string.Empty;
        public static string version => string.Empty;
        public static bool IsExtensionEnabled(string extensionName) => false;
    }
}

namespace UnityEngine.XR.OpenXR.Features
{
    public abstract class OpenXRFeature : UnityEngine.ScriptableObject
    {
        public bool enabled { get; set; }
        protected virtual System.IntPtr HookGetInstanceProcAddr(System.IntPtr func) => func;
        protected virtual bool OnInstanceCreate(ulong xrInstance) => true;
        protected virtual void OnSessionCreate(ulong xrSession) { }
        protected virtual void OnSessionDestroy(ulong xrSession) { }
    }
}

#if UNITY_EDITOR
namespace UnityEditor.XR.OpenXR.Features
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class OpenXRFeatureAttribute : System.Attribute
    {
        public string UiName; public string Desc; public string Company; public string OpenxrExtensionStrings; public string Version;
        public string FeatureId; public UnityEditor.BuildTargetGroup[] BuildTargetGroups;
    }
}
#endif
