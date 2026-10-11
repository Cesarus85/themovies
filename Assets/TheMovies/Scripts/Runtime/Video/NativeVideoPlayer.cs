using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TheMovies.Video
{
    /// <summary>
    /// C# side of the Android player (Assets/Plugins/Android/TheMoviesPlayer.java, ExoPlayer/Media3).
    /// ExoPlayer decodes into a SurfaceTexture; once per frame <see cref="Tick"/> queues a render-thread callback
    /// that copies the newest frame into an OpenGL ES texture, which Unity uses as an external texture.
    /// Needs OpenGL ES 3 (with Vulkan the app falls back to Unity's VideoPlayer).
    /// </summary>
    public sealed class NativeVideoPlayer : IDisposable
    {
        const string k_JavaClass = "com.themovies.player.TheMoviesPlayer";

        public const int StateIdle = 0, StateBuffering = 1, StateReady = 2, StateEnded = 3, StateError = -1;

        static bool? s_Supported;
        static string s_UnsupportedReason = string.Empty;

        /// <summary>True on the Quest with OpenGL ES 3 and the Java player in the build.</summary>
        public static bool Supported
        {
            get
            {
                if (!s_Supported.HasValue) s_Supported = Detect();
                return s_Supported.Value;
            }
        }

        /// <summary>Why the own player is not used (shown in the menu), empty if it is.</summary>
        public static string UnsupportedReason
        {
            get
            {
                _ = Supported;
                return s_UnsupportedReason;
            }
        }

        static bool Detect()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3)
            {
                s_UnsupportedReason = "Eigener Player braucht OpenGL ES 3 (Grafik-API ist " + SystemInfo.graphicsDeviceType + ")";
                return false;
            }
            try
            {
                InitRenderCallback();
                return true;
            }
            catch (Exception e)
            {
                s_UnsupportedReason = "Eigener Player nicht im Build: " + e.Message;
                Debug.LogWarning("[TheMovies] Native player unavailable: " + e);
                return false;
            }
#else
            s_UnsupportedReason = "Eigener Player nur auf der Quest";
            return false;
#endif
        }

        // ---------------- Render thread bridge ----------------

        delegate void RenderEventDelegate(int eventId);

        static RenderEventDelegate s_Callback;
        static IntPtr s_RenderFunc;
        static IntPtr s_JavaClassRef;
        static IntPtr s_RenderMethod;
        static jvalue[] s_RenderArgs;

        static void InitRenderCallback()
        {
            using (var cls = new AndroidJavaClass(k_JavaClass))
            {
                // A global reference: the render thread can't look the class up itself (wrong class loader).
                s_JavaClassRef = AndroidJNI.NewGlobalRef(cls.GetRawClass());
            }
            s_RenderMethod = AndroidJNI.GetStaticMethodID(s_JavaClassRef, "renderFrame", "(I)V");
            if (s_RenderMethod == IntPtr.Zero) throw new MissingMethodException(k_JavaClass, "renderFrame");
            s_RenderArgs = new jvalue[1];
            s_Callback = OnRenderEvent;
            s_RenderFunc = Marshal.GetFunctionPointerForDelegate(s_Callback);
        }

        [AOT.MonoPInvokeCallback(typeof(RenderEventDelegate))]
        static void OnRenderEvent(int eventId)
        {
            AndroidJNI.AttachCurrentThread();
            s_RenderArgs[0].i = eventId;
            AndroidJNI.CallStaticVoidMethod(s_JavaClassRef, s_RenderMethod, s_RenderArgs);
            if (AndroidJNI.ExceptionOccurred() != IntPtr.Zero)
                AndroidJNI.ExceptionClear();
        }

        // ---------------- Instance ----------------

        readonly AndroidJavaClass m_Java;
        readonly int m_Id;
        double[] m_State = new double[17];
        Texture2D m_Texture;
        int m_TextureGeneration = -1;
        int m_CueVersion = -1;
        bool m_Disposed;

        public NativeVideoPlayer(string location, string preferredLanguage = "de")
        {
            m_Java = new AndroidJavaClass(k_JavaClass);
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
                m_Id = m_Java.CallStatic<int>("create", activity);
            m_Java.CallStatic("open", m_Id, location, preferredLanguage ?? "de");
        }

        public int State => (int)m_State[0];
        public double Position => m_State[1];
        public double Duration => m_State[2];
        public double BufferedPosition => m_State[3];
        public bool IsPlaying => m_State[4] > 0.5;
        public int VideoWidth => (int)m_State[5];
        public int VideoHeight => (int)m_State[6];
        public float PixelAspect => m_State[7] > 0 ? (float)m_State[7] : 1f;
        public bool HasFrame => m_Texture != null && m_State[11] > 0;
        public bool IsSeeking => m_State[12] > 0.5;
        public bool AudioUnsupported => m_State[14] > 0.5;
        public bool PlayWhenReady => m_State[16] > 0.5;
        public Texture Texture => m_Texture;

        /// <summary>Text of the embedded subtitle shown right now (empty if none).</summary>
        public string Cues { get; private set; } = string.Empty;

        public string Error => m_Java.CallStatic<string>("error", m_Id);

        /// <summary>Audio/subtitle tracks, one per line "a|lang|label|selected|supported".</summary>
        public string Tracks => m_Java.CallStatic<string>("tracks", m_Id);

        /// <summary>Once per frame on the main thread, before rendering.</summary>
        public void Tick()
        {
            if (m_Disposed) return;
            GL.IssuePluginEvent(s_RenderFunc, m_Id);
            m_State = m_Java.CallStatic<double[]>("state", m_Id) ?? m_State;

            int generation = (int)m_State[15];
            int textureId = (int)m_State[8];
            if (textureId != 0 && generation != m_TextureGeneration)
            {
                m_TextureGeneration = generation;
                DestroyTexture();
                m_Texture = Texture2D.CreateExternalTexture((int)m_State[9], (int)m_State[10], TextureFormat.RGBA32,
                    true, false, new IntPtr(textureId));
                m_Texture.name = "TheMovies Native Video";
                m_Texture.filterMode = FilterMode.Trilinear;
                m_Texture.anisoLevel = 4;
                m_Texture.wrapMode = TextureWrapMode.Clamp;
            }

            int cueVersion = (int)m_State[13];
            if (cueVersion != m_CueVersion)
            {
                m_CueVersion = cueVersion;
                Cues = m_Java.CallStatic<string>("cues", m_Id) ?? string.Empty;
            }
        }

        public void SetPlaying(bool playing) => m_Java.CallStatic("setPlaying", m_Id, playing);

        /// <param name="exact">Frame accurate (for sync) instead of the nearest keyframe.</param>
        public void Seek(double seconds, bool exact = true)
        {
            m_Java.CallStatic("seek", m_Id, Math.Max(0, seconds), exact);
            m_State[12] = 1;
            m_State[1] = seconds;
        }

        public void SetSpeed(float speed) => m_Java.CallStatic("setSpeed", m_Id, speed);
        public void SetVolume(float volume) => m_Java.CallStatic("setVolume", m_Id, volume);

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            DestroyTexture();
            m_Java.CallStatic("release", m_Id);
            // One more render-thread call frees the GL objects.
            GL.IssuePluginEvent(s_RenderFunc, 0);
            m_Java.Dispose();
        }

        void DestroyTexture()
        {
            if (m_Texture == null) return;
            UnityEngine.Object.Destroy(m_Texture);
            m_Texture = null;
        }
    }
}
