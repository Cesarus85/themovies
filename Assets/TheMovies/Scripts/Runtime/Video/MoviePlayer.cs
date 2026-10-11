using System;
using UnityEngine;
using UnityEngine.Video;

namespace TheMovies.Video
{
    public enum MovieState
    {
        Empty,
        Preparing,
        Ready,
        Error,
    }

    /// <summary>
    /// The movie player: on the Quest (OpenGL ES 3) the own ExoPlayer-based <see cref="NativeVideoPlayer"/>
    /// (large buffers, fast seeking, AC3/DTS, embedded subtitles, pitch-correct speed changes), otherwise Unity's
    /// <see cref="VideoPlayer"/> rendering into a RenderTexture. Measures seek latency, detects stalls (buffering)
    /// and exposes speed control for drift correction.
    /// </summary>
    public sealed class MoviePlayer : MonoBehaviour
    {
        public event Action Prepared;

        /// <summary>The texture object changed after <see cref="Prepared"/> (own player: first frame or new size).</summary>
        public event Action TextureChanged;
        public event Action<string> Failed;

        VideoPlayer m_Player;
        RenderTexture m_Texture;
        double m_SeekStartedAt;
        double m_SeekLatency = 0.3;
        double m_LastTime;
        double m_LastProgressAt;
        float m_Volume = 1f;
        float m_Speed = 1f;
        NativeVideoPlayer m_Native;

        /// <summary>Use the own player when the device supports it (can be switched off for comparison).</summary>
        public static bool PreferNative = true;

        /// <summary>True while the own (ExoPlayer) player plays the current movie.</summary>
        public bool IsNative => m_Native != null;

        /// <summary>A hint about the current movie worth showing (e.g. unsupported sound format), empty if none.</summary>
        public string Warning { get; private set; } = string.Empty;

        /// <summary>Embedded subtitle text to show right now (own player only).</summary>
        public string EmbeddedSubtitle => m_Native != null ? m_Native.Cues : string.Empty;

        /// <summary>Seconds the player has buffered ahead of the playback position (own player), MaxValue if unknown.</summary>
        public double BufferedAhead
        {
            get
            {
                if (m_Native == null || State != MovieState.Ready) return double.MaxValue;
                double duration = m_Native.Duration;
                if (duration > 0 && m_Native.BufferedPosition >= duration - 0.5) return double.MaxValue;
                return Math.Max(0, m_Native.BufferedPosition - m_Native.Position);
            }
        }

        public MovieState State { get; private set; } = MovieState.Empty;
        public string Source { get; private set; }
        public string Error { get; private set; }
        public Texture Texture => m_Native != null ? m_Native.Texture : (Texture)m_Texture;
        public int Width { get; private set; }
        public int Height { get; private set; }

        public double Duration => State != MovieState.Ready ? 0
            : m_Native != null ? Math.Max(0, m_Native.Duration)
            : m_Player.length > 0 ? m_Player.length : 0;

        public double Time => State != MovieState.Ready ? 0 : m_Native != null ? m_Native.Position : m_Player.time;
        public bool IsPlaying => State == MovieState.Ready && (m_Native != null ? m_Native.IsPlaying : m_Player.isPlaying);
        public bool IsSeeking { get; private set; }
        public bool CanSetSpeed => State == MovieState.Ready && (m_Native != null || m_Player.canSetPlaybackSpeed);

        /// <summary>Exponential average of how long seeks take on this device.</summary>
        public double SeekLatency => m_SeekLatency;

        /// <summary>True while playing but the picture does not advance (network stream buffering).</summary>
        public bool IsStalled { get; private set; }

        static double Now => UnityEngine.Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            m_Player = gameObject.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.isLooping = false;
            m_Player.waitForFirstFrame = true;
            m_Player.skipOnDrop = true;
            m_Player.source = VideoSource.Url;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.audioOutputMode = VideoAudioOutputMode.Direct;
            m_Player.prepareCompleted += OnPrepared;
            m_Player.errorReceived += OnError;
            m_Player.seekCompleted += OnSeekCompleted;
        }

