using System;
using System.Collections.Generic;
using TheMovies.Core;
using TheMovies.Net;
using TheMovies.World;
using UnityEngine;

namespace TheMovies.Video
{
    public enum LightMode
    {
        Auto,
        On,
        Off,
    }

    /// <summary>
    /// Glue between the shared playback state and this device: finds and loads the selected movie,
    /// keeps the local video player in sync with the shared timeline and drives the screen.
    /// </summary>
    public sealed class MediaCoordinator : MonoBehaviour
    {
        MoviePlayer m_Player;
        ScreenController m_Screen;
        MediaLibrary m_Library;
        readonly SyncController m_Sync = new SyncController();

        IPlaybackAuthority m_Authority;
        int m_LoadedVersion = -1;
        string m_LoadedKey;
        byte m_AppliedFormat = 255;
        bool m_AppliedSwap;
        bool m_DurationReported;
        string m_PendingSelectionKey;
        bool m_FormatRefined;
        bool m_RetryMissing;
        float m_ReportedProblemUntil;
        readonly Dictionary<string, MediaEntry> m_Assignments = new Dictionary<string, MediaEntry>();

        public ParticipantMediaState LocalState { get; private set; } = ParticipantMediaState.NoMovie;
        public SyncCommand LastCommand { get; private set; }
        public MediaEntry CurrentEntry { get; private set; }
        public MoviePlayer Player => m_Player;
        public MediaLibrary Library => m_Library;

        /// <summary>True while the movie comes from the host's stream instead of a local file.</summary>
        public bool IsStreaming { get; private set; }
        public LightMode Lights { get; set; } = LightMode.Auto;

        /// <summary>Last problem worth telling the user about (missing file, decoder error, …).</summary>
        public string Problem { get; private set; } = string.Empty;

        public void Init(MoviePlayer player, ScreenController screen, MediaLibrary library)
        {
            m_Player = player;
            m_Screen = screen;
            m_Library = library;
            m_Player.Prepared += OnPrepared;
            m_Player.Failed += OnFailed;
            m_Library.Changed += () => m_RetryMissing = true;
        }

        // ---------------- Commands from the UI ----------------

        /// <summary>Selects a movie for everybody in the cinema.</summary>
        public bool Select(MediaEntry entry, out string error)
        {
            error = null;
            if (entry == null) return false;
            if (!Playback.FitsNetwork(entry))
            {
                error = "Die Adresse ist zu lang (max. 500 Zeichen).";
                return false;
            }
            var format = entry.FormatHint ?? StereoFormats.Detect(entry.FileName);
            m_PendingSelectionKey = entry.MatchKey;
            m_FormatRefined = entry.FormatHint.HasValue;
            // Remember which local file belongs to this key, in case the same name exists twice.
            if (!entry.IsUrl)
                m_Assignments[entry.MatchKey] = entry;
            Playback.Current.RequestSelectMedia(entry, format);
            return true;
        }

        /// <summary>Uses a local file for the currently selected movie (when it could not be found automatically).</summary>
        public void AssignLocalFile(MediaEntry entry)
        {
            var media = Playback.Current.Media;
            if (media.IsEmpty || entry == null) return;
            m_Assignments[media.Key.ToString()] = entry;
            m_LoadedVersion = -1;
        }

        /// <summary>Shows a message in the problem line of the menu.</summary>
        public void ReportProblem(string message)
        {
            Problem = message ?? string.Empty;
            m_ReportedProblemUntil = Time.unscaledTime + 8f;
        }

        public void TogglePlay()
        {
            var auth = Playback.Current;
            if (auth.Timeline.Playing) auth.RequestPause();
            else auth.RequestPlay();
        }

        public void CycleFormat()
        {
            var media = Playback.Current.Media;
            if (media.IsEmpty) return;
            m_FormatRefined = true;
            Playback.Current.RequestFormat(media.StereoFormat.Next(), media.SwapEyes);
        }

