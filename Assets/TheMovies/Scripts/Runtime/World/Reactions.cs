using System.Collections.Generic;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    public enum ReactionType : byte
    {
        Laugh = 0,
        Wow = 1,
        Heart = 2,
        Applause = 3,
        Popcorn = 4,
    }

    /// <summary>Visuals for reactions: emojis floating up above a participant, and popcorn thrown towards the screen.</summary>
    public static class Reactions
    {
        public static readonly ReactionType[] All =
        {
            ReactionType.Laugh, ReactionType.Wow, ReactionType.Heart, ReactionType.Applause, ReactionType.Popcorn,
        };

        static readonly Color k_Yellow = new Color(1f, 0.82f, 0.22f);
        static readonly Color k_Face = new Color(0.25f, 0.13f, 0.05f);

        /// <summary>Builds the emoji out of icon layers. Returns the renderers so they can be faded.</summary>
        public static List<MeshRenderer> BuildEmoji(Transform parent, ReactionType type, float size, float layerStep = 0.001f)
        {
            var layers = new List<(UIIcon icon, Color color)>();
            switch (type)
            {
                case ReactionType.Laugh:
                    layers.Add((UIIcon.Disc, k_Yellow));
                    layers.Add((UIIcon.HappyEyes, k_Face));
                    layers.Add((UIIcon.Smile, k_Face));
                    break;
                case ReactionType.Wow:
                    layers.Add((UIIcon.Disc, k_Yellow));
                    layers.Add((UIIcon.Eyes, k_Face));
                    layers.Add((UIIcon.MouthO, k_Face));
                    break;
                case ReactionType.Heart:
                    layers.Add((UIIcon.Heart, new Color(0.95f, 0.2f, 0.32f)));
                    break;
                case ReactionType.Applause:
                    layers.Add((UIIcon.Star, new Color(1f, 0.78f, 0.2f)));
                    break;
                case ReactionType.Popcorn:
                    layers.Add((UIIcon.Kernels, new Color(1f, 0.95f, 0.75f)));
                    layers.Add((UIIcon.Cup, new Color(0.85f, 0.12f, 0.15f)));
                    break;
            }

            var renderers = new List<MeshRenderer>();
            for (int i = 0; i < layers.Count; i++)
                renderers.Add(UIIcons.Create(parent, layers[i].icon, new Vector3(0, 0, -layerStep * (i + 1)), size, layers[i].color));
            return renderers;
        }

        /// <summary>Plays a reaction of a participant. <paramref name="head"/> and <paramref name="hand"/> are world poses.</summary>
        /// <param name="self">The local user's own reaction: show the emoji in front of them instead of above their head.</param>
        public static void Play(ReactionType type, Pose head, Pose hand, bool handTracked, bool self)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up).normalized;
            Vector3 emoji = self ? head.position + forward * 0.7f + Vector3.down * 0.12f : head.position + Vector3.up * 0.32f;
            FloatingReaction.Spawn(type, emoji);
            if (type == ReactionType.Popcorn)
            {
                Vector3 origin = handTracked ? hand.position : head.position + head.rotation * new Vector3(0.15f, -0.25f, 0.3f);
                Vector3 direction = handTracked ? hand.rotation * Vector3.forward : head.rotation * Vector3.forward;
                PopcornBurst.Spawn(origin, direction);
            }
        }
    }

    /// <summary>An emoji that rises, wobbles and fades out, always facing the viewer.</summary>
    public sealed class FloatingReaction : MonoBehaviour
    {
        const float k_Lifetime = 2.4f;

        List<MeshRenderer> m_Renderers;
        List<Color> m_BaseColors;
        Vector3 m_Start;
        float m_Age;
        float m_Seed;

        public static void Spawn(ReactionType type, Vector3 position)
        {
            var go = new GameObject("Reaction " + type);
            go.transform.position = position;
            var r = go.AddComponent<FloatingReaction>();
            r.m_Start = position;
            r.m_Seed = Random.value * 10f;
            r.m_Renderers = Reactions.BuildEmoji(go.transform, type, 0.16f);
            r.m_BaseColors = new List<Color>();
            var block = new MaterialPropertyBlock();
            foreach (var mr in r.m_Renderers)
            {
                mr.GetPropertyBlock(block);
                r.m_BaseColors.Add(block.GetColor("_Color"));
            }
            r.Update();
        }

        void Update()
        {
            m_Age += Time.deltaTime;
            float t = m_Age / k_Lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            float pop = t < 0.12f ? Mathf.SmoothStep(0.3f, 1.15f, t / 0.12f) : Mathf.Lerp(1.15f, 1f, Mathf.Clamp01((t - 0.12f) / 0.1f));
            transform.localScale = Vector3.one * pop;
            transform.position = m_Start + new Vector3(Mathf.Sin(m_Seed + m_Age * 4f) * 0.04f, t * 0.55f, 0);

            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = transform.position - cam.transform.position;
                if (away.sqrMagnitude > 1e-4f)
                    transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }

            float alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            for (int i = 0; i < m_Renderers.Count; i++)
            {
                var c = m_BaseColors[i];
                c.a = alpha;
                UIFactory.SetColor(m_Renderers[i], c);
            }
        }
    }

    /// <summary>A handful of popcorn flying in an arc (simple ballistic motion, no physics engine).</summary>
    public sealed class PopcornBurst : MonoBehaviour
    {
        const int k_Kernels = 14;
        const float k_Lifetime = 3.5f;
        static readonly Vector3 k_Gravity = new Vector3(0, -6f, 0);

        readonly List<Transform> m_Kernels = new List<Transform>();
        readonly List<Vector3> m_Velocities = new List<Vector3>();
        readonly List<Vector3> m_Spin = new List<Vector3>();
        float m_Age;

        public static void Spawn(Vector3 origin, Vector3 direction)
        {
            var go = new GameObject("Popcorn");
            var burst = go.AddComponent<PopcornBurst>();
            direction = (direction.normalized + Vector3.up * 0.6f).normalized;
            for (int i = 0; i < k_Kernels; i++)
            {
                var kernel = Shapes.Create(PrimitiveType.Sphere, go.transform, "Kernel", origin,
                    new Vector3(0.028f, 0.022f, 0.025f) * Random.Range(0.8f, 1.25f),
                    Color.Lerp(new Color(1f, 0.97f, 0.85f), new Color(1f, 0.85f, 0.45f), Random.value * 0.4f), Shapes.Flat).transform;
                burst.m_Kernels.Add(kernel);
                burst.m_Velocities.Add((direction + Random.insideUnitSphere * 0.35f).normalized * Random.Range(2.2f, 3.6f));
                burst.m_Spin.Add(Random.insideUnitSphere * 720f);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            m_Age += dt;
            if (m_Age > k_Lifetime)
            {
                Destroy(gameObject);
                return;
            }
            for (int i = 0; i < m_Kernels.Count; i++)
            {
                m_Velocities[i] += k_Gravity * dt;
                m_Kernels[i].position += m_Velocities[i] * dt;
                m_Kernels[i].Rotate(m_Spin[i] * dt, Space.Self);
                // Settle on the stepped floor (roughly), so kernels don't fall through the room
                if (m_Kernels[i].position.y < -0.88f)
                {
                    var p = m_Kernels[i].position;
                    p.y = -0.88f;
                    m_Kernels[i].position = p;
                    m_Velocities[i] = Vector3.zero;
                    m_Spin[i] = Vector3.zero;
                }
            }
        }
    }
}
