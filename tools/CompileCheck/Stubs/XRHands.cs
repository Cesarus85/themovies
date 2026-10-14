// API stubs for the XR Hands package (com.unity.xr.hands 1.x), only what the hand joint source uses.
namespace UnityEngine.XR.Hands
{
    public enum XRHandJointID { Invalid = 0, BeginMarker = 1, Wrist = 1, Palm, ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip, EndMarker }

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
        public bool TryGetMeshData(out Meshing.XRHandMeshDataQueryResult result, ref Meshing.XRHandMeshDataQueryParams queryParams)
        {
            result = default;
            return false;
        }
        public void Start() { }
        public void Stop() { }
        public void Destroy() { }
    }
}

namespace UnityEngine.XR.Hands.Meshing
{
    using Unity.Collections;

    public struct XRHandMeshDataQueryParams { public Allocator allocator { get; set; } }

    public struct XRHandMeshDataQueryResult : System.IDisposable
    {
        public XRHandMeshData leftHand => default;
        public XRHandMeshData rightHand => default;
        public void Dispose() { }
    }

    public struct XRHandMeshData
    {
        public NativeArray<int> indices => default;
        public NativeArray<Vector3> positions => default;
        public NativeArray<Vector3> normals => default;
        public NativeArray<byte> bonesPerVertex => default;
        public NativeArray<BoneWeight1> boneWeights => default;
        public NativeArray<Matrix4x4> jointBindPoseMatricesRaw => default;
        public bool TryGetJointBindPoseMatrix(out Matrix4x4 bindPose, XRHandJointID jointID) { bindPose = default; return false; }
    }
}
