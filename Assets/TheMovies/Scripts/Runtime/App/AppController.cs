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

        /// <summary>Free seat choice (any seat of the room, balcony and boxes included).</summary>
        public SeatPicker Seats { get; private set; }

        /// <summary>What this viewer sees, for the others: 0 = cinema, 1 = living room, 2 = mixed reality.</summary>
        int RoomKey => MixedReality.Enabled ? 2 : (int)m_Cinema.CurrentRoom;

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
            if (Settings.Room == (int)CinemaEnvironment.Room.Home) cinema.SetRoom(CinemaEnvironment.Room.Home);
            Screen.Convert3D = Settings.Convert3D;
            Screen.GlowLevel = Settings.ScreenGlow;
            Screen.UseAiDepth = Settings.DepthAi;
            Screen.SetPicture(Settings.Brightness, Settings.Contrast, Settings.Saturation);
            Screen.DepthSteps = Settings.DepthSteps;

            m_Rig = XRRig.Create();
            m_Rig.SetHeightOffset(Settings.HeightOffset);
            m_Rig.RealHands = Settings.RealHands;
            var seat = CinemaEnvironment.SeatPose(1);
            m_Rig.SetSeat(seat.position, seat.rotation);
            Seats = SeatPicker.Create(cinema, m_Rig, () => RoomKey);
            NetworkPlayer.LocalRoomProvider = () => RoomKey;

            MediaLibrary.EnsureAppFolder();
            var library = new MediaLibrary();
            var playerGo = new GameObject("Movie Player");
            playerGo.transform.SetParent(Screen.transform, false);
            var moviePlayer = playerGo.AddComponent<MoviePlayer>();
            moviePlayer.Volume = Settings.Volume;
            moviePlayer.AudioDelayMs = Settings.AudioDelayMs;
            Media = playerGo.AddComponent<MediaCoordinator>();
            Media.Init(moviePlayer, Screen, library);
            Media.Lights = (LightMode)Mathf.Clamp(Settings.LightMode, 0, (int)LightMode.Manual);
            Media.ManualLight = Mathf.Clamp01(Settings.HouseLight);

            Subtitles = SubtitleService.Create(Screen, () => Media.Player.State == MovieState.Ready ? Media.Player.Time : Media.DisplayTime);
            Subtitles.Enabled = Settings.Subtitles;
            Subtitles.SizeScale = Settings.SubtitleSize;
            Subtitles.Placement = Settings.SubtitlePlacement;
            VoiceChat.PushToTalk = Settings.PushToTalk;

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
            m_Rig.MenuToggleRequested += () =>
            {
                // While choosing a seat the menu button cancels and brings the menu back.
                if (Seats.Active)
                {
                    Seats.End();
                    Menu.Show();
                }
                else
                {
                    Menu.Toggle();
                }
            };
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

        float m_DuckLevel = 1f;

        /// <summary>Movie sound factor while others talk, per ducking setting.</summary>
        static float DuckTarget(int setting) => setting == 1 ? 0.45f : setting == 2 ? 0.2f : 1f;

        void Update()
        {
            VoiceChat.TalkHeld = m_Rig != null && m_Rig.TalkHeld;
            // Lower the movie quickly when someone starts talking, raise it slowly after a pause.
            float target = Settings.Ducking > 0 && VoiceChat.OthersSpeaking ? DuckTarget(Settings.Ducking) : 1f;
            float speed = target < m_DuckLevel ? 4f : 0.8f;
            m_DuckLevel = Mathf.MoveTowards(m_DuckLevel, target, speed * Time.unscaledDeltaTime);
            Media.Player.Duck = m_DuckLevel;

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
                // Back on the default seat when alone again (a seat of one's own choosing stays).
                if (!Seats.HasOwnSeat)
                {
                    var seat = CinemaEnvironment.SeatPose(1);
                    m_Rig.SetSeat(seat.position, seat.rotation);
                }
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

        public CinemaEnvironment.Room Room => m_Cinema.CurrentRoom;

        public bool RoomsAvailable => m_Cinema.RoomsAvailable;

        /// <summary>House lights: automatic (dim while the movie plays) or a fixed level.</summary>
        public void SetLightMode(LightMode mode)
        {
            Media.Lights = mode;
            Settings.LightMode = (int)mode;
            Settings.Save();
        }

        /// <summary>Dims the house lights by hand in 10 % steps (switches to the manual mode).</summary>
        public void ChangeHouseLight(float delta)
        {
            float level = Media.Lights == LightMode.Manual ? Media.ManualLight : Settings.HouseLight;
            level = Mathf.Clamp01(Mathf.Round((level + delta) * 10f) / 10f);
            Media.ManualLight = Settings.HouseLight = level;
            SetLightMode(LightMode.Manual);
        }

        /// <summary>Shows the free seats to choose from (not in mixed reality, where the room is your own).</summary>
        public void BeginSeatChoice()
        {
            if (MixedReality.Enabled) return;
            Menu.Hide();
            Seats.Begin();
        }

        /// <summary>Cinema or living room (only for this viewer); switches mixed reality off.</summary>
        public void SetRoom(CinemaEnvironment.Room room)
        {
            if (MixedReality.Enabled) SetMixedReality(false);
            m_Cinema.SetRoom(room);
            Settings.Room = (int)m_Cinema.CurrentRoom;
            Settings.Save();
            Seats.ResetToStandardSeat(); // the seats of one room don't exist in the other
        }

        public void SetMixedReality(bool enabled)
        {
            MixedReality.SetEnabled(enabled, m_Cinema, m_Rig.Camera);
            Settings.MixedReality = MixedReality.Enabled;
            Settings.Save();
            Seats?.ResetToStandardSeat();
        }

        /// <summary>2D→3D for 2D movies (only for this viewer): 0 = off, 1 = light, 2 = strong.</summary>
        public void SetConvert3D(int level)
        {
            Screen.Convert3D = level;
            Settings.Convert3D = Screen.Convert3D;
            Settings.Save();
        }

        /// <summary>Changes brightness (0), contrast (1) or saturation (2) by <paramref name="delta"/>; NaN resets all.</summary>
        public void ChangePicture(int which, float delta)
        {
            if (float.IsNaN(delta))
            {
                Settings.Brightness = Settings.Contrast = Settings.Saturation = 1f;
                Settings.DepthSteps = 0;
                Screen.DepthSteps = 0;
            }
            else if (which == 0) Settings.Brightness = Mathf.Clamp(Mathf.Round((Settings.Brightness + delta) * 20f) / 20f, 0.5f, 1.6f);
            else if (which == 1) Settings.Contrast = Mathf.Clamp(Mathf.Round((Settings.Contrast + delta) * 20f) / 20f, 0.6f, 1.6f);
            else Settings.Saturation = Mathf.Clamp(Mathf.Round((Settings.Saturation + delta) * 10f) / 10f, 0f, 2f);
            Screen.SetPicture(Settings.Brightness, Settings.Contrast, Settings.Saturation);
            Settings.Save();
        }

        public void ChangeDepthSteps(int delta)
        {
            Screen.DepthSteps += delta;
            Settings.DepthSteps = Screen.DepthSteps;
            Settings.Save();
        }

        public void ChangeSubtitleSize(float delta)
        {
            Subtitles.SizeScale = Mathf.Round((Subtitles.SizeScale + delta) * 10f) / 10f;
            Settings.SubtitleSize = Subtitles.SizeScale;
            Settings.Save();
        }

        public void SetSubtitlePlacement(int placement)
        {
            Subtitles.Placement = placement;
            Settings.SubtitlePlacement = Subtitles.Placement;
            Settings.Save();
        }

        public void SetPushToTalk(bool enabled)
        {
            VoiceChat.PushToTalk = enabled;
            Settings.PushToTalk = enabled;
            Settings.Save();
        }

        public void SetDucking(int level)
        {
            Settings.Ducking = Mathf.Clamp(level, 0, 2);
            Settings.Save();
        }

        /// <summary>Real hands and controllers (passthrough) or models.</summary>
        public void SetRealHands(bool enabled)
        {
            m_Rig.RealHands = enabled;
            Settings.RealHands = enabled;
            Settings.Save();
        }

        public bool RealHands => Settings.RealHands;

        public bool ShowingRealHands => m_Rig != null && m_Rig.ShowingRealHands;

        public void SetDepthAi(bool enabled)
        {
            Screen.UseAiDepth = enabled;
            Settings.DepthAi = enabled;
            Settings.Save();
        }

        /// <summary>Light around the screen in mixed reality: 0 = off, 1 = soft, 2 = strong.</summary>
        public void SetScreenGlow(int level)
        {
            Screen.GlowLevel = level;
            Settings.ScreenGlow = Screen.GlowLevel;
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
