using System;
using TheMovies.Core;
using TheMovies.UI;
using TheMovies.XR;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// The cinema screen: shows the video with the right per-eye cropping for 3D, keeps the aspect ratio,
    /// scales to the chosen size and lets the room pick up its average color ("screen glow").
    /// 180°/360° videos are shown on a (half) sphere around the viewer instead; the hall is hidden meanwhile.
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
        const float k_SphereRadius = 40f;
        Transform m_Sphere;
        Mesh m_SphereMesh;
        int m_SphereDegrees;

        static readonly int k_LeftEyeST = Shader.PropertyToID("_LeftEyeST");
        static readonly int k_RightEyeST = Shader.PropertyToID("_RightEyeST");
        static readonly int k_HasVideo = Shader.PropertyToID("_HasVideo");
        static readonly int k_Brightness = Shader.PropertyToID("_Brightness");
        static readonly int k_GlowTex = Shader.PropertyToID("_TM_GlowTex");
        static readonly int k_ScreenCenter = Shader.PropertyToID("_TM_ScreenCenter");
        static readonly int k_RoomLight = Shader.PropertyToID("_TM_RoomLight");
        static readonly int k_GlowEnabled = Shader.PropertyToID("_TM_GlowEnabled");
        static readonly int k_DepthTex = Shader.PropertyToID("_DepthTex");
        static readonly int k_Convert3D = Shader.PropertyToID("_Convert3D");
        static readonly int k_Convergence = Shader.PropertyToID("_Convergence");
        static readonly int k_ShowDepth = Shader.PropertyToID("_ShowDepth");
        static readonly int k_GlowStrength = Shader.PropertyToID("_Strength");
        static readonly int k_GlowFalloff = Shader.PropertyToID("_Falloff");
        static readonly int k_GlowScreenSize = Shader.PropertyToID("_ScreenSize");
        static readonly int k_GlowQuadSize = Shader.PropertyToID("_QuadSize");
        static readonly int k_GlowEyeSt = Shader.PropertyToID("_EyeST");

        // 2D→3D (per viewer, not synced) and the light around the screen in mixed reality
        int m_Convert3D;
        DepthEstimator m_Depth;
        int m_GlowLevel = 1;
        bool m_MixedReality;
        MeshRenderer m_Glow;
        Material m_GlowMaterial;

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

        /// <summary>A 180°/360° video is shown around the viewer (the hall should be hidden).</summary>
        public bool IsImmersive => m_Format.IsImmersive();

        /// <summary>Fires when <see cref="IsImmersive"/> changes.</summary>
        public event Action<bool> ImmersiveChanged;

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

            // Sphere for 180°/360° videos: same material, so texture, 3D packing and brightness apply as well.
            m_SphereMesh = new Mesh { name = "Immersive Sphere" };
            var sphere = UIFactory.CreateEmpty(transform, "Immersive Sphere", Vector3.zero);
            sphere.AddComponent<MeshFilter>().sharedMesh = m_SphereMesh;
            var sphereRenderer = sphere.AddComponent<MeshRenderer>();
            sphereRenderer.sharedMaterial = m_Material;
            sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;
            m_Sphere = sphere.transform;
            sphere.SetActive(false);

            m_GlowMaterial = new Material(UIFactory.FindShader("TheMovies/ScreenGlow"));
            m_Glow = UIFactory.CreateQuad(transform, "Screen Glow", Vector3.zero, Vector2.one, Color.white, m_GlowMaterial);
            m_Glow.gameObject.SetActive(false);

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
            if (m_SphereMesh != null) Destroy(m_SphereMesh);
            if (m_GlowMaterial != null) Destroy(m_GlowMaterial);
            m_Depth?.Dispose();
        }

        public void SetTexture(Texture texture)
        {
            m_Texture = texture;
            m_Material.mainTexture = texture;
            m_Material.SetFloat(k_HasVideo, texture != null ? 1f : 0f);
            Shader.SetGlobalFloat(k_GlowEnabled, texture != null ? 1f : 0f);
            LayoutGlow();
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
            bool wasImmersive = IsImmersive;
            m_Format = format;
            m_SwapEyes = swapEyes;
            ApplyFormat();
            Layout();
            ApplyImmersion();
            if (wasImmersive != IsImmersive) ImmersiveChanged?.Invoke(IsImmersive);
            LayoutGlow();
        }

        void ApplyImmersion()
        {
            int degrees = m_Format.Immersion();
            bool immersive = degrees > 0;
            m_ScreenTransform.gameObject.SetActive(!immersive);
            m_Frame.gameObject.SetActive(!immersive);
            m_Sphere.gameObject.SetActive(immersive);
            if (immersive && degrees != m_SphereDegrees)
            {
                m_SphereDegrees = degrees;
                BuildSphere(m_SphereMesh, k_SphereRadius, degrees);
            }
            if (immersive) FollowViewer(aim: true);
        }

        /// <summary>
        /// The sphere stays centered on the viewer's head (moving the head must not move the picture), its
        /// middle towards the screen.
        /// </summary>
        void FollowViewer(bool aim = false)
        {
            var rig = XRRig.Instance;
            Transform head = rig != null && rig.Head != null ? rig.Head : Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            m_Sphere.position = head.position;
            // The middle of the picture where the screen is (the seats face it). Only aimed when the video starts:
            // turning it with every head movement would make the picture swim.
            if (!aim) return;
            Vector3 forward = Center - head.position;
            forward.y = 0;
            if (forward.sqrMagnitude > 0.01f) m_Sphere.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>
        /// Inside of a sphere (360°) or of its front half (180°) with equirectangular texture coordinates:
        /// U runs from left to right as seen from the center, the middle of the picture is straight ahead (+Z).
        /// </summary>
        public static void BuildSphere(Mesh mesh, float radius, int degrees, int columns = 96, int rows = 48)
        {
            float lonRange = degrees * Mathf.Deg2Rad;
            var vertices = new Vector3[(columns + 1) * (rows + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int r = 0; r <= rows; r++)
            {
                float v = (float)r / rows;
                float lat = (v - 0.5f) * Mathf.PI;
                for (int c = 0; c <= columns; c++)
                {
                    float u = (float)c / columns;
                    float lon = (u - 0.5f) * lonRange;
                    var dir = new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat));
                    int i = r * (columns + 1) + c;
                    vertices[i] = dir * radius;
                    normals[i] = -dir;
                    uvs[i] = new Vector2(u, v);
                }
            }
            var triangles = new int[columns * rows * 6];
            int t = 0;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int a = r * (columns + 1) + c;
                    int b = a + columns + 1;
                    // Wound to face the center.
                    triangles[t++] = a;
                    triangles[t++] = a + 1;
                    triangles[t++] = b;
                    triangles[t++] = a + 1;
                    triangles[t++] = b + 1;
                    triangles[t++] = b;
                }
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * radius * 2f);
        }

        public void SetSize(int index)
        {
            m_SizeIndex = Mathf.Clamp(index, 0, Widths.Length - 1);
            Layout();
        }

        /// <summary>Mixed reality: a TV-sized screen a few meters in front of the seats instead of the cinema screen.</summary>
        public void SetMixedRealityLayout(bool mixedReality)
        {
            m_MixedReality = mixedReality;
            transform.localPosition = mixedReality ? new Vector3(0f, 0.35f, 4.2f) : m_CinemaPosition;
            transform.localRotation = mixedReality ? Quaternion.identity : m_CinemaRotation;
            m_SizeScale = mixedReality ? 0.35f : 1f;
            Layout();
        }

        public void SetBrightness(float brightness) => m_Material.SetFloat(k_Brightness, brightness);

        bool m_UseAiDepth = true;

        /// <summary>2D→3D may use the AI depth model (if installed); off = only the nearly free estimate from the picture.</summary>
        public bool UseAiDepth
        {
            get => m_UseAiDepth;
            set
            {
                m_UseAiDepth = value;
                if (m_Depth != null) m_Depth.UseModel = value;
            }
        }

        /// <summary>Shows the depth map instead of the picture (bright = near), to see what 2D→3D works with.</summary>
        public bool ShowDepth { get; set; }

        /// <summary>2D→3D for 2D movies on the flat screen: 0 = off, 1 = light, 2 = medium, 3 = strong. Only for this viewer.</summary>
        public int Convert3D
        {
            get => m_Convert3D;
            set
            {
                m_Convert3D = Mathf.Clamp(value, 0, Conversion3D.Levels - 1);
                if (m_Convert3D == 0 && m_Depth != null)
                {
                    m_Depth.Dispose();
                    m_Depth = null;
                }
            }
        }

        /// <summary>The movie is converted right now (2D movie, flat screen, setting on).</summary>
        public bool IsConverting => m_Convert3D > 0 && m_Format == StereoFormat.Mono && m_Texture != null;

        /// <summary>Which depth source 2D→3D uses (for the settings page).</summary>
        public string Convert3DStatus =>
            m_Convert3D == 0 ? "Aus"
            : m_Format != StereoFormat.Mono ? "Nur für 2D-Filme (dieser ist " + m_Format.DisplayName() + ")"
            : m_Texture == null ? "Wartet auf einen Film"
            : m_Depth != null ? m_Depth.Status : "startet …";

        /// <summary>Light around the screen in mixed reality: 0 = off, 1 = soft, 2 = strong.</summary>
        public int GlowLevel
        {
            get => m_GlowLevel;
            set
            {
                m_GlowLevel = Mathf.Clamp(value, 0, 2);
                LayoutGlow();
            }
        }

        void UpdateConversion()
        {
            if (!IsConverting)
            {
                m_Material.SetFloat(k_Convert3D, 0f);
                m_Material.SetFloat(k_ShowDepth, 0f);
                return;
            }
            m_Depth ??= new DepthEstimator(m_UseAiDepth);
            m_Depth.Update(m_Texture);
            // The strength is an angle, so it depends on how far away the screen is.
            var rig = XRRig.Instance;
            Transform head = rig != null && rig.Head != null ? rig.Head : Camera.main != null ? Camera.main.transform : null;
            float distance = head != null ? Vector3.Distance(head.position, Center) : 12f;
            Conversion3D.Parameters(m_Convert3D, Size.x, distance, out float shift, out float convergence);
            m_Material.SetTexture(k_DepthTex, m_Depth.Depth);
            m_Material.SetFloat(k_Convergence, convergence);
            m_Material.SetFloat(k_Convert3D, shift);
            m_Material.SetFloat(k_ShowDepth, ShowDepth ? 1f : 0f);
        }

        /// <summary>Places the glow quad behind the screen, larger than the picture, with the left eye's part of the frame.</summary>
        void LayoutGlow()
        {
            if (m_Glow == null) return;
            bool on = m_GlowLevel > 0 && m_MixedReality && !IsImmersive && m_Texture != null;
            m_Glow.gameObject.SetActive(on);
            if (!on) return;
            float spread = Mathf.Max(Size.x, Size.y) * 0.32f;
            var quad = new Vector2(Size.x + 2 * spread, Size.y + 2 * spread);
            m_Glow.transform.localPosition = new Vector3(0, Size.y * 0.5f, 0.06f);
            m_Glow.transform.localScale = new Vector3(quad.x, quad.y, 1f);
            var eye = m_Format.EyeUv(0, m_SwapEyes);
            m_GlowMaterial.SetVector(k_GlowEyeSt, new Vector4(eye.ScaleX, eye.ScaleY, eye.OffsetX, eye.OffsetY));
            m_GlowMaterial.SetVector(k_GlowScreenSize, new Vector4(Size.x, Size.y, 0, 0));
            m_GlowMaterial.SetVector(k_GlowQuadSize, new Vector4(quad.x, quad.y, 0, 0));
            m_GlowMaterial.SetFloat(k_GlowStrength, m_GlowLevel == 1 ? 0.45f : 0.8f);
            m_GlowMaterial.SetFloat(k_GlowFalloff, spread * (m_GlowLevel == 1 ? 0.3f : 0.45f));
        }

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
            LayoutGlow();
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
            if (m_Sphere.gameObject.activeSelf) FollowViewer();

            if (!Mathf.Approximately(m_RoomLight, m_RoomLightTarget))
            {
                m_RoomLight = Mathf.MoveTowards(m_RoomLight, m_RoomLightTarget, Time.deltaTime * 0.6f);
                Shader.SetGlobalFloat(k_RoomLight, m_RoomLight);
            }

            if (m_Texture != null)
                Graphics.Blit(m_Texture, m_GlowTexture);
            UpdateConversion();
        }
    }
}
