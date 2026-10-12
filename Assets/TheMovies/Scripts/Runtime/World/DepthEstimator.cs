using System;
using TheMovies.UI;
using UnityEngine;

namespace TheMovies.World
{
    /// <summary>A depth model that turns video frames into depth maps (Depth Anything via Unity Inference Engine).</summary>
    public interface IDepthModel : IDisposable
    {
        /// <summary>Latest depth map (R: 0 = far, 1 = near), null until the first one is ready.</summary>
        Texture Depth { get; }

        /// <summary>Error that made the model unusable, null while it works.</summary>
        string Error { get; }

        /// <summary>Speed for the settings page, e.g. "5,2 Tiefenbilder/s · 24 Schichten pro Bild".</summary>
        string Stats { get; }

        /// <summary>Called every frame: starts a new estimate from <paramref name="video"/> or continues the running one.</summary>
        void Tick(Texture video);
    }

    /// <summary>
    /// Depth for 2D→3D. Uses the depth model when it is installed and allowed (see "The Movies > 2D→3D mit KI
    /// einrichten"), otherwise – and until the model's first result – a cheap estimate from the picture itself.
    /// The depth shown is never switched abruptly: every frame it glides towards the newest map, so a new map
    /// (a few per second) doesn't make parts of the picture jump sideways.
    /// </summary>
    public sealed class DepthEstimator : IDisposable
    {
        const int k_EstimateWidth = 96;
        const int k_EstimateHeight = 54;
        const int k_DisplayWidth = 256;
        const int k_DisplayHeight = 144;

        /// <summary>How fast the shown depth follows a new map (1/s): about 90 % after 0.25 s.</summary>
        const float k_FollowRate = 9f;

        readonly Material m_Estimate;
        readonly Material m_Blend;
        RenderTexture m_EstimateCurrent, m_EstimatePrevious;
        RenderTexture m_DisplayCurrent, m_DisplayPrevious;
        IDepthModel m_Model;
        bool m_ModelTried;
        bool m_UseModel;
        string m_ModelStatus = string.Empty;
        int m_Frame;

        static readonly int k_PreviousId = Shader.PropertyToID("_Previous");
        static readonly int k_BlendId = Shader.PropertyToID("_Blend");

        public DepthEstimator(bool useModel)
        {
            m_UseModel = useModel;
            m_Estimate = new Material(UIFactory.FindShader("Hidden/TheMovies/DepthEstimate"));
            m_Blend = new Material(UIFactory.FindShader("Hidden/TheMovies/DepthBlend"));
            m_EstimateCurrent = CreateTarget(k_EstimateWidth, k_EstimateHeight);
            m_EstimatePrevious = CreateTarget(k_EstimateWidth, k_EstimateHeight);
            m_DisplayCurrent = CreateTarget(k_DisplayWidth, k_DisplayHeight);
            m_DisplayPrevious = CreateTarget(k_DisplayWidth, k_DisplayHeight);
        }

        /// <summary>Use the AI model if it is installed (off: only the estimate from the picture, nearly free).</summary>
        public bool UseModel
        {
            get => m_UseModel;
            set
            {
                if (m_UseModel == value) return;
                m_UseModel = value;
                if (!value)
                {
                    m_Model?.Dispose();
                    m_Model = null;
                    m_ModelTried = false;
                }
            }
        }

        bool ModelWorks => m_Model != null && m_Model.Error == null;

        /// <summary>For the settings page: which depth source is in use and how fast it is.</summary>
        public string Status
        {
            get
            {
                if (!m_UseModel) return "Schätzung aus dem Bild (KI ausgeschaltet)";
                if (ModelWorks) return m_Model.Depth != null ? "KI-Tiefe · " + m_Model.Stats : "KI-Tiefe startet …";
                if (m_Model != null) return "Schätzung – KI-Fehler: " + m_Model.Error;
                return m_ModelTried ? m_ModelStatus : "Schätzung aus dem Bild";
            }
        }

        /// <summary>The depth to show (R: 0 = far, 1 = near).</summary>
        public Texture Depth => m_DisplayCurrent;

        static RenderTexture CreateTarget(int width, int height)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "TheMovies Depth",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        public void Update(Texture video)
        {
            if (video == null) return;
            if (m_UseModel && !m_ModelTried)
            {
                m_ModelTried = true;
                m_Model = DepthModels.TryCreate(out m_ModelStatus);
                Debug.Log("[TheMovies] 2D→3D: " + m_ModelStatus);
            }

            Texture target = null;
            if (ModelWorks)
            {
                try
                {
                    m_Model.Tick(video);
                    target = m_Model.Depth;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TheMovies] 2D→3D depth model failed: " + e);
                    m_Model.Dispose();
                    m_Model = null;
                }
            }

            if (target == null)
            {
                // Estimate from the picture; every other frame is plenty, it is blended over time anyway.
                if ((m_Frame & 1) == 0)
                {
                    m_Estimate.SetTexture(k_PreviousId, m_EstimateCurrent);
                    m_Estimate.SetFloat(k_BlendId, 0.15f);
                    Graphics.Blit(video, m_EstimatePrevious, m_Estimate);
                    (m_EstimateCurrent, m_EstimatePrevious) = (m_EstimatePrevious, m_EstimateCurrent);
                }
                target = m_EstimateCurrent;
            }
            m_Frame++;

            // Glide the shown depth towards the newest map.
            float follow = 1f - Mathf.Exp(-k_FollowRate * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            m_Blend.SetTexture(k_PreviousId, m_DisplayCurrent);
            m_Blend.SetFloat(k_BlendId, m_Frame <= 1 ? 1f : follow);
            Graphics.Blit(target, m_DisplayPrevious, m_Blend);
            (m_DisplayCurrent, m_DisplayPrevious) = (m_DisplayPrevious, m_DisplayCurrent);
        }

        public void Dispose()
        {
            m_Model?.Dispose();
            m_Model = null;
            foreach (var rt in new[] { m_EstimateCurrent, m_EstimatePrevious, m_DisplayCurrent, m_DisplayPrevious })
                if (rt != null) rt.Release();
            UnityEngine.Object.Destroy(m_Estimate);
            UnityEngine.Object.Destroy(m_Blend);
        }
    }

    /// <summary>Finds the optional depth model (Unity Inference Engine package + model file in Resources).</summary>
    public static partial class DepthModels
    {
        /// <summary>Path of the model inside a Resources folder (Assets/TheMovies/Resources/TheMovies/DepthModel.onnx).</summary>
        public const string ResourcePath = "TheMovies/DepthModel";

        static partial void Create(ref IDepthModel model, ref string status);

        public static IDepthModel TryCreate(out string status)
        {
            IDepthModel model = null;
            string text = "Schätzung aus dem Bild (Paket „Inference Engine“ nicht installiert)";
            Create(ref model, ref text);
            status = text;
            return model;
        }
    }
}
