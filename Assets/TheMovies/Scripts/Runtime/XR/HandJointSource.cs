using System.Collections.Generic;
using UnityEngine;
#if THEMOVIES_XRHANDS
using Unity.Collections;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Meshing;
#endif

namespace TheMovies.XR
{
    /// <summary>Hands from finger joints: an aim ray, the palm pose and how far thumb and index finger are pinched.</summary>
    public interface IHandJointSource
    {
        /// <param name="head">Head pose in tracking space (the ray direction depends on it).</param>
        /// <param name="pinch">0 = fingers apart, 1 = touching.</param>
        bool TryGetHand(bool left, Pose head, out Pose aim, out Pose palm, out float pinch);

        /// <summary>All joint poses of a hand in tracking space, indexed like XRHandJointID (0 = invalid); false if untracked.</summary>
        bool TryGetJoints(bool left, Pose[] joints);

        /// <summary>
        /// The runtime's mesh of this hand (XR_FB_hand_tracking_mesh) with bone weights and bind poses for the 26 joints
        /// (bone i = XRHandJointID i + 1), or null with the reason in <paramref name="status"/>.
        /// </summary>
        Mesh TryCreateMesh(bool left, out string status);

        string Status { get; }
    }

    public static class HandJointSources
    {
        /// <summary>The joint source when the XR Hands package is in the build, otherwise null.</summary>
        public static IHandJointSource Create()
        {
#if THEMOVIES_XRHANDS
            return new XRHandsJointSource();
#else
            return null;
#endif
        }

        /// <summary>
        /// Ray through the base of the index finger from a pivot below and behind the head (around the chest), so
        /// a hand resting low points straight ahead and raising it a little points up – pointing at the menu needs no
        /// lifted arms. The pinch comes from the distance between thumb tip and index tip (2 cm = 1, 5 cm = 0).
        /// </summary>
        public static void Compute(bool left, Pose head, Vector3 indexBase, Vector3 thumbBase, Vector3 thumbTip, Vector3 indexTip,
            Pose palm, out Pose aim, out float pinch)
        {
            var forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up);
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var pivot = head.position + Vector3.down * 0.55f - forward * 0.25f + right * (left ? -0.12f : 0.12f);
            var origin = Vector3.Lerp(indexBase, thumbBase, 0.5f);
            var direction = origin - pivot;
            if (direction.sqrMagnitude < 1e-4f) direction = palm.rotation * Vector3.forward;
            aim = new Pose(origin, Quaternion.LookRotation(direction.normalized, Vector3.up));
            float distance = Vector3.Distance(thumbTip, indexTip);
            pinch = Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.02f, distance));
        }
    }