        public void ToggleSwapEyes()
        {
            var media = Playback.Current.Media;
            if (media.IsEmpty) return;
            Playback.Current.RequestFormat(media.StereoFormat, !media.SwapEyes);
        }

        /// <summary>Time to show in the UI (the shared position, also when the local file is missing).</summary>
        public double DisplayTime
        {
            get
            {
                var auth = Playback.Current;
                if (auth.Media.IsEmpty) return 0;
                double t = auth.Timeline.MediaTimeAt(auth.HostNow);
                double d = DisplayDuration;
                return d > 0 ? Math.Min(t, d) : t;
            }
        }

        public double DisplayDuration
        {
            get
            {
                if (m_Player.State == MovieState.Ready && m_Player.Duration > 0) return m_Player.Duration;
                return Playback.Current.Media.Duration;
            }
        }

        public ParticipantStatus BuildStatus()
        {
            var auth = Playback.Current;
            return new ParticipantStatus
            {
                Media = LocalState,
                MediaVersion = auth.Media.Version,
                DriftMs = (short)Mathf.Clamp((float)(LastCommand.Drift * 1000.0), short.MinValue, short.MaxValue),
                RttMs = (short)Mathf.Clamp((float)(auth.RoundTrip * 1000.0), 0, short.MaxValue),
                Muted = VoiceChat.Muted,
            };
        }

        // ---------------- Per frame ----------------

        void Update()
        {
            var auth = Playback.Current;
            if (!ReferenceEquals(auth, m_Authority))
            {
                m_Authority = auth;
                m_LoadedVersion = -1;
                m_LoadedKey = null;
            }

            if (auth is LocalAuthority local)
                local.Tick();

            var media = auth.Media;
            if (media.IsEmpty)
            {
                if (m_LoadedVersion != 0)
                {
                    UnloadMovie();
                    m_LoadedVersion = 0;
                }
                LocalState = ParticipantMediaState.NoMovie;
                if (Time.unscaledTime > m_ReportedProblemUntil)
                    Problem = string.Empty;
                UpdateLights(false);
                return;
            }

            string key = media.Key.ToString();
            if (media.Version != m_LoadedVersion || key != m_LoadedKey || (m_RetryMissing && LocalState == ParticipantMediaState.Missing))
                LoadMedia(media, key);
            m_RetryMissing = false;

            if (media.Format != m_AppliedFormat || media.SwapEyes != m_AppliedSwap)
            {
                m_AppliedFormat = media.Format;
                m_AppliedSwap = media.SwapEyes;
                m_Screen.SetFormat(media.StereoFormat, media.SwapEyes);
            }

            var timeline = auth.Timeline;
            UpdateLights(timeline.Playing);

            if (m_Player.State != MovieState.Ready)
            {
                if (m_Player.State == MovieState.Preparing) LocalState = ParticipantMediaState.Loading;
                return;
            }

            if (!m_DurationReported && m_Player.Duration > 0)
            {
                m_DurationReported = true;
                if (media.Duration <= 0)
                    auth.ReportDuration(media.Version, m_Player.Duration);
            }

            if (!auth.ClockReady)
            {
                m_Player.Pause();
                LocalState = ParticipantMediaState.Loading;
                return;
            }

            var state = new LocalPlayerState
            {
                MediaTime = m_Player.Time,
                IsPlaying = m_Player.IsPlaying,
                IsSeeking = m_Player.IsSeeking,
                Duration = m_Player.Duration,
                CanSetSpeed = m_Player.CanSetSpeed,
                SeekLatency = m_Player.SeekLatency,
            };
            var cmd = m_Sync.Update(timeline, auth.HostNow, state);
            LastCommand = cmd;

            if (cmd.Seek)
                m_Player.Seek(cmd.SeekTarget);
            if (cmd.ShouldPlay) m_Player.Play();
            else m_Player.Pause();
            m_Player.SetSpeed(cmd.Speed);

            bool buffering = m_Player.IsStalled;
            if (IsStreaming && !cmd.ReachedEnd && HostStreamService.Instance != null)
            {
                // Report "buffering" early so the host waits, and only "ready" again with some reserve.
                float ahead = HostStreamService.Instance.BufferedSeconds(m_Player.Time, m_Player.Duration);
                buffering |= ahead < (timeline.Playing ? 1.0f : 6f);
            }
            LocalState = buffering ? ParticipantMediaState.Buffering : ParticipantMediaState.Ready;
        }

