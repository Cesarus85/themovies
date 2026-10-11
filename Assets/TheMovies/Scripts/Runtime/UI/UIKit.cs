using System;
using System.Collections.Generic;
using TheMovies.Core;
using UnityEngine;

namespace TheMovies.UI
{
    /// <summary>Anything the laser pointer (or the mouse) can interact with.</summary>
    public interface IPointerTarget
    {
        void OnPointerEnter();
        void OnPointerExit();
        void OnPointerDown(Ray ray, Vector3 hitPoint);
        void OnPointerDrag(Ray ray);

        /// <param name="releasedInside">True if the pointer was still on this target when released.</param>
        void OnPointerUp(bool releasedInside);
    }

    public static class UIStyle
    {
        public static readonly Color PanelTop = new Color(0.115f, 0.12f, 0.16f, 0.97f);
        public static readonly Color PanelBottom = new Color(0.05f, 0.05f, 0.075f, 0.97f);
        public static readonly Color PanelRim = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color PanelInset = new Color(0.03f, 0.03f, 0.045f, 0.85f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color Button = new Color(0.20f, 0.21f, 0.27f, 1f);
        public static readonly Color ButtonHover = new Color(0.30f, 0.32f, 0.41f, 1f);
        public static readonly Color ButtonPressed = new Color(0.14f, 0.14f, 0.18f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.13f, 0.13f, 0.16f, 0.8f);
        public static readonly Color Subtle = new Color(1f, 1f, 1f, 0.0f);
        public static readonly Color SubtleHover = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color Accent = new Color(0.98f, 0.66f, 0.22f, 1f);
        public static readonly Color AccentHover = new Color(1f, 0.76f, 0.38f, 1f);
        public static readonly Color Danger = new Color(0.66f, 0.20f, 0.22f, 1f);
        public static readonly Color DangerHover = new Color(0.80f, 0.27f, 0.29f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.96f, 0.98f, 1f);
        public static readonly Color TextDim = new Color(0.64f, 0.66f, 0.73f, 1f);
        public static readonly Color TextDark = new Color(0.10f, 0.08f, 0.06f, 1f);
        public static readonly Color Ok = new Color(0.45f, 0.90f, 0.55f, 1f);
        public static readonly Color Warning = new Color(1f, 0.72f, 0.32f, 1f);
        public static readonly Color Error = new Color(1f, 0.45f, 0.40f, 1f);
        public static readonly Color Handle = new Color(0.85f, 0.87f, 0.95f, 0.45f);
        public static readonly Color HandleHover = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>Distance (m) between stacked layers, so they never z-fight.</summary>
        public const float Layer = 0.002f;
    }

    public enum ButtonStyle
    {
        Normal,

        /// <summary>Accent colored main action (play, OK).</summary>
        Primary,

        Danger,

        /// <summary>No background until hovered (icon buttons in headers).</summary>
        Subtle,
    }

    public enum UIIcon
    {
        None,
        Play,
        Pause,
        Back,
        Forward,
        Plus,
        Minus,
        ChevronLeft,
        ChevronRight,
        Close,
        Settings,
        Film,

        // Building blocks for reactions
        Disc,
        Heart,
        Star,
        Smile,
        MouthO,
        Eyes,
        HappyEyes,
        Cup,
        Kernels,
        Subtitles,
    }

    /// <summary>Creates the meshes, materials and text objects the 3D menu is built from.</summary>
    public static class UIFactory
    {
        // TextMesh: one line is roughly fontSize * characterSize * 0.1 world units tall.
        const int k_FontSize = 96;
        const float k_TextMeshUnitScale = 0.1f;

        static Font s_Font;
        static Mesh s_Quad;
        static Material s_TextMaterial;
        static Material s_PanelMaterial;
        static Material s_WidgetMaterial;
        static Material s_OverlayMaterial;
        static MaterialPropertyBlock s_Block;
        static readonly Dictionary<long, Mesh> s_RoundedCache = new Dictionary<long, Mesh>();

        static readonly int k_ColorId = Shader.PropertyToID("_Color");

        public static Font Font
        {
            get
            {
                if (s_Font == null)
                {
                    s_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (s_Font == null)
                        s_Font = Font.CreateDynamicFontFromOSFont("Arial", k_FontSize);
                    Font.textureRebuilt += OnFontTextureRebuilt;
                }
                return s_Font;
            }
        }

        static void OnFontTextureRebuilt(Font font)
        {
            if (font == s_Font && s_TextMaterial != null)
                s_TextMaterial.mainTexture = font.material.mainTexture;
        }

