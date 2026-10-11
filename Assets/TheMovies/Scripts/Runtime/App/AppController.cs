using TheMovies.Core.Network;
using TheMovies.Net;
using TheMovies.Platform;
using TheMovies.UI;
using TheMovies.Video;
using TheMovies.World;
using TheMovies.XR;
using UnityEngine;

namespace TheMovies.App
{
    /// <summary>
    /// Entry point, placed in the scene by "The Movies > Projekt einrichten". Builds the cinema,
    /// the XR rig and the menu at runtime and wires everything together.
    /// </summary>
    public sealed class AppController : MonoBehaviour
    {
        [SerializeField] SessionManager m_Sessions;

        public AppSettings Settings { get; private set; }
        public SessionManager Sessions => m_Sessions;
        public MediaCoordinator Media { get; private set; }
        public ScreenController Screen { get; private set; }
        public MenuUI Menu { get; private set; }

        XRRig m_Rig;
        CinemaEnvironment m_Cinema;
        string m_VoiceChannel;

        public SubtitleService Subtitles { get; private set; }

        void Awake()
        {
            AppLog.Start();
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Settings = AppSettings.Load();

            if (m_Sessions == null)
                m_Sessions = FindFirstObjectByType<SessionManager>();
            if (m_Sessions == null)
                Debug.LogError("[TheMovies] SessionManager missing. Run 'The Movies > Projekt einrichten' in the editor.");

            var cinema = CinemaEnvironment.Build();
            m_Cinema = cinema;
            Screen = cinema.Screen;
            Screen.SetSize(Settings.ScreenSize);
            Screen.SetRoomLight(0.7f, immediate: true);

            m_Rig = XRRig.Create();
            m_Rig.SetHeightOffset(Settings.HeightOffset);
            var seat = CinemaEnvironment.SeatPose(1);
            m_Rig.SetSeat(seat.position, seat.rotation);

            MediaLibrary.EnsureAppFolder();
            var library = new MediaLibrary();
            var playerGo = new GameObject("Movie Player");
            playerGo.transform.SetParent(Screen.transform, false);
            var moviePlayer = playerGo.AddComponent<MoviePlayer>();
            moviePlayer.Volume = Settings.Volume;
            moviePlayer.AudioDelayMs = Settings.AudioDelayMs;
            Media = playerGo.AddComponent<MediaCoordinator>();
            Media.Init(moviePlayer, Screen, library);

            Subtitles = SubtitleService.Create(Screen, () => Media.Player.State == MovieState.Ready ? Media.Player.Time : Media.DisplayTime);
            Subtitles.Enabled = Settings.Subtitles;

            Menu = MenuUI.Create(this);

            NetworkLibrary.Create();
            HostStreamService.Create();
            HostStreamService.HostSourceProvider = version =>
            {
                var media = Playback.Current.Media;
                var entry = Media.CurrentEntry;
                if (media.Version != version || !media.HostStream || entry == null || entry.IsUrl || Media.IsStreaming) return null;
                return entry.IsNetwork ? NetworkLibrary.Instance.OpenForHosting(entry) : new LocalFileSource(entry.Location);
            };
            HostStreamService.HostSubtitleProvider = version =>
            {
                var media = Playback.Current.Media;
                var entry = Media.CurrentEntry;
                if (media.Version != version || entry == null || entry.IsUrl) return null;
                // Whatever this device shows: the sidecar of a local file or the one found on the NAS.
                return Subtitles.TrackData;
            };

            NetworkPlayer.LocalNameProvider = () => Settings.Name;
            NetworkPlayer.LocalVoiceIdProvider = () => SessionManager.ServicesPlayerId;
            NetworkPlayer.LocalStatusProvider = Media.BuildStatus;
            NetworkPlayer.IsSpeaking = VoiceChat.IsSpeaking;
            VoiceChat.SetMuted(Settings.MicMuted);
        }

        void Start()
        {
            m_Rig.MenuToggleRequested += Menu.Toggle;
            m_Rig.PlayPauseRequested += Media.TogglePlay;
            m_Rig.SeekStepRequested += delta => Playback.Current.RequestSeekRelative(delta);
            if (m_Sessions != null)
                m_Sessions.Changed += OnSessionChanged;

            if (Settings.MixedReality)
                MixedReality.SetEnabled(true, m_Cinema, m_Rig.Camera);

            AndroidPermissions.RequestStorage(() => Media.Library.Refresh());
            Media.Library.Refresh();
            Menu.Show();
        }

        void OnDestroy()
        {
            if (m_Sessions != null)
                m_Sessions.Changed -= OnSessionChanged;
        }

        void Update()
        {
            // Join the voice channel the host announced, leave it when the cinema is gone.
            var sync = CinemaSync.Instance;
            string channel = sync != null && sync.IsSpawned && sync.VoiceChannel.Length > 0 ? sync.VoiceChannel : null;
            if (channel != m_VoiceChannel)
            {
                m_VoiceChannel = channel;
                if (channel != null)
                {
                    AndroidPermissions.RequestMicrophone();
                    _ = VoiceChat.Join(channel, Settings.Name);
                }
                else
                {
                    _ = VoiceChat.Leave();
                }
            }
        }

        void OnSessionChanged()
        {
            Menu.OnSessionChanged();

            if (m_Sessions.State == ConnectionState.Offline)
            {
                // Back on the default seat when alone again.
                var seat = CinemaEnvironment.SeatPose(1);
                m_Rig.SetSeat(seat.position, seat.rotation);
            }
        }

        public void SetName(string name)
        {
            Settings.Name = string.IsNullOrWhiteSpace(name) ? Settings.Name : name.Trim();
            Settings.Save();
            if (NetworkPlayer.Local != null)
                NetworkPlayer.Local.SetName(Settings.Name);
        }

        public void ChangeVolume(float delta)
        {
            Settings.Volume = Mathf.Clamp01(Mathf.Round((Settings.Volume + delta) * 10f) / 10f);
            Media.Player.Volume = Settings.Volume;
            Settings.Save();
        }

        public void SetSubtitles(bool enabled)
        {
            Subtitles.Enabled = enabled;
            Settings.Subtitles = enabled;
            Settings.Save();
        }

        public void SetMixedReality(bool enabled)
        {
            MixedReality.SetEnabled(enabled, m_Cinema, m_Rig.Camera);
            Settings.MixedReality = MixedReality.Enabled;
            Settings.Save();
        }

        /// <summary>Sound offset of the own player in ms (+ later, - earlier); saved for the next movies.</summary>
        public void ChangeAudioDelay(int deltaMs)
        {
            int value = Mathf.Clamp(Settings.AudioDelayMs + deltaMs, -500, 500);
            Settings.AudioDelayMs = value;
            Settings.Save();
            Media.Player.AudioDelayMs = value;
        }

        public void ChangeHeight(float delta)
        {
            m_Rig.SetHeightOffset(Mathf.Round((m_Rig.HeightOffset + delta) * 100f) / 100f);
            Settings.HeightOffset = m_Rig.HeightOffset;
            Settings.Save();
        }

        public void SetScreenSize(int index)
        {
            Screen.SetSize(index);
            Settings.ScreenSize = Screen.SizeIndex;
            Settings.Save();
        }

        public void SetMicMuted(bool muted)
        {
            VoiceChat.SetMuted(muted);
            Settings.MicMuted = muted;
            Settings.Save();
        }
    }
}