        void OnDestroy()
        {
            m_Native?.Dispose();
            m_Native = null;
            if (m_Player != null)
            {
                m_Player.prepareCompleted -= OnPrepared;
                m_Player.errorReceived -= OnError;
                m_Player.seekCompleted -= OnSeekCompleted;
            }
            ReleaseTexture();
        }

        /// <summary>Loads a local file path or an http(s) URL. Playback starts paused at 0.</summary>
        public void Load(string pathOrUrl)
        {
            Unload();
            Source = pathOrUrl;
            State = MovieState.Preparing;
            if (PreferNative && !NativeVideoPlayer.Supported)
                Debug.Log("[TheMovies] Using Unity's VideoPlayer: " + NativeVideoPlayer.UnsupportedReason);
            if (PreferNative && NativeVideoPlayer.Supported)
            {
                try
                {
                    m_Native = new NativeVideoPlayer(pathOrUrl);
                    m_Native.SetVolume(m_Volume);
                    m_PrepareStartedAt = Now;
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TheMovies] Own player failed, using Unity's VideoPlayer: " + e.Message);
                    m_Native = null;
                }
            }
            try
            {
                m_Player.url = pathOrUrl;
                m_Player.controlledAudioTrackCount = 1;
                m_Player.EnableAudioTrack(0, true);
                m_Player.Prepare();
            }
            catch (Exception e)
            {
                SetError(e.Message);
            }
        }

        public void Unload()
        {
            if (m_Native != null)
            {
                m_Native.Dispose();
                m_Native = null;
            }
            m_NativeTexture = null;
            m_NativeReadySince = -1;
            Warning = string.Empty;
            if (m_Player.isPlaying || m_Player.isPrepared)
                m_Player.Stop();
            m_Player.targetTexture = null;
            ReleaseTexture();
            State = MovieState.Empty;
            Source = null;
            Error = null;
            IsSeeking = false;
            IsStalled = false;
            Width = Height = 0;
            m_Speed = 1f;
        }

        public void Play()
        {
            if (State != MovieState.Ready) return;
            if (m_Native != null)
            {
                if (!m_Native.PlayWhenReady) m_Native.SetPlaying(true);
                return;
            }
            if (m_Player.isPlaying) return;
            m_Player.Play();
            m_LastProgressAt = Now;
        }

        public void Pause()
        {
            if (State != MovieState.Ready) return;
            if (m_Native != null)
            {
                if (m_Native.PlayWhenReady) m_Native.SetPlaying(false);
                return;
            }
            if (!m_Player.isPlaying) return;
            m_Player.Pause();
        }

        public void Seek(double seconds)
        {
            if (State != MovieState.Ready) return;
            if (Duration > 0) seconds = Math.Min(seconds, Duration);
            seconds = Math.Max(0, seconds);
            IsSeeking = true;
            m_SeekStartedAt = Now;
            if (m_Native != null) m_Native.Seek(seconds);
            else m_Player.time = seconds;
        }

        public void SetSpeed(float speed)
        {
            if (!CanSetSpeed) return;
            if (Mathf.Abs(speed - m_Speed) < 0.002f) return;
            m_Speed = speed;
            if (m_Native != null) m_Native.SetSpeed(speed);
            else m_Player.playbackSpeed = speed;
        }

        public float Volume
        {
            get => m_Volume;
            set
            {
                m_Volume = Mathf.Clamp01(value);
                ApplyVolume();
            }
        }

        void ApplyVolume()
        {
            if (m_Native != null)
            {
                m_Native.SetVolume(m_Volume);
                return;
            }
            if (State != MovieState.Ready) return;
            for (ushort i = 0; i < m_Player.audioTrackCount; i++)
                m_Player.SetDirectAudioVolume(i, m_Volume);
        }

        void OnPrepared(VideoPlayer source)
        {
            if (State != MovieState.Preparing) return;

            Width = (int)source.width;
            Height = (int)source.height;
            if (Width <= 0 || Height <= 0)
            {
                SetError("Video hat keine Bildgröße (Codec nicht unterstützt?)");
                return;
            }

            ReleaseTexture();
            m_Texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
            {
                name = "TheMovies Video",
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            m_Texture.Create();
            source.targetTexture = m_Texture;

            State = MovieState.Ready;
            m_Speed = 1f;
            source.playbackSpeed = 1f;
            ApplyVolume();
            // Show the first frame right away
            source.Pause();
            Prepared?.Invoke();
        }

        void OnError(VideoPlayer source, string message)
        {
            Debug.LogWarning($"[TheMovies] Video error for '{Source}': {message}");
            SetError(message);
        }

        void SetError(string message)
        {
            State = MovieState.Error;
            Error = string.IsNullOrEmpty(message) ? "Unbekannter Fehler" : message;
            Failed?.Invoke(Error);
        }

        void OnSeekCompleted(VideoPlayer source)
        {
            if (!IsSeeking) return;
            IsSeeking = false;
            double took = Now - m_SeekStartedAt;
            if (took > 0 && took < 5)
                m_SeekLatency = m_SeekLatency * 0.6 + took * 0.4;
            m_LastProgressAt = Now;
            m_LastTime = m_Player.time;
        }

        double m_PrepareStartedAt;
        double m_NativeReadySince = -1;
        Texture m_NativeTexture;

        void UpdateNative()
        {
            m_Native.Tick();
            if (m_Native.State == NativeVideoPlayer.StateError)
            {
                string error = m_Native.Error;
                m_Native.Dispose();
                m_Native = null;
                SetError(error);
                return;
            }

            if (State == MovieState.Preparing)
            {
                // Ready once the first frame is in the texture (or the player has been ready for a while without one).
                bool ready = m_Native.State >= NativeVideoPlayer.StateReady && m_Native.VideoWidth > 0;
                if (!ready) m_NativeReadySince = -1;
                else if (m_NativeReadySince < 0) m_NativeReadySince = Now;
                if (ready && (m_Native.HasFrame || Now - m_NativeReadySince > 2.0))
                {
                    m_NativeTexture = m_Native.Texture;
                    Width = Mathf.RoundToInt(m_Native.VideoWidth * m_Native.PixelAspect);
                    Height = m_Native.VideoHeight;
                    State = MovieState.Ready;
                    m_Speed = 1f;
                    Warning = m_Native.AudioUnsupported ? "Tonformat wird nicht unterstützt – Film läuft ohne Ton." : string.Empty;
                    Prepared?.Invoke();
                }
                else if (Now - m_PrepareStartedAt > 60)
                {
                    m_Native.Dispose();
                    m_Native = null;
                    SetError("Zeitüberschreitung beim Öffnen (60 s)");
                }
                return;
            }

            if (State != MovieState.Ready) return;
            if (m_Native.Texture != m_NativeTexture)
            {
                m_NativeTexture = m_Native.Texture;
                if (m_NativeTexture != null) TextureChanged?.Invoke();
            }
            if (IsSeeking && (!m_Native.IsSeeking || Now - m_SeekStartedAt > 20))
            {
                IsSeeking = false;
                double took = Now - m_SeekStartedAt;
                if (took > 0 && took < 5) m_SeekLatency = m_SeekLatency * 0.6 + took * 0.4;
            }
            // Buffering while it should be playing.
            IsStalled = m_Native.PlayWhenReady && m_Native.State == NativeVideoPlayer.StateBuffering && !IsSeeking;
            if (m_Native.AudioUnsupported && Warning.Length == 0)
                Warning = "Tonformat wird nicht unterstützt – Film läuft ohne Ton.";
        }

        void Update()
        {
            if (m_Native != null)
            {
                UpdateNative();
                return;
            }
            if (State != MovieState.Ready)
            {
                IsStalled = false;
                return;
            }

            // Some platforms never report seekCompleted for tiny seeks; don't wait forever.
            if (IsSeeking && Now - m_SeekStartedAt > 6)
                IsSeeking = false;

            double t = m_Player.time;
            if (!m_Player.isPlaying || IsSeeking || Math.Abs(t - m_LastTime) > 0.0001)
                m_LastProgressAt = Now;
            m_LastTime = t;
            IsStalled = m_Player.isPlaying && Now - m_LastProgressAt > 0.75;
        }

        void ReleaseTexture()
        {
            if (m_Texture == null) return;
            m_Texture.Release();
            Destroy(m_Texture);
            m_Texture = null;
        }
    }
}