        /// <summary>1x1 quad centered on the origin, visible from -Z (the side facing the user), white vertex colors.</summary>
        public static Mesh QuadMesh
        {
            get
            {
                if (s_Quad == null)
                {
                    s_Quad = new Mesh { name = "TheMovies Quad" };
                    s_Quad.vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                        new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0),
                    };
                    s_Quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                    s_Quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                    s_Quad.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                    s_Quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    s_Quad.RecalculateBounds();
                }
                return s_Quad;
            }
        }

        /// <summary>
        /// Rounded rectangle centered on the origin with a vertical gradient baked into the vertex colors
        /// (white at the top, darker at the bottom by <paramref name="gradient"/>). Cached per size.
        /// </summary>
        public static Mesh RoundedRect(Vector2 size, float radius, float gradient = 0.15f)
        {
            long key = (long)Mathf.RoundToInt(size.x * 1000) * 1_000_000_000L + Mathf.RoundToInt(size.y * 1000) * 100_000L +
                       Mathf.RoundToInt(radius * 1000) * 100L + Mathf.RoundToInt(gradient * 99);
            if (s_RoundedCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            radius = Mathf.Clamp(radius, 0f, Mathf.Min(size.x, size.y) * 0.5f);
            const int cornerSegments = 8;
            var outline = new List<Vector3>();
            var corners = new[]
            {
                new Vector2(size.x * 0.5f - radius, size.y * 0.5f - radius),    // top right
                new Vector2(-size.x * 0.5f + radius, size.y * 0.5f - radius),   // top left
                new Vector2(-size.x * 0.5f + radius, -size.y * 0.5f + radius),  // bottom left
                new Vector2(size.x * 0.5f - radius, -size.y * 0.5f + radius),   // bottom right
            };
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i <= cornerSegments; i++)
                {
                    float a = (c * 90f + 90f * i / cornerSegments) * Mathf.Deg2Rad;
                    outline.Add(new Vector3(corners[c].x + Mathf.Cos(a) * radius, corners[c].y + Mathf.Sin(a) * radius, 0));
                }
            }

            var vertices = new List<Vector3> { Vector3.zero };
            vertices.AddRange(outline);
            var colors = new Color32[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
            {
                float t = size.y > 0 ? vertices[i].y / size.y + 0.5f : 1f; // 0 bottom .. 1 top
                byte v = (byte)Mathf.RoundToInt(255f * (1f - gradient * (1f - t)));
                colors[i] = new Color32(v, v, v, 255);
            }
            var triangles = new List<int>();
            for (int i = 1; i <= outline.Count; i++)
            {
                int next = i == outline.Count ? 1 : i + 1;
                // Menu shaders use Cull Off, so the winding only needs to be consistent.
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(i);
            }

            var mesh = new Mesh { name = "TheMovies Rounded Rect" };
            mesh.SetVertices(vertices);
            mesh.colors32 = colors;
            var normals = new Vector3[vertices.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.back;
            mesh.normals = normals;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            s_RoundedCache[key] = mesh;
            return mesh;
        }

        public static Shader FindShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
            {
                Debug.LogError($"[TheMovies] Shader '{name}' not found. Is Assets/TheMovies/Resources/Shaders imported?");
                shader = Shader.Find("Unlit/Color") ?? Shader.Find("Hidden/InternalErrorShader");
            }
            return shader;
        }

        static Material UIMaterial(ref Material cache, int renderQueue)
        {
            if (cache == null)
                cache = new Material(FindShader("TheMovies/UIRounded")) { renderQueue = renderQueue };
            return cache;
        }

        /// <summary>Panel backgrounds, drawn first.</summary>
        public static Material PanelMaterial => UIMaterial(ref s_PanelMaterial, 2980);

        /// <summary>Buttons and slider tracks, drawn on top of panels.</summary>
        public static Material WidgetMaterial => UIMaterial(ref s_WidgetMaterial, 2990);

        /// <summary>Icons, slider fills and knobs, drawn on top of widgets.</summary>
        public static Material OverlayMaterial => UIMaterial(ref s_OverlayMaterial, 2995);

