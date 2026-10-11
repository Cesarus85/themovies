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
    /// Thin wrapper around Unity's <see cref="VideoPlayer"/>: loads files/URLs into a RenderTexture,
    /// measures seek latency, detects stalls (buffering) and exposes speed control for drift correction.
    /// </summary>
    public sealed class MoviePlayer : MonoBehaviour
    {
        public event Action Prepared;
        public event Action<string> Failed;

        VideoPlayer m_Player;
        RenderTexture m_Texture;
        double m_SeekStartedAt;
        double m_SeekLatency = 0.3;
        double m_LastTime;
        double m_LastProgressAt;
        float m_Volume = 1f;
        float m_Speed = 1f;

        public MovieState State { get; private set; } = MovieState.Empty;
        public string Source { get; private set; }
        public string Error { get; private set; }
        public Texture Texture => m_Texture;
        public int Width { get; private set; }
        public int Height { get; private set; }

        public double Duration => State == MovieState.Ready && m_Player.length > 0 ? m_Player.length : 0;
        public double Time => State == MovieState.Ready ? m_Player.time : 0;
        public bool IsPlaying => State == MovieState.Ready && m_Player.isPlaying;
        public bool IsSeeking { get; private set; }
        public bool CanSetSpeed => State == MovieState.Ready && m_Player.canSetPlaybackSpeed;

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
            if (State != MovieState.Ready || m_Player.isPlaying) return;
            m_Player.Play();
            m_LastProgressAt = Now;
        }

        public void Pause()
        {
            if (State != MovieState.Ready || !m_Player.isPlaying) return;
            m_Player.Pause();
        }

        public void Seek(double seconds)
        {
            if (State != MovieState.Ready) return;
            if (Duration > 0) seconds = Math.Min(seconds, Duration);
            seconds = Math.Max(0, seconds);
            IsSeeking = true;
            m_SeekStartedAt = Now;
            m_Player.time = seconds;
        }

        public void SetSpeed(float speed)
        {
            if (!CanSetSpeed) return;
            if (Mathf.Abs(speed - m_Speed) < 0.002f) return;
            m_Speed = speed;
            m_Player.playbackSpeed = speed;
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

        void Update()
        {
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
