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