        public static Material TextMaterial
        {
            get
            {
                if (s_TextMaterial == null)
                {
                    s_TextMaterial = new Material(FindShader("TheMovies/Text"))
                    {
                        mainTexture = Font.material.mainTexture,
                        renderQueue = 3000,
                    };
                }
                return s_TextMaterial;
            }
        }

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            if (s_Block == null) s_Block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(s_Block);
            s_Block.SetColor(k_ColorId, color);
            renderer.SetPropertyBlock(s_Block);
        }

        public static GameObject CreateEmpty(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        public static MeshRenderer CreateMesh(Transform parent, string name, Vector3 localPosition, Mesh mesh, Color color, Material material)
        {
            var go = CreateEmpty(parent, name, localPosition);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetColor(mr, color);
            return mr;
        }

        public static MeshRenderer CreateQuad(Transform parent, string name, Vector3 localPosition, Vector2 size,
            Color color, Material material)
        {
            var mr = CreateMesh(parent, name, localPosition, QuadMesh, color, material);
            mr.transform.localScale = new Vector3(size.x, size.y, 1f);
            return mr;
        }

        public static MeshRenderer CreateRounded(Transform parent, string name, Vector3 localPosition, Vector2 size, float radius,
            Color color, Material material, float gradient = 0.15f)
        {
            return CreateMesh(parent, name, localPosition, RoundedRect(size, radius, gradient), color, material);
        }

        public static TextMesh CreateText(Transform parent, string text, Vector3 localPosition, float height,
            TextAnchor anchor = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = CreateEmpty(parent, "Text", localPosition);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = k_FontSize;
            tm.characterSize = height / (k_FontSize * k_TextMeshUnitScale);
            tm.anchor = anchor;
            tm.alignment = AlignmentFor(anchor);
            tm.richText = false;
            tm.color = color ?? UIStyle.Text;
            tm.text = text ?? string.Empty;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = TextMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return tm;
        }

        /// <summary>
        /// Width of each character at the given text height, from the font's real glyph advances (for fitting text into
        /// tiles). Loads the glyphs of <paramref name="text"/> into the font texture first.
        /// </summary>
        public static Func<char, float> CharWidth(string text, float height)
        {
            var font = Font;
            float fallback = height * 0.55f;
            if (font == null) return c => fallback;
            font.RequestCharactersInTexture((text ?? string.Empty) + "…", k_FontSize, FontStyle.Normal);
            return c => font.GetCharacterInfo(c, out CharacterInfo info, k_FontSize, FontStyle.Normal) && info.advance > 0
                ? info.advance * height / k_FontSize
                : fallback;
        }

        /// <summary>Rough width of a text line for layout (the built-in font averages about half the line height per character).</summary>
        public static float EstimateTextWidth(string text, float height) => (text?.Length ?? 0) * height * 0.5f;

        static TextAlignment AlignmentFor(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                case TextAnchor.MiddleLeft:
                case TextAnchor.LowerLeft:
                    return TextAlignment.Left;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return TextAlignment.Right;
                default:
                    return TextAlignment.Center;
            }
        }
    }

    /// <summary>Simple vector icons built from triangles (no image assets needed).</summary>
    public static class UIIcons
    {
        static readonly Dictionary<UIIcon, Mesh> s_Cache = new Dictionary<UIIcon, Mesh>();

        public static MeshRenderer Create(Transform parent, UIIcon icon, Vector3 localPosition, float size, Color color)
        {
            var mr = UIFactory.CreateMesh(parent, "Icon " + icon, localPosition, MeshFor(icon), color, UIFactory.OverlayMaterial);
            mr.transform.localScale = new Vector3(size, size, 1f);
            return mr;
        }

        public static Mesh MeshFor(UIIcon icon)
        {
            if (s_Cache.TryGetValue(icon, out var mesh) && mesh != null)
                return mesh;
            var b = new IconBuilder();
            switch (icon)
            {
                case UIIcon.Play:
                    b.Tri(new Vector2(-0.28f, -0.4f), new Vector2(-0.28f, 0.4f), new Vector2(0.42f, 0f));
                    break;
                case UIIcon.Pause:
                    b.Rect(-0.3f, -0.38f, -0.07f, 0.38f);
                    b.Rect(0.07f, -0.38f, 0.3f, 0.38f);
                    break;
                case UIIcon.Back:
                    b.Tri(new Vector2(0.02f, -0.34f), new Vector2(0.02f, 0.34f), new Vector2(-0.42f, 0f));
                    b.Tri(new Vector2(0.44f, -0.34f), new Vector2(0.44f, 0.34f), new Vector2(0f, 0f));
                    break;
                case UIIcon.Forward:
                    b.Tri(new Vector2(-0.02f, -0.34f), new Vector2(-0.02f, 0.34f), new Vector2(0.42f, 0f));
                    b.Tri(new Vector2(-0.44f, -0.34f), new Vector2(-0.44f, 0.34f), new Vector2(0f, 0f));
                    break;
                case UIIcon.Plus:
                    b.Rect(-0.38f, -0.08f, 0.38f, 0.08f);
                    b.Rect(-0.08f, -0.38f, 0.08f, 0.38f);
                    break;
                case UIIcon.Minus:
                    b.Rect(-0.38f, -0.08f, 0.38f, 0.08f);
                    break;
                case UIIcon.ChevronRight:
                    b.Quad(new Vector2(-0.18f, 0.4f), new Vector2(-0.02f, 0.4f), new Vector2(0.28f, 0f), new Vector2(0.12f, 0f));
                    b.Quad(new Vector2(0.12f, 0f), new Vector2(0.28f, 0f), new Vector2(-0.02f, -0.4f), new Vector2(-0.18f, -0.4f));
                    break;
                case UIIcon.ChevronLeft:
                    b.Quad(new Vector2(0.18f, 0.4f), new Vector2(0.02f, 0.4f), new Vector2(-0.28f, 0f), new Vector2(-0.12f, 0f));
                    b.Quad(new Vector2(-0.12f, 0f), new Vector2(-0.28f, 0f), new Vector2(0.02f, -0.4f), new Vector2(0.18f, -0.4f));
                    break;
                case UIIcon.Close:
                    b.RotatedRect(0.86f, 0.15f, 45f);
                    b.RotatedRect(0.86f, 0.15f, -45f);
                    break;
                case UIIcon.Settings:
                    for (int i = 0; i < 3; i++)
                    {
                        float y = 0.3f - i * 0.3f;
                        float knob = i == 1 ? 0.15f : -0.12f;
                        b.Rect(-0.4f, y - 0.04f, 0.4f, y + 0.04f);
                        b.Rect(knob - 0.08f, y - 0.11f, knob + 0.08f, y + 0.11f);
                    }
                    break;
                case UIIcon.Film:
                    b.Rect(-0.42f, -0.32f, 0.42f, -0.22f);
                    b.Rect(-0.42f, 0.22f, 0.42f, 0.32f);
                    b.Rect(-0.42f, -0.22f, -0.3f, 0.22f);
                    b.Rect(0.3f, -0.22f, 0.42f, 0.22f);
                    b.Tri(new Vector2(-0.1f, -0.14f), new Vector2(-0.1f, 0.14f), new Vector2(0.14f, 0f));
                    break;
                case UIIcon.Disc:
                    b.Disc(Vector2.zero, 0.5f, 32);
                    break;
                case UIIcon.Heart:
                    b.Disc(new Vector2(-0.2f, 0.12f), 0.24f, 20);
                    b.Disc(new Vector2(0.2f, 0.12f), 0.24f, 20);
                    b.Quad(new Vector2(-0.43f, 0.05f), new Vector2(0.43f, 0.05f), new Vector2(0.0f, -0.45f), new Vector2(0.0f, -0.45f));
                    b.Tri(new Vector2(-0.2f, 0.25f), new Vector2(0.2f, 0.25f), new Vector2(0f, -0.1f));
                    break;
                case UIIcon.Star:
                    for (int i = 0; i < 5; i++)
                    {
                        float a0 = Mathf.Deg2Rad * (90f + i * 72f);
                        float a1 = a0 + Mathf.Deg2Rad * 36f;
                        float a2 = a0 - Mathf.Deg2Rad * 36f;
                        var tip = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.5f;
                        var left = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.2f;
                        var right = new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * 0.2f;
                        b.Tri(tip, left, Vector2.zero);
                        b.Tri(tip, Vector2.zero, right);
                    }
                    break;
                case UIIcon.Smile:
                    // Open laughing mouth: lower half disc
                    b.Arc(new Vector2(0, -0.02f), 0f, 0.3f, 180f, 360f, 16);
                    break;
                case UIIcon.MouthO:
                    b.Disc(new Vector2(0, -0.17f), 0.13f, 16);
                    break;
                case UIIcon.Eyes:
                    b.Disc(new Vector2(-0.16f, 0.13f), 0.07f, 12);
                    b.Disc(new Vector2(0.16f, 0.13f), 0.07f, 12);
                    break;
                case UIIcon.HappyEyes:
                    b.Arc(new Vector2(-0.16f, 0.1f), 0.05f, 0.11f, 0f, 180f, 10);
                    b.Arc(new Vector2(0.16f, 0.1f), 0.05f, 0.11f, 0f, 180f, 10);
                    break;
                case UIIcon.Cup:
                    b.Quad(new Vector2(-0.3f, 0.12f), new Vector2(0.3f, 0.12f), new Vector2(0.22f, -0.48f), new Vector2(-0.22f, -0.48f));
                    break;
                case UIIcon.Kernels:
                    b.Disc(new Vector2(-0.18f, 0.2f), 0.14f, 12);
                    b.Disc(new Vector2(0.02f, 0.27f), 0.16f, 12);
                    b.Disc(new Vector2(0.2f, 0.19f), 0.13f, 12);
                    b.Disc(new Vector2(-0.06f, 0.4f), 0.11f, 12);
                    break;
                case UIIcon.Subtitles:
                    b.Rect(-0.42f, -0.3f, 0.42f, -0.2f);
                    b.Rect(-0.3f, -0.08f, 0.3f, 0.02f);
                    break;
            }
            mesh = b.ToMesh(icon.ToString());
            s_Cache[icon] = mesh;
            return mesh;
        }

        sealed class IconBuilder
        {
            readonly List<Vector3> m_Vertices = new List<Vector3>();
            readonly List<int> m_Triangles = new List<int>();

            // Menu shaders use Cull Off, so the winding of icon triangles does not matter.
            public void Tri(Vector2 a, Vector2 b, Vector2 c)
            {
                int i = m_Vertices.Count;
                m_Vertices.Add(a);
                m_Vertices.Add(b);
                m_Vertices.Add(c);
                m_Triangles.Add(i);
                m_Triangles.Add(i + 1);
                m_Triangles.Add(i + 2);
            }

            public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }

            public void Rect(float x0, float y0, float x1, float y1) =>
                Quad(new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0));

            public void Disc(Vector2 center, float radius, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = Mathf.PI * 2f * i / segments, a1 = Mathf.PI * 2f * (i + 1) / segments;
                    Tri(center, center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius,
                        center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius);
                }
            }

            /// <summary>Ring segment between two radii (inner 0 = pie slice), angles in degrees.</summary>
            public void Arc(Vector2 center, float innerRadius, float outerRadius, float fromDegrees, float toDegrees, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, (float)i / segments);
                    float a1 = Mathf.Deg2Rad * Mathf.Lerp(fromDegrees, toDegrees, (float)(i + 1) / segments);
                    var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                    var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                    if (innerRadius <= 0f)
                        Tri(center, center + d0 * outerRadius, center + d1 * outerRadius);
                    else
                        Quad(center + d0 * innerRadius, center + d0 * outerRadius, center + d1 * outerRadius, center + d1 * innerRadius);
                }
            }

            public void RotatedRect(float length, float thickness, float degrees)
            {
                var q = Quaternion.Euler(0, 0, degrees);
                Vector2 P(float x, float y) => q * new Vector3(x, y, 0);
                float l = length * 0.5f, t = thickness * 0.5f;
                Quad(P(-l, -t), P(-l, t), P(l, t), P(l, -t));
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = "Icon " + name };
                mesh.SetVertices(m_Vertices);
                var colors = new Color32[m_Vertices.Count];
                for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
                mesh.colors32 = colors;
                mesh.SetTriangles(m_Triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }

    /// <summary>Single line of text that shortens itself to a maximum length.</summary>
    public sealed class UILabel
    {
        readonly TextMesh m_Text;
        readonly int m_MaxChars;
        string m_Raw = string.Empty;

        public UILabel(Transform parent, string text, Vector3 localPosition, float height, int maxChars,
            TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            m_MaxChars = maxChars;
            m_Text = UIFactory.CreateText(parent, string.Empty, localPosition, height, anchor, color);
            Text = text;
        }

        public string Text
        {
            get => m_Raw;
            set
            {
                value = value ?? string.Empty;
                if (value == m_Raw) return;
                m_Raw = value;
                m_Text.text = m_MaxChars > 0 ? Ellipsize(value) : value;
            }
        }

        public Color Color
        {
            get => m_Text.color;
            set
            {
                if (m_Text.color != value)
                    m_Text.color = value;
            }
        }

        public bool Visible
        {
            get => m_Text.gameObject.activeSelf;
            set => m_Text.gameObject.SetActive(value);
        }

        string Ellipsize(string value)
        {
            if (value.IndexOf('\n') < 0)
                return TextUtil.Ellipsize(value, m_MaxChars);
            var lines = value.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = TextUtil.Ellipsize(lines[i], m_MaxChars);
            return string.Join("\n", lines);
        }
    }

    public sealed class UIButton : MonoBehaviour, IPointerTarget
    {
        const float k_Animation = 14f;

        public event Action Clicked;

        MeshRenderer m_Background;
        TextMesh m_Label;
        MeshRenderer m_Icon;
        Vector2 m_Size;
        float m_TextHeight;
        ButtonStyle m_Style;
        bool m_Hover;
        bool m_Pressed;
        bool m_Interactable = true;
        bool m_Highlighted;
        Color m_CurrentColor;
        float m_CurrentScale = 1f;
        bool m_Animating;

        public static UIButton Create(Transform parent, string label, Vector3 localPosition, Vector2 size,
            Action onClick = null, float textHeight = 0.024f, UIIcon icon = UIIcon.None, ButtonStyle style = ButtonStyle.Normal,
            bool round = false)
        {
            var go = UIFactory.CreateEmpty(parent, "Button " + (label ?? icon.ToString()), localPosition);
            var button = go.AddComponent<UIButton>();
            button.m_Size = size;
            button.m_TextHeight = textHeight;
            button.m_Style = style;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.01f);

            float radius = round ? Mathf.Min(size.x, size.y) * 0.5f : Mathf.Min(size.x, size.y) * 0.3f;
            button.m_Background = UIFactory.CreateRounded(go.transform, "Background", Vector3.zero, size, radius,
                UIStyle.Button, UIFactory.WidgetMaterial, 0.22f);

            float iconSize = Mathf.Min(size.y * 0.45f, textHeight * 1.25f);
            bool hasLabel = !string.IsNullOrEmpty(label);
            if (icon != UIIcon.None)
            {
                if (!hasLabel) iconSize = Mathf.Min(size.x, size.y) * 0.42f;
                float labelWidth = hasLabel ? UIFactory.EstimateTextWidth(label, textHeight) : 0f;
                float total = iconSize + (hasLabel ? iconSize * 0.45f + labelWidth : 0f);
                float iconX = -total * 0.5f + iconSize * 0.5f;
                button.m_Icon = UIIcons.Create(go.transform, icon, new Vector3(iconX, 0, -UIStyle.Layer), iconSize, UIStyle.Text);
                if (hasLabel)
                    button.m_Label = UIFactory.CreateText(go.transform, label,
                        new Vector3(iconX + iconSize * 0.5f + iconSize * 0.45f, 0, -UIStyle.Layer), textHeight, TextAnchor.MiddleLeft);
            }
            else
            {
                button.m_Label = UIFactory.CreateText(go.transform, label, new Vector3(0, 0, -UIStyle.Layer), textHeight);
            }

            if (onClick != null)
                button.Clicked += onClick;
            button.m_CurrentColor = button.TargetColor();
            button.ApplyVisuals();
            return button;
        }

        public string Label
        {
            get => m_Label != null ? m_Label.text : string.Empty;
            set
            {
                if (m_Label == null) return;
                value = value ?? string.Empty;
                if (m_Label.text != value)
                    m_Label.text = value; // regenerating the text mesh is not free
            }
        }

        public Vector2 Size => m_Size;

        public bool Interactable
        {
            get => m_Interactable;
            set
            {
                if (m_Interactable == value) return;
                m_Interactable = value;
                if (!value) m_Pressed = false;
                StartAnimation();
            }
        }

        /// <summary>Accent color, e.g. for the active choice of a toggle group.</summary>
        public bool Highlighted
        {
            get => m_Highlighted;
            set
            {
                if (m_Highlighted == value) return;
                m_Highlighted = value;
                StartAnimation();
            }
        }

        public ButtonStyle Style
        {
            get => m_Style;
            set
            {
                if (m_Style == value) return;
                m_Style = value;
                StartAnimation();
            }
        }

        /// <summary>Swaps the icon (e.g. play/pause).</summary>
        public void SetIcon(UIIcon icon)
        {
            if (m_Icon == null) return;
            var mesh = UIIcons.MeshFor(icon);
            var filter = m_Icon.GetComponent<MeshFilter>();
            if (filter.sharedMesh != mesh)
                filter.sharedMesh = mesh;
        }

        public void OnPointerEnter()
        {
            m_Hover = true;
            StartAnimation();
        }

        public void OnPointerExit()
        {
            m_Hover = false;
            StartAnimation();
        }

        public void OnPointerDown(Ray ray, Vector3 hitPoint)
        {
            if (!m_Interactable) return;
            m_Pressed = true;
            StartAnimation();
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp(bool releasedInside)
        {
            bool click = m_Pressed && releasedInside && m_Interactable;
            m_Pressed = false;
            StartAnimation();
            if (click)
            {
                try
                {
                    Clicked?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        void OnDisable()
        {
            m_Hover = false;
            m_Pressed = false;
            m_CurrentColor = TargetColor();
            m_CurrentScale = 1f;
            ApplyVisuals();
        }

        void StartAnimation()
        {
            m_Animating = true;
            enabled = true;
        }

        void Update()
        {
            if (!m_Animating) return;
            float t = 1f - Mathf.Exp(-k_Animation * Time.unscaledDeltaTime);
            Color target = TargetColor();
            float targetScale = !m_Interactable ? 1f : m_Pressed ? 0.96f : m_Hover ? 1.04f : 1f;
            m_CurrentColor = Color.Lerp(m_CurrentColor, target, t);
            m_CurrentScale = Mathf.Lerp(m_CurrentScale, targetScale, t);
            if (Mathf.Abs(m_CurrentScale - targetScale) < 0.001f && ColorClose(m_CurrentColor, target))
            {
                m_CurrentColor = target;
                m_CurrentScale = targetScale;
                m_Animating = false;
            }
            ApplyVisuals();
        }

        Color TargetColor()
        {
            if (!m_Interactable) return m_Style == ButtonStyle.Subtle ? UIStyle.Subtle : UIStyle.ButtonDisabled;
            if (m_Pressed) return m_Style == ButtonStyle.Subtle ? UIStyle.SubtleHover : UIStyle.ButtonPressed;
            bool accent = m_Highlighted || m_Style == ButtonStyle.Primary;
            if (accent) return m_Hover ? UIStyle.AccentHover : UIStyle.Accent;
            switch (m_Style)
            {
                case ButtonStyle.Danger: return m_Hover ? UIStyle.DangerHover : UIStyle.Danger;
                case ButtonStyle.Subtle: return m_Hover ? UIStyle.SubtleHover : UIStyle.Subtle;
                default: return m_Hover ? UIStyle.ButtonHover : UIStyle.Button;
            }
        }

        void ApplyVisuals()
        {
            if (m_Background == null) return;
            UIFactory.SetColor(m_Background, m_CurrentColor);
            transform.localScale = new Vector3(m_CurrentScale, m_CurrentScale, 1f);

            bool accent = (m_Highlighted || m_Style == ButtonStyle.Primary) && m_Interactable && !m_Pressed;
            Color content = !m_Interactable ? UIStyle.TextDim * 0.75f : accent ? UIStyle.TextDark : UIStyle.Text;
            if (m_Label != null && m_Label.color != content) m_Label.color = content;
            if (m_Icon != null) UIFactory.SetColor(m_Icon, content);
        }

        static bool ColorClose(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.004f;
    }

    /// <summary>Horizontal slider, used as the seek bar and for the volume.</summary>
    public sealed class UISlider : MonoBehaviour, IPointerTarget
    {
        /// <summary>Fired while dragging.</summary>
        public event Action<float> Dragged;

        /// <summary>Fired once when the pointer is released.</summary>
        public event Action<float> Committed;

        MeshRenderer m_Track;
        MeshRenderer m_Fill;
        MeshRenderer m_Knob;
        float m_Width;
        float m_Height;
        float m_Value;
        bool m_Hover;
        bool m_Interactable = true;

        public bool IsDragging { get; private set; }

        public float Value => m_Value;

        public static UISlider Create(Transform parent, Vector3 localPosition, float width, float height)
        {
            var go = UIFactory.CreateEmpty(parent, "Slider", localPosition);
            var slider = go.AddComponent<UISlider>();
            slider.m_Width = width;
            slider.m_Height = height;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(width + 0.03f, Mathf.Max(height * 4f, 0.05f), 0.01f);
            slider.m_Track = UIFactory.CreateRounded(go.transform, "Track", Vector3.zero, new Vector2(width, height), height * 0.5f,
                UIStyle.Button, UIFactory.WidgetMaterial, 0f);
            slider.m_Fill = UIFactory.CreateQuad(go.transform, "Fill", new Vector3(0, 0, -UIStyle.Layer),
                new Vector2(width, height), UIStyle.Accent, UIFactory.OverlayMaterial);
            float knob = height * 2.4f;
            slider.m_Knob = UIFactory.CreateRounded(go.transform, "Knob", new Vector3(0, 0, -2 * UIStyle.Layer),
                new Vector2(knob, knob), knob * 0.5f, UIStyle.Text, UIFactory.OverlayMaterial, 0.2f);
            slider.SetValueWithoutNotify(0);
            slider.Refresh();
            return slider;
        }

        public bool Interactable
        {
            get => m_Interactable;
            set
            {
                if (m_Interactable == value) return;
                m_Interactable = value;
                if (!value) IsDragging = false;
                Refresh();
            }
        }

        public void SetValueWithoutNotify(float value)
        {
            m_Value = Mathf.Clamp01(float.IsNaN(value) ? 0 : value);
            // The fill is a plain rectangle inside the rounded track, inset by the track's corner radius.
            float inner = Mathf.Max(0f, m_Width - m_Height);
            float fillWidth = inner * m_Value;
            m_Fill.transform.localScale = new Vector3(Mathf.Max(fillWidth, 0.0001f), m_Height, 1);
            m_Fill.transform.localPosition = new Vector3(-inner * 0.5f + fillWidth * 0.5f, 0, -UIStyle.Layer);
            m_Knob.transform.localPosition = new Vector3(-inner * 0.5f + fillWidth, 0, -2 * UIStyle.Layer);
        }

        public void OnPointerEnter()
        {
            m_Hover = true;
            Refresh();
        }

        public void OnPointerExit()
        {
            m_Hover = false;
            Refresh();
        }

        public void OnPointerDown(Ray ray, Vector3 hitPoint)
        {
            if (!m_Interactable) return;
            IsDragging = true;
            UpdateFromRay(ray);
        }

        public void OnPointerDrag(Ray ray)
        {
            if (IsDragging)
                UpdateFromRay(ray);
        }

        public void OnPointerUp(bool releasedInside)
        {
            if (!IsDragging) return;
            IsDragging = false;
            Refresh();
            Committed?.Invoke(m_Value);
        }

        void OnDisable()
        {
            IsDragging = false;
            m_Hover = false;
        }

        void UpdateFromRay(Ray ray)
        {
            var plane = new Plane(transform.forward, transform.position);
            if (!plane.Raycast(ray, out float distance))
                return;
            Vector3 local = transform.InverseTransformPoint(ray.GetPoint(distance));
            float inner = Mathf.Max(0.001f, m_Width - m_Height);
            SetValueWithoutNotify(local.x / inner + 0.5f);
            Refresh();
            Dragged?.Invoke(m_Value);
        }

        void Refresh()
        {
            UIFactory.SetColor(m_Track, m_Hover || IsDragging ? UIStyle.ButtonHover : UIStyle.Button);
            UIFactory.SetColor(m_Fill, m_Interactable ? UIStyle.Accent : UIStyle.TextDim * 0.6f);
            float knob = m_Hover || IsDragging ? 1.25f : 1f;
            m_Knob.transform.localScale = new Vector3(knob, knob, 1f);
            m_Knob.gameObject.SetActive(m_Interactable);
        }
    }

    /// <summary>Bar below a panel that can be grabbed with the pointer to move the panel.</summary>
    public sealed class UIGrabHandle : MonoBehaviour, IPointerTarget
    {
        public event Action<Ray, Vector3> Began;
        public event Action<Ray> Moved;
        public event Action Ended;

        MeshRenderer m_Bar;
        bool m_Hover;
        bool m_Dragging;

        public bool IsDragging => m_Dragging;

        public static UIGrabHandle Create(Transform parent, Vector3 localPosition, float width)
        {
            var go = UIFactory.CreateEmpty(parent, "Grab Handle", localPosition);
            var handle = go.AddComponent<UIGrabHandle>();
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(width * 1.5f, 0.06f, 0.01f);
            handle.m_Bar = UIFactory.CreateRounded(go.transform, "Bar", Vector3.zero, new Vector2(width, 0.018f), 0.009f,
                UIStyle.Handle, UIFactory.WidgetMaterial, 0f);
            return handle;
        }

        public void OnPointerEnter()
        {
            m_Hover = true;
            Refresh();
        }

        public void OnPointerExit()
        {
            m_Hover = false;
            Refresh();
        }

        public void OnPointerDown(Ray ray, Vector3 hitPoint)
        {
            m_Dragging = true;
            Refresh();
            Began?.Invoke(ray, hitPoint);
        }

        public void OnPointerDrag(Ray ray)
        {
            if (m_Dragging) Moved?.Invoke(ray);
        }

        public void OnPointerUp(bool releasedInside)
        {
            if (!m_Dragging) return;
            m_Dragging = false;
            Refresh();
            Ended?.Invoke();
        }

        void OnDisable()
        {
            if (m_Dragging) Ended?.Invoke();
            m_Dragging = false;
            m_Hover = false;
        }

        void Refresh()
        {
            UIFactory.SetColor(m_Bar, m_Hover || m_Dragging ? UIStyle.HandleHover : UIStyle.Handle);
            float s = m_Dragging ? 1.15f : m_Hover ? 1.08f : 1f;
            m_Bar.transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
