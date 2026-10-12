// API stubs for the XR Hands package (com.unity.xr.hands 1.x), only what the hand joint source uses.
namespace UnityEngine.XR.Hands
{
    public enum XRHandJointID { Invalid, BeginMarker, Wrist = 1, Palm, ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip }

    public struct XRHandJoint { public bool TryGetPose(out Pose pose) { pose = default; return false; } }

    public struct XRHand
    {
        public bool isTracked => false;
        public XRHandJoint GetJoint(XRHandJointID id) => default;
    }

    public class XRHandSubsystem : UnityEngine.ISubsystem
    {
        public XRHand leftHand => default;
        public XRHand rightHand => default;
        public bool running => false;
        public void Start() { }
        public void Stop() { }
        public void Destroy() { }
    }
}