        void UpdateLights(bool playing)
        {
            float target;
            switch (Lights)
            {
                case LightMode.On: target = 1f; break;
                case LightMode.Off: target = 0f; break;
                default: target = playing ? 0f : 0.7f; break;
            }
            m_Screen.SetRoomLight(target);
        }

        void LoadMedia(MediaDescriptor media, string key)
        {
            m_LoadedVersion = media.Version;
            m_LoadedKey = key;
            m_DurationReported = false;
            m_Sync.Reset();

            MediaEntry entry;
            if (!m_Assignments.TryGetValue(key, out entry))
                entry = m_Library.FindMatch(key, media.IsUrl, media.Size);

            // No own copy: use the host's stream if it offers one.
            bool stream = false;
            if (entry == null && media.HostStream && Playback.Current is CinemaSync sync && !sync.IsServer && HostStreamService.Instance != null)
            {
                string url = HostStreamService.Instance.PrepareStream(media.Version, key, media.Size);
                entry = new MediaEntry { Title = media.Title.ToString(), Location = url, IsUrl = true };
                stream = true;
            }
            if (!stream && IsStreaming)
                HostStreamService.Instance?.StopStream();
            IsStreaming = stream;

            if (entry == null)
            {
                UnloadMovie();
                CurrentEntry = null;
                LocalState = ParticipantMediaState.Missing;
                Problem = $"Datei \"{key}\" nicht gefunden. Bitte in Movies/Download ablegen oder zuordnen.";
                return;
            }

            if (CurrentEntry != null && CurrentEntry.Location == entry.Location && m_Player.State == MovieState.Ready)
            {
                // Same file already loaded (e.g. authority switched from solo to a session): keep it.
                CurrentEntry = entry;
                Problem = string.Empty;
                return;
            }

            CurrentEntry = entry;
            Problem = string.Empty;
            LocalState = ParticipantMediaState.Loading;
            m_Screen.SetTexture(null);
            m_Player.Load(entry.Location);
        }

        void UnloadMovie()
        {
            if (IsStreaming)
            {
                HostStreamService.Instance?.StopStream();
                IsStreaming = false;
            }
            m_Player.Unload();
            m_Screen.SetTexture(null);
            CurrentEntry = null;
        }

        void OnPrepared()
        {
            m_Screen.SetVideoSize(m_Player.Width, m_Player.Height);
            m_Screen.SetTexture(m_Player.Texture);

            // The participant who picked the movie refines the guessed 3D format now that the frame size is known.
            var auth = Playback.Current;
            var media = auth.Media;
            if (!m_FormatRefined && m_PendingSelectionKey != null && media.Key.ToString() == m_PendingSelectionKey)
            {
                m_FormatRefined = true;
                string name = CurrentEntry != null ? CurrentEntry.FileName : m_PendingSelectionKey;
                var detected = StereoFormats.Detect(name, m_Player.Width, m_Player.Height);
                if (detected != media.StereoFormat)
                    auth.RequestFormat(detected, media.SwapEyes);
            }
        }

        void OnFailed(string error)
        {
            LocalState = ParticipantMediaState.Error;
            Problem = "Wiedergabefehler: " + TextUtil.Ellipsize(error, 80);
            m_Screen.SetTexture(null);
        }
    }
}
