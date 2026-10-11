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
        /// <summary>Radius of the screen's curve (seats are about this far away, so the picture wraps around them a little).</summary>
        const float k_CurveRadius = 30f;
        const int k_CurveSegments = 32;
        float m_MaxHeight = 8f;
        Mesh m_PictureMesh;
        Mesh m_MaskingMesh;

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

        Vector3 m_CinemaPosition;
        Quaternion m_CinemaRotation;
        float m_SizeScale = 1f;

        public int SizeIndex => m_SizeIndex;
        public Vector3 Center => transform.TransformPoint(new Vector3(0, Size.y * 0.5f, 0));
        public Vector2 Size { get; private set; }

        /// <param name="localPosition">Bottom center of the screen.</param>
        public static ScreenController Create(Transform parent, Vector3 localPosition, Quaternion localRotation, float maxHeight)
        {
            var go = new GameObject("Screen");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            var screen = go.AddComponent<ScreenController>();
            screen.m_MaxHeight = maxHeight;
            screen.m_CinemaPosition = localPosition;
            screen.m_CinemaRotation = localRotation;
            screen.Build();
            return screen;
        }

        void Build()
        {
            m_MaskingMesh = new Mesh { name = "Screen Masking" };
            var masking = UIFactory.CreateEmpty(transform, "Masking", Vector3.zero);
            masking.AddComponent<MeshFilter>().sharedMesh = m_MaskingMesh;
            var maskingRenderer = masking.AddComponent<MeshRenderer>();
            maskingRenderer.sharedMaterial = Shapes.Flat;
            maskingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shapes.SetColor(maskingRenderer, new Color(0.006f, 0.006f, 0.008f));
            m_Frame = masking.transform;

            m_Material = new Material(UIFactory.FindShader("TheMovies/StereoVideo"));
            m_PictureMesh = new Mesh { name = "Screen Picture" };
            var picture = UIFactory.CreateEmpty(transform, "Picture", Vector3.zero);
            picture.AddComponent<MeshFilter>().sharedMesh = m_PictureMesh;
            var mr = picture.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m_Material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_ScreenTransform = picture.transform;

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
            if (m_PictureMesh != null) Destroy(m_PictureMesh);
            if (m_MaskingMesh != null) Destroy(m_MaskingMesh);
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

        /// <summary>Mixed reality: a TV-sized screen a few meters in front of the seats instead of the cinema screen.</summary>
        public void SetMixedRealityLayout(bool mixedReality)
        {
            transform.localPosition = mixedReality ? new Vector3(0f, 0.35f, 4.2f) : m_CinemaPosition;
            transform.localRotation = mixedReality ? Quaternion.identity : m_CinemaRotation;
            m_SizeScale = mixedReality ? 0.35f : 1f;
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
            float width = Widths[m_SizeIndex] * m_SizeScale;
            float height = width / aspect;
            if (height > m_MaxHeight)
            {
                height = m_MaxHeight;
                width = height * aspect;
            }

            Size = new Vector2(width, height);
            BuildCurved(m_PictureMesh, width, height, 0f, 0f);
            BuildCurved(m_MaskingMesh, width + 0.7f, height + 0.7f, -0.35f, 0.04f);

            Vector3 c = transform.TransformPoint(new Vector3(0, height * 0.5f, 0));
            Shader.SetGlobalVector(k_ScreenCenter, new Vector4(c.x, c.y, c.z, 14f + width * 0.6f));
        }

        /// <summary>A section of a cylinder around the audience; the edges come slightly forward.</summary>
        static void BuildCurved(Mesh mesh, float width, float height, float bottom, float back)
        {
            float angle = width / k_CurveRadius;
            var vertices = new Vector3[(k_CurveSegments + 1) * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[k_CurveSegments * 6];
            for (int i = 0; i <= k_CurveSegments; i++)
            {
                float f = (float)i / k_CurveSegments;
                float a = (f - 0.5f) * angle;
                float x = Mathf.Sin(a) * k_CurveRadius;
                float z = -(1f - Mathf.Cos(a)) * k_CurveRadius + back;
                var n = new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a));
                vertices[i * 2] = new Vector3(x, bottom, z);
                vertices[i * 2 + 1] = new Vector3(x, bottom + height, z);
                normals[i * 2] = normals[i * 2 + 1] = n;
                uvs[i * 2] = new Vector2(f, 0);
                uvs[i * 2 + 1] = new Vector2(f, 1);
            }
            for (int i = 0; i < k_CurveSegments; i++)
            {
                int a = i * 2, t = i * 6;
                triangles[t] = a;
                triangles[t + 1] = a + 1;
                triangles[t + 2] = a + 3;
                triangles[t + 3] = a;
                triangles[t + 4] = a + 3;
                triangles[t + 5] = a + 2;
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
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
