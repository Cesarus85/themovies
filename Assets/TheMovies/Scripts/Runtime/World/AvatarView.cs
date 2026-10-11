using TheMovies.Net;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>Head, body hint, hands and a name tag for a remote participant, smoothly following the network pose.</summary>
    public sealed class AvatarView : MonoBehaviour
    {
        const float k_Smoothing = 18f;

        Transform m_Head;
        Transform m_Body;
        Transform m_Left;
        Transform m_Right;
        Transform m_NameTag;
        TextMesh m_Name;
        MeshRenderer m_HeadRenderer;
        MeshRenderer m_Visor;
        MeshRenderer m_BodyRenderer;
        MeshRenderer[] m_HandRenderers;
        Transform m_SpeakingRing;
        Color m_Color = Color.gray;
        bool m_HasPose;
        float m_SpeakLevel;

        public static AvatarView Create(Transform parent)
        {
            var go = new GameObject("Avatar");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<AvatarView>();
            view.Build();
            return view;
        }

        void Build()
        {
            m_Head = new GameObject("Head").transform;
            m_Head.SetParent(transform, false);
            m_HeadRenderer = Shapes.Create(PrimitiveType.Sphere, m_Head, "Skull", Vector3.zero, new Vector3(0.19f, 0.23f, 0.21f), m_Color);
            m_Visor = Shapes.Create(PrimitiveType.Cube, m_Head, "Visor", new Vector3(0, 0.02f, 0.085f), new Vector3(0.17f, 0.07f, 0.06f), new Color(0.05f, 0.05f, 0.06f));
            m_SpeakingRing = Shapes.Create(PrimitiveType.Cylinder, m_Head, "Mouth", new Vector3(0, -0.06f, 0.095f), new Vector3(0.05f, 0.004f, 0.02f), Color.white, Shapes.Flat).transform;
            m_SpeakingRing.localRotation = Quaternion.Euler(90, 0, 0);

            m_Body = new GameObject("Body").transform;
            m_Body.SetParent(transform, false);
            m_BodyRenderer = Shapes.Create(PrimitiveType.Capsule, m_Body, "Torso", new Vector3(0, -0.42f, -0.05f), new Vector3(0.34f, 0.3f, 0.22f), m_Color * 0.6f);

            m_Left = CreateHand("Left");
            m_Right = CreateHand("Right");
            m_HandRenderers = new[]
            {
                m_Left.GetComponentInChildren<MeshRenderer>(),
                m_Right.GetComponentInChildren<MeshRenderer>(),
            };

            m_NameTag = new GameObject("Name Tag").transform;
            m_NameTag.SetParent(transform, false);
            m_Name = UIFactory.CreateText(m_NameTag, string.Empty, Vector3.zero, 0.05f);

            SetVisible(false);
        }

        Transform CreateHand(string name)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(transform, false);
            Shapes.Create(PrimitiveType.Cube, hand, "Controller", new Vector3(0, 0, -0.01f), new Vector3(0.035f, 0.045f, 0.1f), m_Color);
            return hand;
        }

        /// <summary>Shows a reaction coming from this avatar.</summary>
        public void PlayReaction(ReactionType type)
        {
            if (!m_HasPose) return;
            var head = new Pose(m_Head.position, m_Head.rotation);
            var hand = new Pose(m_Right.position, m_Right.rotation);
            Reactions.Play(type, head, hand, m_Right.gameObject.activeSelf, self: false);
        }

        public void SetIdentity(string displayName, Color color)
        {
            m_Color = color;
            m_Name.text = displayName;
            m_Name.color = Color.Lerp(color, Color.white, 0.4f);
            Shapes.SetColor(m_HeadRenderer, color);
            Shapes.SetColor(m_BodyRenderer, color * 0.55f);
            foreach (var r in m_HandRenderers)
                Shapes.SetColor(r, color * 0.8f);
        }

        public void Apply(in AvatarPose pose, bool speaking, bool muted)
        {
            bool valid = pose.HeadRotation.x * pose.HeadRotation.x + pose.HeadRotation.y * pose.HeadRotation.y +
                         pose.HeadRotation.z * pose.HeadRotation.z + pose.HeadRotation.w * pose.HeadRotation.w > 0.5f;
            if (!valid)
            {
                SetVisible(false);
                return;
            }

            float t = m_HasPose ? 1f - Mathf.Exp(-k_Smoothing * Time.deltaTime) : 1f;
            Follow(m_Head, pose.HeadPosition, pose.HeadRotation, t);

            // The body hangs below the head and only turns around the vertical axis.
            Vector3 forward = Vector3.ProjectOnPlane(m_Head.forward, Vector3.up);
            Quaternion bodyRot = forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;
            m_Body.SetPositionAndRotation(m_Head.position, bodyRot);

            bool left = (pose.Flags & AvatarPose.LeftTracked) != 0;
            bool right = (pose.Flags & AvatarPose.RightTracked) != 0;
            m_Left.gameObject.SetActive(left);
            m_Right.gameObject.SetActive(right);
            if (left) Follow(m_Left, pose.LeftPosition, pose.LeftRotation, t);
            if (right) Follow(m_Right, pose.RightPosition, pose.RightRotation, t);

            // Name tag floats above the head and faces the viewer.
            m_NameTag.position = m_Head.position + Vector3.up * 0.24f;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toTag = m_NameTag.position - cam.transform.position;
                if (toTag.sqrMagnitude > 1e-4f)
                    m_NameTag.rotation = Quaternion.LookRotation(toTag, Vector3.up);
            }

            // Simple mouth animation while speaking.
            m_SpeakLevel = Mathf.MoveTowards(m_SpeakLevel, speaking && !muted ? 1f : 0f, Time.deltaTime * 8f);
            float open = m_SpeakLevel * (0.5f + 0.5f * Mathf.Sin(Time.time * 22f));
            m_SpeakingRing.localScale = new Vector3(0.05f, 0.004f, 0.012f + 0.03f * open);

            m_HasPose = true;
            SetVisible(true);
        }

        static void Follow(Transform t, Vector3 position, Quaternion rotation, float amount)
        {
            t.SetPositionAndRotation(Vector3.Lerp(t.position, position, amount), Quaternion.Slerp(t.rotation, rotation, amount));
        }

        void SetVisible(bool visible)
        {
            if (m_Head.gameObject.activeSelf == visible) return;
            m_Head.gameObject.SetActive(visible);
            m_Body.gameObject.SetActive(visible);
            m_NameTag.gameObject.SetActive(visible);
            if (!visible)
            {
                m_Left.gameObject.SetActive(false);
                m_Right.gameObject.SetActive(false);
                m_HasPose = false;
            }
        }
    }
}
