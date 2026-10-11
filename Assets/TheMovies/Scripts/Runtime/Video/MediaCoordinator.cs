using System;
using System.Collections.Generic;
using System.IO;
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

        ResumeStore m_Resume = new ResumeStore();
        string m_ResumePath;
        float m_NextResumeRecord;
        float m_NextResumeSave;
        int m_ResumeOfferVersion = -1;
        float m_ResumeOfferUntil;

        /// <summary>Saved position of the movie the local user just picked, offered in the menu ("Weiterschauen").</summary>
        public double? ResumeOffer { get; private set; }

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

            m_ResumePath = Path.Combine(Application.persistentDataPath, "resume.txt");
            try
            {
                if (File.Exists(m_ResumePath))
                    m_Resume = ResumeStore.Deserialize(File.ReadAllText(m_ResumePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not read {m_ResumePath}: {e.Message}");
            }
        }

        /// <summary>Jumps everyone to the saved position.</summary>
        public void AcceptResume()
        {
            if (ResumeOffer.HasValue)
                Playback.Current.RequestSeek(ResumeOffer.Value);
            ResumeOffer = null;
        }

        /// <summary>Starts from the beginning and forgets the saved position.</summary>
        public void DeclineResume()
        {
            if (m_LoadedKey != null) m_Resume.Remove(m_LoadedKey);
            ResumeOffer = null;
        }

        void RecordResume(bool force)
        {
            if (m_LoadedKey == null || m_Player == null || m_Player.State != MovieState.Ready || m_Player.Duration <= 0) return;
            if (!force && Time.unscaledTime < m_NextResumeRecord) return;
            m_NextResumeRecord = Time.unscaledTime + 5f;
            m_Resume.Record(m_LoadedKey, m_Player.Time, m_Player.Duration, DateTime.UtcNow);
        }

        void SaveResume(bool force)
        {
            if (!m_Resume.IsDirty || m_ResumePath == null) return;
            if (!force && Time.unscaledTime < m_NextResumeSave) return;
            m_NextResumeSave = Time.unscaledTime + 20f;
            try
            {
                File.WriteAllText(m_ResumePath, m_Resume.Serialize());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not save {m_ResumePath}: {e.Message}");
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            RecordResume(true);
            SaveResume(true);
        }

        void OnDestroy()
        {
            RecordResume(true);
            SaveResume(true);
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

        /// <summary>Ends the movie for everyone; the position is kept for "Weiterschauen".</summary>
        public void Stop()
        {
            if (Playback.Current.Media.IsEmpty) return;
            RecordResume(true);
            Playback.Current.RequestStop();
        }

        public void TogglePlay()
        {
            var auth = Playback.Current;
            if (auth.Timeline.Playing) auth.RequestPause();
            else auth.RequestPlay();
        }

        /// <param name="direction">+1 = next format, -1 = previous format.</param>
        public void CycleFormat(int direction = 1)
        {
            var media = Playback.Current.Media;
            if (media.IsEmpty) return;
            m_FormatRefined = true;
            var format = media.StereoFormat;
            int steps = direction >= 0 ? 1 : StereoFormats.All.Length - 1;
            for (int i = 0; i < steps; i++)
                format = format.Next();
            Playback.Current.RequestFormat(format, media.SwapEyes);
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

            if (timeline.Playing && !cmd.Seek)
                RecordResume(false);
            SaveResume(false);
            if (ResumeOffer.HasValue && (media.Version != m_ResumeOfferVersion || Time.unscaledTime > m_ResumeOfferUntil ||
                                         timeline.MediaTimeAt(auth.HostNow) > 30))
                ResumeOffer = null;

            bool buffering = m_Player.IsStalled;
            float ahead = float.MaxValue, window = float.MaxValue;
            double position = m_Player.IsSeeking ? cmd.Target : m_Player.Time;
            if (IsStreaming && HostStreamService.Instance != null)
            {
                ahead = HostStreamService.Instance.BufferedSeconds(position, m_Player.Duration);
                window = HostStreamService.Instance.ReadAheadSeconds(m_Player.Duration);
            }
            else if (CurrentEntry != null && CurrentEntry.IsNetwork && NetworkLibrary.Instance != null && NetworkLibrary.Instance.IsProxyStreaming)
            {
                ahead = NetworkLibrary.Instance.BufferedSeconds(position, m_Player.Duration);
                window = NetworkLibrary.Instance.ReadAheadSeconds(m_Player.Duration);
            }
            if (!cmd.ReachedEnd)
            {
                // Report "buffering" early so playback waits, and "ready" again only with some reserve
                // (at most what the read-ahead window can hold, else a high-bitrate movie would never be ready).
                float reserve = Mathf.Clamp(window * 0.6f, 1.5f, 6f);
                buffering |= ahead < (timeline.Playing ? 1.0f : reserve);
            }
            string nasError = CurrentEntry != null && CurrentEntry.IsNetwork ? NetworkLibrary.Instance?.StreamError : null;
            if (buffering && nasError != null)
                ReportProblem("NAS: " + TextUtil.Ellipsize(nasError, 70));
            LocalState = buffering ? ParticipantMediaState.Buffering : ParticipantMediaState.Ready;
            if (auth is LocalAuthority solo)
                solo.UpdateBuffering(!buffering);
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

            string location = entry.Location;
            if (entry.IsNetwork)
            {
                // NAS file: played through the local proxy (SMB, WebDAV) or straight from the server (DLNA).
                string error = "Netzwerkquellen nicht verfügbar.";
                location = null;
                if (NetworkLibrary.Instance != null)
                    location = NetworkLibrary.Instance.Resolve(entry, out error);
                if (location == null)
                {
                    UnloadMovie();
                    CurrentEntry = null;
                    LocalState = ParticipantMediaState.Missing;
                    Problem = error;
                    return;
                }
            }
            else
            {
                NetworkLibrary.Instance?.StopPlayback();
            }

            CurrentEntry = entry;
            Problem = string.Empty;
            LocalState = ParticipantMediaState.Loading;
            m_Screen.SetTexture(null);
            // Streams: no speed correction (it distorts the sound when the player has to resample) and fewer,
            // larger corrective seeks (each seek means buffering again).
            bool streamed = stream || entry.IsUrl || entry.IsNetwork;
            m_Sync.Settings.AllowSpeedCorrection = !streamed;
            m_Sync.Settings.SeekThresholdWithoutSpeed = streamed ? 0.6 : 0.25;
            m_Player.Load(location);
            LoadSubtitles(entry, media, stream);
        }

        void LoadSubtitles(MediaEntry entry, MediaDescriptor media, bool stream)
        {
            var subtitles = SubtitleService.Instance;
            if (subtitles == null) return;
            if (stream)
            {
                subtitles.Clear();
                int version = media.Version;
                HostStreamService.Instance?.RequestSubtitles(version, (v, data) =>
                {
                    if (v == m_LoadedVersion && data != null)
                        subtitles.SetTrack(data, "vom Gastgeber");
                });
            }
            else if (entry.IsNetwork)
            {
                subtitles.Clear();
                int token = subtitles.LoadToken;
                NetworkLibrary.Instance?.LoadSubtitles(entry, (data, name) =>
                {
                    if (token == subtitles.LoadToken && CurrentEntry == entry)
                        subtitles.SetTrack(data, name);
                });
            }
            else if (entry.IsUrl)
            {
                subtitles.LoadForUrl(entry.Location);
            }
            else
            {
                subtitles.LoadForFile(entry.Location);
            }
        }

        void UnloadMovie()
        {
            RecordResume(true);
            SubtitleService.Instance?.Clear();
            ResumeOffer = null;
            // Close the streams first: a decoder waiting for data then gets an immediate end instead of a timeout.
            if (IsStreaming)
            {
                HostStreamService.Instance?.StopStream();
                IsStreaming = false;
            }
            NetworkLibrary.Instance?.StopPlayback();
            m_Player.Unload();
            m_Screen.SetTexture(null);
            bool hadMovie = CurrentEntry != null;
            CurrentEntry = null;
            SaveResume(true);
            // Give the decoder buffers and stream caches back right away.
            if (hadMovie)
            {
                Resources.UnloadUnusedAssets();
                GC.Collect();
            }
        }

        void OnPrepared()
        {
            m_Screen.SetVideoSize(m_Player.Width, m_Player.Height);
            m_Screen.SetTexture(m_Player.Texture);

            // The participant who picked the movie refines the guessed 3D format now that the frame size is known.
            var auth = Playback.Current;
            var media = auth.Media;
            bool pickedByMe = m_PendingSelectionKey != null && media.Key.ToString() == m_PendingSelectionKey;
            if (pickedByMe && m_ResumeOfferVersion != media.Version)
            {
                m_ResumeOfferVersion = media.Version;
                var saved = m_Resume.Get(m_PendingSelectionKey);
                if (saved.HasValue && !auth.Timeline.Playing && auth.Timeline.MediaTimeAt(auth.HostNow) < 5)
                {
                    ResumeOffer = saved;
                    m_ResumeOfferUntil = Time.unscaledTime + 60f;
                }
            }

            if (!m_FormatRefined && pickedByMe)
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
            string hint = CurrentEntry != null && CurrentEntry.IsNetwork && NetworkLibrary.Instance != null
                ? NetworkLibrary.Instance.DescribePlaybackFailure(CurrentEntry)
                : string.Empty;
            Problem = "Wiedergabefehler: " + TextUtil.Ellipsize(error, 90) + (hint.Length > 0 ? " · " + hint : string.Empty);
            Debug.LogWarning("[TheMovies] " + Problem);
            m_Screen.SetTexture(null);
        }
    }
}
