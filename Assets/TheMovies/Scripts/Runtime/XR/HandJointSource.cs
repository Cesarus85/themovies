using System.Collections.Generic;
using UnityEngine;
#if THEMOVIES_XRHANDS
using UnityEngine.XR.Hands;
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
