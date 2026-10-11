using TheMovies.Core;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// The cinema screen: shows the video with the right per-eye cropping for 3D, keeps the aspect ratio,
    /// scales to the chosen size and lets the room pick up its average color ("screen glow").
    /// </summary>
    public sealed class ScreenController : MonoBehaviour
    {
        public static readonly float[] Widths = { 7f, 10f, 14f, 19f };
        public static readonly string[] SizeNames = { "Klein", "Mittel", "Groß", "Riesig" };
        const float k_MaxHeight = 12f;
        const float k_BottomGap = 0.9f;

        static readonly int k_LeftEyeST = Shader.PropertyToID("_LeftEyeST");
        static readonly int k_RightEyeST = Shader.PropertyToID("_RightEyeST");
        static readonly int k_HasVideo = Shader.PropertyToID("_HasVideo");
        static readonly int k_Brightness = Shader.PropertyToID("_Brightness");
        static readonly int k_GlowTex = Shader.PropertyToID("_TM_GlowTex");
        static readonly int k_ScreenCenter = Shader.PropertyToID("_TM_ScreenCenter");
        static readonly int k_RoomLight = Shader.PropertyToID("_TM_RoomLight");
        static readonly int k_GlowEnabled = Shader.PropertyToID("_TM_GlowEnabled");

        Transform m_ScreenTransform;
        Transform m_Frame;
        Material m_Material;
        Texture m_Texture;
        RenderTexture m_GlowTexture;
        StereoFormat m_Format;
        bool m_SwapEyes;
        int m_VideoWidth = 1920;
        int m_VideoHeight = 1080;
        int m_SizeIndex = 2;
        float m_RoomLight = 1f;
        float m_RoomLightTarget = 1f;

        public int SizeIndex => m_SizeIndex;
        public Vector3 Center => m_ScreenTransform.position;
        public Vector2 Size { get; private set; }

        public static ScreenController Create(Transform parent, float z)
        {
            var go = new GameObject("Screen");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, z);
            var screen = go.AddComponent<ScreenController>();
            screen.Build();
            return screen;
        }

        void Build()
        {
            m_Frame = Shapes.Create(PrimitiveType.Cube, transform, "Masking", Vector3.zero, Vector3.one, new Color(0.01f, 0.01f, 0.012f), Shapes.Flat).transform;

            m_Material = new Material(UIFactory.FindShader("TheMovies/StereoVideo"));
            var quad = UIFactory.CreateEmpty(transform, "Picture", Vector3.zero);
            quad.AddComponent<MeshFilter>().sharedMesh = UIFactory.QuadMesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_Material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_ScreenTransform = quad.transform;

            m_GlowTexture = new RenderTexture(32, 16, 0, RenderTextureFormat.ARGB32)
            {
                name = "TheMovies Screen Glow",
                useMipMap = true,
                autoGenerateMips = true,
                wrapMode = TextureWrapMode.Clamp,
            };
            m_GlowTexture.Create();
            Shader.SetGlobalTexture(k_GlowTex, m_GlowTexture);
            Shader.SetGlobalFloat(k_RoomLight, m_RoomLight);
            Shader.SetGlobalFloat(k_GlowEnabled, 0f);

            ApplyFormat();
            Layout();
        }

        void OnDestroy()
        {
            if (m_GlowTexture != null) m_GlowTexture.Release();
            if (m_Material != null) Destroy(m_Material);
        }

        public void SetTexture(Texture texture)
        {
            m_Texture = texture;
            m_Material.mainTexture = texture;
            m_Material.SetFloat(k_HasVideo, texture != null ? 1f : 0f);
            Shader.SetGlobalFloat(k_GlowEnabled, texture != null ? 1f : 0f);
        }

        public void SetVideoSize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            m_VideoWidth = width;
            m_VideoHeight = height;
            Layout();
        }

        public void SetFormat(StereoFormat format, bool swapEyes)
        {
            m_Format = format;
            m_SwapEyes = swapEyes;
            ApplyFormat();
            Layout();
        }

        public void SetSize(int index)
        {
            m_SizeIndex = Mathf.Clamp(index, 0, Widths.Length - 1);
            Layout();
        }

        public void SetBrightness(float brightness) => m_Material.SetFloat(k_Brightness, brightness);

        /// <summary>1 = house lights on, 0 = dark cinema. Fades smoothly.</summary>
        public void SetRoomLight(float target, bool immediate = false)
        {
            m_RoomLightTarget = Mathf.Clamp01(target);
            if (immediate)
            {
                m_RoomLight = m_RoomLightTarget;
                Shader.SetGlobalFloat(k_RoomLight, m_RoomLight);
            }
        }

        void ApplyFormat()
        {
            var l = m_Format.EyeUv(0, m_SwapEyes);
            var r = m_Format.EyeUv(1, m_SwapEyes);
            m_Material.SetVector(k_LeftEyeST, new Vector4(l.ScaleX, l.ScaleY, l.OffsetX, l.OffsetY));
            m_Material.SetVector(k_RightEyeST, new Vector4(r.ScaleX, r.ScaleY, r.OffsetX, r.OffsetY));
        }

        void Layout()
        {
            float aspect = (float)m_Format.DisplayAspect(m_VideoWidth, m_VideoHeight);
            float width = Widths[m_SizeIndex];
            float height = width / aspect;
            if (height > k_MaxHeight)
            {
                height = k_MaxHeight;
                width = height * aspect;
            }

            Size = new Vector2(width, height);
            m_ScreenTransform.localScale = new Vector3(width, height, 1f);
            m_ScreenTransform.localPosition = new Vector3(0, k_BottomGap + height * 0.5f, 0);
            m_Frame.localScale = new Vector3(width + 0.5f, height + 0.5f, 0.05f);
            m_Frame.localPosition = new Vector3(0, k_BottomGap + height * 0.5f, 0.05f);

            Vector3 c = m_ScreenTransform.position;
            Shader.SetGlobalVector(k_ScreenCenter, new Vector4(c.x, c.y, c.z, 14f + width * 0.6f));
        }

        void LateUpdate()
        {
            if (!Mathf.Approximately(m_RoomLight, m_RoomLightTarget))
            {
                m_RoomLight = Mathf.MoveTowards(m_RoomLight, m_RoomLightTarget, Time.deltaTime * 0.6f);
                Shader.SetGlobalFloat(k_RoomLight, m_RoomLight);
            }

            if (m_Texture != null)
                Graphics.Blit(m_Texture, m_GlowTexture);
        }
    }
}
