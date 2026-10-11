#if THEMOVIES_INFERENCE
using System;
using System.Collections;
using TheMovies.Core;
using TheMovies.UI;
using Unity.InferenceEngine;
using UnityEngine;

namespace TheMovies.World
{
    public static partial class DepthModels
    {
        static partial void Create(ref IDepthModel model, ref string status)
        {
            var asset = Resources.Load<ModelAsset>(ResourcePath);
            if (asset == null)
            {
                status = "Schätzung aus dem Bild (KI-Modell fehlt – Menü „The Movies > 2D→3D mit KI einrichten“)";
                return;
            }
            try
            {
                model = new InferenceDepthModel(asset);
                status = "KI-Tiefe (Depth Anything V2 Small, " + InferenceDepthModel.Width + "×" + InferenceDepthModel.Height + ")";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Depth model could not be loaded: " + e);
                status = "Schätzung aus dem Bild (KI-Modell lädt nicht: " + e.Message + ")";
            }
        }
    }

    /// <summary>
    /// Depth Anything V2 Small on the GPU. One estimate is spread over several frames (a few layers per frame), so
    /// the headset keeps its frame rate; the result is read back asynchronously, normalized and smoothed on the CPU
    /// (<see cref="DepthNormalizer"/>) and uploaded as a small 8-bit texture. About 4–8 new depth maps per second.
    /// </summary>
    sealed class InferenceDepthModel : IDepthModel
    {
        // Multiples of the model's 14-pixel patches, 16:9.
        public const int Width = 252;
        public const int Height = 140;
        const int k_LayersPerFrame = 48;

        readonly Model m_Model;
        readonly Worker m_Worker;
        readonly Tensor<float> m_Input;
        readonly RenderTexture m_InputTexture;
        readonly Material m_Prepare;
        readonly DepthNormalizer m_Normalizer = new DepthNormalizer(Width, Height);
        readonly byte[] m_Bytes = new byte[Width * Height];
        Texture2D m_Output;
        IEnumerator m_Running;
        bool m_ReadingBack;
        bool m_Disposed;

        public InferenceDepthModel(ModelAsset asset)
        {
            m_Model = ModelLoader.Load(asset);
            var backend = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.GPUPixel;
            m_Worker = new Worker(m_Model, backend);
            m_Input = new Tensor<float>(new TensorShape(1, 3, Height, Width));
            m_InputTexture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "TheMovies Depth Input",
                filterMode = FilterMode.Bilinear,
            };
            m_InputTexture.Create();
            m_Prepare = new Material(UIFactory.FindShader("Hidden/TheMovies/DepthInput"));
        }

        public Texture Depth => m_Output;

        public string Error { get; private set; }

        public void Tick(Texture video)
        {
            if (m_Disposed || Error != null || m_ReadingBack) return;
            if (m_Running == null)
            {
                Graphics.Blit(video, m_InputTexture, m_Prepare);
                TextureConverter.ToTensor(m_InputTexture, m_Input, new TextureTransform());
                m_Running = m_Worker.ScheduleIterable(m_Input);
            }
            for (int i = 0; i < k_LayersPerFrame; i++)
            {
                if (m_Running.MoveNext()) continue;
                m_Running = null;
                ReadBack();
                return;
            }
        }

        async void ReadBack()
        {
            m_ReadingBack = true;
            try
            {
                var output = m_Worker.PeekOutput() as Tensor<float>;
                if (output == null) throw new InvalidOperationException("Das Modell liefert keine Gleitkomma-Tiefe.");
                if (output.shape.length != Width * Height)
                    throw new InvalidOperationException($"Unerwartete Modellausgabe {output.shape} (erwartet {Height}×{Width}).");
                using (var cpu = await output.ReadbackAndCloneAsync())
                {
                    if (m_Disposed) return;
                    m_Normalizer.Add(cpu.DownloadToArray());
                }
                m_Normalizer.ToBytes(m_Bytes);
                if (m_Output == null)
                {
                    m_Output = new Texture2D(Width, Height, TextureFormat.R8, false, true)
                    {
                        name = "TheMovies Depth (AI)",
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                }
                m_Output.SetPixelData(m_Bytes, 0);
                m_Output.Apply(false);
            }
            catch (Exception e)
            {
                Error = e.Message;
                Debug.LogWarning("[TheMovies] Depth model: " + e);
            }
            finally
            {
                m_ReadingBack = false;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            m_Worker?.Dispose();
            m_Input?.Dispose();
            if (m_InputTexture != null) m_InputTexture.Release();
            if (m_Prepare != null) UnityEngine.Object.Destroy(m_Prepare);
            if (m_Output != null) UnityEngine.Object.Destroy(m_Output);
        }
    }
}
#endif