#if THEMOVIES_XRHANDS
    /// <summary>Reads the XRHandSubsystem of the XR Hands package (OpenXR feature "Hand Tracking Subsystem").</summary>
    sealed class XRHandsJointSource : IHandJointSource
    {
        readonly List<XRHandSubsystem> m_Subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem m_Subsystem;
        float m_NextSearch;

        public string Status => m_Subsystem == null ? "kein Hand-Subsystem (Feature „Hand Tracking Subsystem“ an?)"
            : m_Subsystem.running ? "Hand-Subsystem läuft" : "Hand-Subsystem gestoppt";

        XRHandSubsystem Subsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running) return m_Subsystem;
            if (Time.unscaledTime < m_NextSearch) return null;
            m_NextSearch = Time.unscaledTime + 1f;
            m_Subsystems.Clear();
            SubsystemManager.GetSubsystems(m_Subsystems);
            m_Subsystem = null;
            foreach (var s in m_Subsystems)
                if (s.running) m_Subsystem = s;
            return m_Subsystem;
        }

        public bool TryGetJoints(bool left, Pose[] joints)
        {
            var subsystem = Subsystem();
            if (subsystem == null) return false;
            var hand = left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked) return false;
            for (int i = 1; i < joints.Length; i++)
                if (!hand.GetJoint((XRHandJointID)i).TryGetPose(out joints[i]))
                    joints[i] = joints[i - 1];
            return true;
        }

        public Mesh TryCreateMesh(bool left, out string status)
        {
            var subsystem = Subsystem();
            if (subsystem == null)
            {
                status = "kein Hand-Subsystem";
                return null;
            }
            var query = new XRHandMeshDataQueryParams { allocator = Allocator.Temp };
            if (!subsystem.TryGetMeshData(out var result, ref query))
            {
                status = "die Laufzeit liefert kein Hand-Mesh (OpenXR-Feature „Meta Quest: Hand Mesh Data“ an?)";
                return null;
            }
            try
            {
                var data = left ? result.leftHand : result.rightHand;
                if (!data.positions.IsCreated || !data.indices.IsCreated || data.positions.Length == 0 || data.indices.Length == 0)
                {
                    status = "Hand-Mesh leer";
                    return null;
                }
                if (!data.boneWeights.IsCreated || !data.bonesPerVertex.IsCreated || !data.jointBindPoseMatricesRaw.IsCreated)
                {
                    status = "Hand-Mesh ohne Knochengewichte";
                    return null;
                }

                int joints = data.jointBindPoseMatricesRaw.Length;
                var mesh = new Mesh { name = (left ? "Left" : "Right") + " hand (runtime mesh)" };
                mesh.SetVertices(data.positions);
                mesh.SetIndices(data.indices, MeshTopology.Triangles, 0);
                if (data.normals.IsCreated && data.normals.Length == data.positions.Length)
                    mesh.SetNormals(data.normals);
                else
                    mesh.RecalculateNormals();

                // Unity wants each vertex's weights sorted, heaviest first, and no empty ones.
                var perVertex = data.bonesPerVertex;
                var weights = data.boneWeights;
                var counts = new NativeArray<byte>(perVertex.Length, Allocator.Temp);
                var sorted = new NativeArray<BoneWeight1>(weights.Length, Allocator.Temp);
                var scratch = new List<BoneWeight1>(8);
                int read = 0, written = 0;
                for (int v = 0; v < perVertex.Length; v++)
                {
                    scratch.Clear();
                    for (int k = 0; k < perVertex[v] && read < weights.Length; k++, read++)
                    {
                        var w = weights[read];
                        if (w.weight > 0f && w.boneIndex >= 0 && w.boneIndex < joints) scratch.Add(w);
                    }
                    if (scratch.Count == 0) scratch.Add(new BoneWeight1 { boneIndex = 0, weight = 1f }); // wrist
                    scratch.Sort((a, b) => b.weight.CompareTo(a.weight));
                    counts[v] = (byte)scratch.Count;
                    foreach (var w in scratch) sorted[written++] = w;
                }
                mesh.SetBoneWeights(counts, sorted.GetSubArray(0, written));

                // The runtime gives each joint's pose in the mesh; Unity wants the inverse (mesh → joint space).
                var bindposes = new Matrix4x4[joints];
                for (int i = 0; i < joints; i++)
                    bindposes[i] = data.TryGetJointBindPoseMatrix(out var pose, (XRHandJointID)(i + 1)) ? pose.inverse : Matrix4x4.identity;
                mesh.bindposes = bindposes;
                mesh.RecalculateBounds();
                status = $"{data.positions.Length} Punkte, {data.indices.Length / 3} Dreiecke";
                return mesh;
            }
            finally
            {
                result.Dispose();
            }
        }

        public bool TryGetHand(bool left, Pose head, out Pose aim, out Pose palm, out float pinch)
        {
            aim = palm = default;
            pinch = 0f;
            var subsystem = Subsystem();
            if (subsystem == null) return false;
            var hand = left ? subsystem.leftHand : subsystem.rightHand;
            if (!hand.isTracked) return false;
            if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out palm)) return false;
            if (!hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var indexBase) ||
                !hand.GetJoint(XRHandJointID.ThumbProximal).TryGetPose(out var thumbBase) ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbTip) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexTip))
                return false;
            HandJointSources.Compute(left, head, indexBase.position, thumbBase.position, thumbTip.position, indexTip.position, palm, out aim, out pinch);
            return true;
        }
    }
#endif
}
