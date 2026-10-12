using TheMovies.World;
using UnityEngine;

namespace TheMovies.XR
{
    /// <summary>
    /// The user's hand drawn from the tracked joints (hand tracking): rounded segments for the finger bones and
    /// metacarpals plus a palm, so it moves exactly like the real hand. Joint indices follow XRHandJointID
    /// (1 = wrist, 2 = palm, 3–6 thumb, 7–11 index, 12–16 middle, 17–21 ring, 22–26 little finger).
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        public const int JointCount = 27;

        static readonly Color k_Skin = new Color(0.86f, 0.70f, 0.60f);

        // (from joint, to joint, radius in m)
        static readonly (int a, int b, float r)[] k_Bones =
        {
            // wrist to the finger bases (fills the back of the hand)
            (1, 3, 0.016f), (1, 8, 0.016f), (1, 13, 0.016f), (1, 18, 0.015f), (1, 23, 0.014f),
            // across the knuckles
            (8, 13, 0.012f), (13, 18, 0.012f), (18, 23, 0.011f),
            // thumb
            (3, 4, 0.0135f), (4, 5, 0.0115f), (5, 6, 0.0098f),
            // index
            (8, 9, 0.0098f), (9, 10, 0.009f), (10, 11, 0.0082f),
            // middle
            (13, 14, 0.0102f), (14, 15, 0.0092f), (15, 16, 0.0084f),
            // ring
            (18, 19, 0.0095f), (19, 20, 0.0087f), (20, 21, 0.008f),
            // little
            (23, 24, 0.0085f), (24, 25, 0.0078f), (25, 26, 0.0072f),
        };

        Transform[] m_Segments;
        Transform m_Palm;
        bool m_Mask;
        Renderer[] m_Renderers;

        /// <summary>
        /// Mask mode: the hand is drawn a bit thicker as a hole into the picture, so the passthrough shows the real
        /// hand there (see PassthroughFeature.SetForHands).
        /// </summary>
        public void SetMask(bool mask, Material maskMaterial)
        {
            if (m_Mask == mask) return;
            m_Mask = mask;
            foreach (var r in m_Renderers) r.sharedMaterial = mask ? maskMaterial : Shapes.Lit;
        }

        public static HandView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<HandView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        void Build()
        {
            m_Segments = new Transform[k_Bones.Length];
            for (int i = 0; i < k_Bones.Length; i++)
                m_Segments[i] = Shapes.Create(PrimitiveType.Capsule, transform, "Bone " + i, Vector3.zero, Vector3.one * 0.01f, k_Skin).transform;
            m_Palm = Shapes.Create(PrimitiveType.Sphere, transform, "Palm", Vector3.zero, Vector3.one * 0.01f, k_Skin * 0.97f).transform;
            m_Renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <param name="joints">Poses in the parent's space, indexed like XRHandJointID.</param>
        public void Apply(Pose[] joints)
        {
            for (int i = 0; i < k_Bones.Length; i++)
            {
                var (a, b, r) = k_Bones[i];
                if (m_Mask) r = r * 1.35f + 0.003f; // a little margin: tracking and camera don't match to the millimetre
                Vector3 pa = joints[a].position, pb = joints[b].position;
                Vector3 d = pb - pa;
                float length = d.magnitude;
                var t = m_Segments[i];
                if (length < 1e-4f)
                {
                    t.localScale = Vector3.zero;
                    continue;
                }
                t.localPosition = (pa + pb) * 0.5f;
                t.localRotation = Quaternion.FromToRotation(Vector3.up, d / length);
                // Unity's capsule is 2 units tall and 1 wide: rounded ends reach a little past the joints.
                t.localScale = new Vector3(r * 2f, (length + r * 2f) * 0.5f, r * 2f);
            }
            // Palm: flat ellipsoid at the palm joint, between the wrist and the knuckles.
            var palm = joints[2];
            Vector3 across = joints[8].position - joints[23].position;
            Vector3 along = (joints[13].position - joints[1].position);
            m_Palm.localPosition = Vector3.Lerp(palm.position, (joints[1].position + joints[13].position) * 0.5f, 0.3f);
            m_Palm.localRotation = palm.rotation;
            float grow = m_Mask ? 1.15f : 1f;
            m_Palm.localScale = new Vector3(Mathf.Max(0.05f, across.magnitude * 1.25f) * grow, 0.026f * (m_Mask ? 1.6f : 1f),
                Mathf.Max(0.06f, along.magnitude * 0.85f) * grow);
        }
    }
}
