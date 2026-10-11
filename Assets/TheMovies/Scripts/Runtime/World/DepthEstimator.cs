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

        /// <summary>Called every frame: starts a new estimate from <paramref name="video"/> or continues the running one.</summary>
        void Tick(Texture video);
    }

    /// <summary>
    /// Depth for 2D→3D. Uses the depth model when it is installed (see "The Movies > 2D→3D mit KI einrichten"),
    /// otherwise – and until the model's first result – a cheap estimate from the picture itself.
    /// </summary>
    public sealed class DepthEstimator : IDisposable
    {
        const int k_Width = 96;
        const int k_Height = 54;

        readonly Material m_Estimate;
        RenderTexture m_Current;
        RenderTexture m_Previous;
        IDepthModel m_Model;
        bool m_ModelTried;
        string m_ModelStatus = string.Empty;
        int m_Frame;

        static readonly int k_PreviousId = Shader.PropertyToID("_Previous");
        static readonly int k_BlendId = Shader.PropertyToID("_Blend");

        public DepthEstimator()
        {
            m_Estimate = new Material(UIFactory.FindShader("Hidden/TheMovies/DepthEstimate"));
            m_Current = CreateTarget();
            m_Previous = CreateTarget();
        }

        /// <summary>For the settings page: which depth source is in use.</summary>
        public string Status
        {
            get
            {
                if (m_Model != null && m_Model.Error == null)
                    return m_Model.Depth != null ? "KI-Tiefe (Depth Anything)" : "KI-Tiefe startet …";
                if (m_Model != null) return "Schätzung – KI-Fehler: " + m_Model.Error;
                return m_ModelTried ? m_ModelStatus : "Schätzung aus dem Bild";
            }
        }

        public Texture Depth => m_Model != null && m_Model.Error == null && m_Model.Depth != null ? m_Model.Depth : m_Current;

        static RenderTexture CreateTarget()
        {
            var rt = new RenderTexture(k_Width, k_Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "TheMovies Depth Estimate",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        public void Update(Texture video)
        {
            if (video == null) return;
            if (!m_ModelTried)
            {
                m_ModelTried = true;
                m_Model = DepthModels.TryCreate(out m_ModelStatus);
                Debug.Log("[TheMovies] 2D→3D: " + m_ModelStatus);
            }
            if (m_Model != null && m_Model.Error == null)
            {
                try
                {
                    m_Model.Tick(video);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TheMovies] 2D→3D depth model failed: " + e);
                    m_Model.Dispose();
                    m_Model = null;
                }
                if (m_Model != null && m_Model.Depth != null) return;
            }

            // Every other frame is plenty for the estimate; it is blended over time anyway.
            if ((m_Frame++ & 1) != 0) return;
            m_Estimate.SetTexture(k_PreviousId, m_Current);
            m_Estimate.SetFloat(k_BlendId, 0.15f);
            Graphics.Blit(video, m_Previous, m_Estimate);
            (m_Current, m_Previous) = (m_Previous, m_Current);
        }

        public void Dispose()
        {
            m_Model?.Dispose();
            m_Model = null;
            if (m_Current != null) m_Current.Release();
            if (m_Previous != null) m_Previous.Release();
            UnityEngine.Object.Destroy(m_Estimate);
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
