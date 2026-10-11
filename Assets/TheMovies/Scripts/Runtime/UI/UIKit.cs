using System;
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
        public static readonly Color Panel = new Color(0.06f, 0.06f, 0.08f, 0.94f);
        public static readonly Color PanelInset = new Color(0.11f, 0.11f, 0.14f, 0.95f);
        public static readonly Color Button = new Color(0.19f, 0.20f, 0.25f, 1f);
        public static readonly Color ButtonHover = new Color(0.29f, 0.31f, 0.39f, 1f);
        public static readonly Color ButtonPressed = new Color(0.13f, 0.13f, 0.16f, 1f);
        public static readonly Color ButtonDisabled = new Color(0.12f, 0.12f, 0.14f, 1f);
        public static readonly Color Accent = new Color(0.95f, 0.62f, 0.18f, 1f);
        public static readonly Color AccentHover = new Color(1f, 0.72f, 0.32f, 1f);
        public static readonly Color Danger = new Color(0.62f, 0.18f, 0.18f, 1f);
        public static readonly Color Text = new Color(0.95f, 0.95f, 0.97f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.64f, 0.70f, 1f);
        public static readonly Color TextDark = new Color(0.08f, 0.08f, 0.10f, 1f);
        public static readonly Color Ok = new Color(0.45f, 0.90f, 0.50f, 1f);
        public static readonly Color Warning = new Color(1f, 0.70f, 0.30f, 1f);
        public static readonly Color Error = new Color(1f, 0.42f, 0.38f, 1f);

        /// <summary>Distance (m) between stacked layers, so they never z-fight.</summary>
        public const float Layer = 0.002f;
    }

    /// <summary>Creates the meshes, materials and text objects the 3D menu is built from.</summary>
    public static class UIFactory
    {
        // TextMesh: one line is roughly fontSize * characterSize * 0.1 world units tall.
        const int k_FontSize = 64;
        const float k_TextMeshUnitScale = 0.1f;

        static Font s_Font;
        static Mesh s_Quad;
        static Material s_TextMaterial;
        static Material s_PanelMaterial;
        static Material s_WidgetMaterial;
        static Material s_OverlayMaterial;
        static MaterialPropertyBlock s_Block;

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

        public static Mesh QuadMesh
        {
            get
            {
                if (s_Quad == null)
                {
                    // 1x1 quad centered on the origin, visible from -Z (the side facing the user).
                    s_Quad = new Mesh { name = "TheMovies Quad" };
                    s_Quad.vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                        new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0),
                    };
                    s_Quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                    s_Quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                    s_Quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    s_Quad.RecalculateBounds();
                }
                return s_Quad;
            }
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
            {
                cache = new Material(FindShader("TheMovies/UIPanel")) { renderQueue = renderQueue };
            }
            return cache;
        }

        /// <summary>Panel backgrounds, drawn first.</summary>
        public static Material PanelMaterial => UIMaterial(ref s_PanelMaterial, 2980);

        /// <summary>Buttons and slider tracks, drawn on top of panels.</summary>
        public static Material WidgetMaterial => UIMaterial(ref s_WidgetMaterial, 2990);

        /// <summary>Slider fills and knobs, drawn on top of widgets.</summary>
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

        public static MeshRenderer CreateQuad(Transform parent, string name, Vector3 localPosition, Vector2 size,
            Color color, Material material)
        {
            var go = CreateEmpty(parent, name, localPosition);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SetColor(mr, color);
            return mr;
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
            set => m_Text.color = value;
        }

        public bool Visible
        {
            get => m_Text.gameObject.activeSelf;
            set => m_Text.gameObject.SetActive(value);
        }

        string Ellipsize(string value)
        {
            // Multi-line text: shorten each line on its own.
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
        public event Action Clicked;

        MeshRenderer m_Background;
        TextMesh m_Label;
        Vector2 m_Size;
        bool m_Hover;
        bool m_Pressed;
        bool m_Interactable = true;
        bool m_Highlighted;
        Color m_BaseColor = UIStyle.Button;

        public static UIButton Create(Transform parent, string label, Vector3 localPosition, Vector2 size,
            Action onClick = null, float textHeight = 0.024f)
        {
            var go = UIFactory.CreateEmpty(parent, "Button " + label, localPosition);
            var button = go.AddComponent<UIButton>();
            button.m_Size = size;
            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.01f);
            button.m_Background = UIFactory.CreateQuad(go.transform, "Background", Vector3.zero, size,
                UIStyle.Button, UIFactory.WidgetMaterial);
            button.m_Label = UIFactory.CreateText(go.transform, label, new Vector3(0, 0, -UIStyle.Layer), textHeight);
            if (onClick != null)
                button.Clicked += onClick;
            button.Refresh();
            return button;
        }

        public string Label
        {
            get => m_Label.text;
            set
            {
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
                Refresh();
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
                Refresh();
            }
        }

        public Color BaseColor
        {
            get => m_BaseColor;
            set
            {
                m_BaseColor = value;
                Refresh();
            }
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
            m_Pressed = true;
            Refresh();
        }

        public void OnPointerDrag(Ray ray) { }

        public void OnPointerUp(bool releasedInside)
        {
            bool click = m_Pressed && releasedInside && m_Interactable;
            m_Pressed = false;
            Refresh();
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
            Refresh();
        }

        void Refresh()
        {
            if (m_Background == null) return;
            Color c;
            if (!m_Interactable) c = UIStyle.ButtonDisabled;
            else if (m_Pressed) c = UIStyle.ButtonPressed;
            else if (m_Highlighted) c = m_Hover ? UIStyle.AccentHover : UIStyle.Accent;
            else if (m_Hover) c = UIStyle.ButtonHover;
            else c = m_BaseColor;
            UIFactory.SetColor(m_Background, c);

            if (m_Label != null)
            {
                if (!m_Interactable) m_Label.color = UIStyle.TextDim * 0.8f;
                else if (m_Highlighted && !m_Pressed) m_Label.color = UIStyle.TextDark;
                else m_Label.color = UIStyle.Text;
            }
        }
    }

    /// <summary>Horizontal slider, used as the seek bar.</summary>
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
            col.size = new Vector3(width + 0.02f, Mathf.Max(height * 3f, 0.04f), 0.01f);
            slider.m_Track = UIFactory.CreateQuad(go.transform, "Track", Vector3.zero, new Vector2(width, height),
                UIStyle.Button, UIFactory.WidgetMaterial);
            slider.m_Fill = UIFactory.CreateQuad(go.transform, "Fill", new Vector3(0, 0, -UIStyle.Layer),
                new Vector2(width, height), UIStyle.Accent, UIFactory.OverlayMaterial);
            float knob = height * 2.2f;
            slider.m_Knob = UIFactory.CreateQuad(go.transform, "Knob", new Vector3(0, 0, -2 * UIStyle.Layer),
                new Vector2(knob * 0.45f, knob), UIStyle.Text, UIFactory.OverlayMaterial);
            slider.SetValueWithoutNotify(0);
            return slider;
        }

        public bool Interactable
        {
            get => m_Interactable;
            set
            {
                m_Interactable = value;
                if (!value) IsDragging = false;
                Refresh();
            }
        }

        public void SetValueWithoutNotify(float value)
        {
            m_Value = Mathf.Clamp01(float.IsNaN(value) ? 0 : value);
            float fillWidth = m_Width * m_Value;
            m_Fill.transform.localScale = new Vector3(Mathf.Max(fillWidth, 0.0001f), m_Height, 1);
            m_Fill.transform.localPosition = new Vector3(-m_Width * 0.5f + fillWidth * 0.5f, 0, -UIStyle.Layer);
            m_Knob.transform.localPosition = new Vector3(-m_Width * 0.5f + fillWidth, 0, -2 * UIStyle.Layer);
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
            SetValueWithoutNotify(local.x / m_Width + 0.5f);
            Refresh();
            Dragged?.Invoke(m_Value);
        }

        void Refresh()
        {
            UIFactory.SetColor(m_Track, m_Hover || IsDragging ? UIStyle.ButtonHover : UIStyle.Button);
            UIFactory.SetColor(m_Fill, m_Interactable ? UIStyle.Accent : UIStyle.TextDim);
            m_Knob.gameObject.SetActive(m_Interactable);
        }
    }
}
