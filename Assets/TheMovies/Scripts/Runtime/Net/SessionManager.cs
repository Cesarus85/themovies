using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using TheMovies.Core;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if THEMOVIES_UGS
using Unity.Services.Authentication;
using Unity.Services.Core;
#endif
#if THEMOVIES_MPS
using Unity.Services.Multiplayer;
#endif

namespace TheMovies.Net
{
    public enum ConnectionState
    {
        Offline,
        Connecting,
        Connected,
    }

    public enum SessionKind
    {
        None,
        Online,
        Lan,
    }

    /// <summary>
    /// Opens and joins cinemas. "Online" uses Unity Multiplayer Services (Relay, joined by a short room
    /// code, works across the internet without port forwarding). "LAN" connects directly by IP address.
    /// The host spawns the shared <see cref="CinemaSync"/> and a <see cref="NetworkPlayer"/> per participant.
    /// Online cinemas survive the host leaving (host migration): the session makes another participant host,
    /// who opens a new Relay connection; meanwhile everybody keeps watching on their own and then resyncs.
    /// </summary>
    public sealed class SessionManager : MonoBehaviour
    {
        public const int MaxParticipants = 4;
        public const ushort LanPort = 7777;

        // A host that leaves on purpose is replaced within seconds; one whose headset crashed only once the
        // session service notices (up to a minute or two).
        const float k_MigrationTimeout = 150f;

        public static SessionManager Instance { get; private set; }

        public event Action Changed;

        [SerializeField] GameObject m_PlayerPrefab;
        [SerializeField] GameObject m_CinemaSyncPrefab;

        NetworkManager m_Network;
        bool m_Hooked;
        bool m_LeavingOnPurpose;
        bool m_Migrating;
        float m_MigrationDeadline;
        readonly Dictionary<ulong, int> m_RequestedSeats = new Dictionary<ulong, int>();
#if THEMOVIES_MPS
        ISession m_Session;
#endif

        public ConnectionState State { get; private set; } = ConnectionState.Offline;
        public SessionKind Kind { get; private set; }

        /// <summary>Room code (online) or IP address (LAN) others need to join.</summary>
        public string RoomCode { get; private set; } = string.Empty;

        /// <summary>Human readable status or error message.</summary>
        public string Status { get; private set; } = string.Empty;

        public bool IsHost => m_Network != null && m_Network.IsHost;

        /// <summary>
        /// The cinema appears in the "Offene Kinos" list (online) or answers searches in the local network (LAN).
        /// New cinemas are private: only by code, IP address or invitation.
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>The list shows what is playing (only if open; off for every new cinema).</summary>
        public bool ShowTitle { get; private set; }

        /// <summary>What an open cinema tells the others: host name, room and (if shown) the movie.</summary>
        public struct ListingInfo
        {
            public string HostName;
            public string Room;
            public string Title;
        }

        /// <summary>Set by the app; read by the host of an open cinema.</summary>
        public Func<ListingInfo> ListingProvider;

        /// <summary>Players in this cinema (for the LAN answer).</summary>
        public int PlayerCount => m_Network != null && m_Network.IsServer ? m_Network.ConnectedClientsIds.Count : 0;

        /// <summary>The host went away and a new one is taking over (the movie keeps running locally).</summary>
        public bool IsMigrating => m_Migrating;

        public static bool OnlineAvailable
        {
            get
            {
#if THEMOVIES_MPS && THEMOVIES_UGS
                return true;
#else
                return false;
#endif
            }
        }

        public void Configure(GameObject playerPrefab, GameObject cinemaSyncPrefab)
        {
            m_PlayerPrefab = playerPrefab;
            m_CinemaSyncPrefab = cinemaSyncPrefab;
        }

        void Awake()
        {
            Instance = this;
            NetworkPlayer.RequestedSeatProvider = clientId => m_RequestedSeats.TryGetValue(clientId, out int seat) ? seat : -1;
        }

        void Update()
        {
            if (m_Migrating && Time.unscaledTime > m_MigrationDeadline)
            {
                m_Migrating = false;
                HandleLost("Kein neuer Gastgeber gefunden – das Kino ist beendet.");
            }
#if THEMOVIES_MPS
            if (Time.unscaledTime >= m_NextListingUpdate)
            {
                m_NextListingUpdate = Time.unscaledTime + 3f;
                UpdateOnlineListing();
            }
#endif
        }

        /// <summary>Makes the cinema findable in the list (or private again). Host only.</summary>
        public async void SetOpen(bool open)
        {
            if (!IsHost || State != ConnectionState.Connected || open == IsOpen) return;
            IsOpen = open;
            if (!open) ShowTitle = false;
            Changed?.Invoke();
#if THEMOVIES_MPS
            if (Kind == SessionKind.Online && m_Session != null && m_Session.IsHost)
            {
                try
                {
                    var host = m_Session.AsHost();
                    host.IsPrivate = !open;
                    ApplyListing(host, true);
                    await host.SavePropertiesAsync();
                    Debug.Log($"[TheMovies] Cinema is now {(open ? "open" : "private")}.");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TheMovies] Changing the cinema's visibility failed: {e.Message}");
                    IsOpen = !open;
                    SetStatus("Sichtbarkeit konnte nicht geändert werden.");
                }
            }
#else
            await Task.Yield();
#endif
        }

        public void SetShowTitle(bool show)
        {
            if (!IsHost || (show && !IsOpen) || show == ShowTitle) return;
            ShowTitle = show;
            Changed?.Invoke();
#if THEMOVIES_MPS
            m_NextListingUpdate = 0;
#endif
        }

        void Start() => Hook();

        void OnDestroy()
        {
            Unhook();
            if (Instance == this) Instance = null;
        }

        bool Hook()
        {
            if (m_Hooked) return true;
            m_Network = NetworkManager.Singleton != null ? NetworkManager.Singleton : FindFirstObjectByType<NetworkManager>();
            if (m_Network == null)
            {
                SetStatus("Kein NetworkManager in der Szene. Bitte im Editor 'The Movies > Projekt einrichten' ausführen.");
                return false;
            }

            m_Network.NetworkConfig.ConnectionApproval = true;
            m_Network.ConnectionApprovalCallback = ApproveConnection;
            m_Network.OnServerStarted += OnServerStarted;
            m_Network.OnClientConnectedCallback += OnClientConnected;
            m_Network.OnClientDisconnectCallback += OnClientDisconnected;
            m_Network.OnTransportFailure += OnTransportFailure;
            m_Hooked = true;
            return true;
        }

        void Unhook()
        {
            if (!m_Hooked || m_Network == null) return;
            m_Network.ConnectionApprovalCallback = null;
            m_Network.OnServerStarted -= OnServerStarted;
            m_Network.OnClientConnectedCallback -= OnClientConnected;
            m_Network.OnClientDisconnectCallback -= OnClientDisconnected;
            m_Network.OnTransportFailure -= OnTransportFailure;
            m_Hooked = false;
        }

        // ---------------- Public API ----------------

        public async void HostOnline()
        {
            if (!BeginConnecting(SessionKind.Online, "Kino wird eröffnet …")) return;
#if THEMOVIES_MPS && THEMOVIES_UGS
            try
            {
                await EnsureSignedIn();
                var options = new SessionOptions
                {
                    Name = "TheMovies-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    MaxPlayers = MaxParticipants,
                    IsPrivate = true,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        // Indexed, so the list only shows cinemas of compatible app versions.
                        [k_PropProtocol] = new SessionProperty(k_ListingProtocol, VisibilityPropertyOptions.Public, PropertyIndex.String1),
                    },
                }.WithRelayNetwork().WithHostMigration(new MigrationData());
                m_Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                HookSession();
                if (AbandonIfCancelled()) return;
                RoomCode = m_Session.Code ?? string.Empty;
                // The session starts the NetworkManager as host; OnServerStarted finishes the setup.
                if (m_Network.IsListening) SetConnected();
            }
            catch (Exception e)
            {
                Fail("Kino konnte nicht eröffnet werden", e);
            }
#else
            await Task.Yield();
            Fail("Online-Modus nicht verfügbar: Paket 'Multiplayer Services' fehlt.", null);
#endif
        }

        public async void JoinOnline(string code)
        {
            code = TextUtil.NormalizeRoomCode(code);
            if (code.Length < 4)
            {
                SetStatus("Bitte einen gültigen Raum-Code eingeben.");
                return;
            }
            if (!BeginConnecting(SessionKind.Online, $"Trete Kino {code} bei …")) return;
#if THEMOVIES_MPS && THEMOVIES_UGS
            try
            {
                await EnsureSignedIn();
                m_Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                HookSession();
                if (AbandonIfCancelled()) return;
                RoomCode = code;
                // Connected once OnClientConnected fires for the local client.
                if (m_Network.IsConnectedClient) SetConnected();
            }
            catch (Exception e)
            {
                Fail("Beitritt fehlgeschlagen. Stimmt der Code?", e);
            }
#else
            await Task.Yield();
            Fail("Online-Modus nicht verfügbar: Paket 'Multiplayer Services' fehlt.", null);
#endif
        }

        /// <summary>Joins a cinema from the "Offene Kinos" list (online, no code needed).</summary>
        public async void JoinOnlineById(string sessionId, string label)
        {
            if (!BeginConnecting(SessionKind.Online, $"Trete {label} bei …")) return;
#if THEMOVIES_MPS && THEMOVIES_UGS
            try
            {
                await EnsureSignedIn();
                m_Session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
                HookSession();
                if (AbandonIfCancelled()) return;
                RoomCode = m_Session.Code ?? string.Empty;
                if (m_Network.IsConnectedClient) SetConnected();
            }
            catch (Exception e)
            {
                Fail("Beitritt fehlgeschlagen – das Kino ist vielleicht schon voll oder geschlossen", e);
            }
#else
            await Task.Yield();
            Fail("Online-Modus nicht verfügbar: Paket 'Multiplayer Services' fehlt.", null);
#endif
        }

        static bool s_QueryFailureLogged;

        /// <summary>Open online cinemas of other people (empty without Unity Services). Never throws.</summary>
        public async Task<List<CinemaListing>> QueryOpenCinemas()
        {
            var result = new List<CinemaListing>();
#if THEMOVIES_MPS && THEMOVIES_UGS
            try
            {
                await EnsureSignedIn();
                var options = new QuerySessionsOptions
                {
                    Count = 20,
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater),
                        new FilterOption(FilterField.StringIndex1, k_ListingProtocol, FilterOperation.Equal),
                    },
                };
                var found = await MultiplayerService.Instance.QuerySessionsAsync(options);
                string own = m_Session?.Id;
                foreach (var info in found.Sessions)
                {
                    if (info.Id == own) continue;
                    result.Add(new CinemaListing
                    {
                        Kind = ListingKind.Online,
                        Id = info.Id,
                        HostName = Property(info.Properties, k_PropHost, "Gast"),
                        Room = Property(info.Properties, k_PropRoom, string.Empty),
                        Title = Property(info.Properties, k_PropTitle, string.Empty),
                        Players = info.MaxPlayers - info.AvailableSlots,
                        MaxPlayers = info.MaxPlayers,
                        SeenAt = Time.realtimeSinceStartupAsDouble,
                    });
                }
            }
            catch (Exception e)
            {
                // Searched every few seconds while the main menu is open: report a failure only once.
                if (!s_QueryFailureLogged) Debug.LogWarning($"[TheMovies] Looking for open cinemas failed: {e.Message}");
                s_QueryFailureLogged = true;
            }
#else
            await Task.Yield();
#endif
            return result;
        }

        public void HostLan()
        {
            if (!BeginConnecting(SessionKind.Lan, "LAN-Kino wird eröffnet …")) return;
            var transport = Transport;
            if (transport == null) return;
            transport.SetConnectionData("127.0.0.1", LanPort, "0.0.0.0");
            RoomCode = LocalIPv4() ?? "?";
            if (!m_Network.StartHost())
                Fail($"Konnte Port {LanPort} nicht öffnen.", null);
        }

        public void JoinLan(string host)
        {
            host = (host ?? string.Empty).Trim();
            if (!TextUtil.IsPlausibleHost(host))
            {
                SetStatus("Bitte eine gültige IP-Adresse eingeben.");
                return;
            }
            if (!BeginConnecting(SessionKind.Lan, $"Verbinde mit {host} …")) return;
            var transport = Transport;
            if (transport == null) return;
            transport.SetConnectionData(host, LanPort);
            RoomCode = host;
            if (!m_Network.StartClient())
                Fail("Verbindung konnte nicht gestartet werden.", null);
        }

        public async void Leave()
        {
            if (State == ConnectionState.Offline) return;
            bool othersStay = false;
#if THEMOVIES_MPS
            othersStay = m_Session != null && m_Session.IsHost && m_Session.PlayerCount > 1;
#endif
            m_LeavingOnPurpose = true;
            await Shutdown();
            m_LeavingOnPurpose = false;
            ForgetSeat();
            SetOffline(othersStay ? "Kino verlassen – die anderen schauen mit neuem Gastgeber weiter." : "Kino verlassen.");
        }

        // ---------------- Internals ----------------

        UnityTransport Transport
        {
            get
            {
                var t = m_Network.NetworkConfig.NetworkTransport as UnityTransport;
                if (t == null) t = m_Network.GetComponent<UnityTransport>();
                if (t == null) Fail("UnityTransport fehlt am NetworkManager.", null);
                return t;
            }
        }

        bool BeginConnecting(SessionKind kind, string message)
        {
            if (!Hook()) return false;
            if (State != ConnectionState.Offline)
            {
                SetStatus("Bitte zuerst das aktuelle Kino verlassen.");
                return false;
            }
            if (m_Network.IsListening)
                m_Network.Shutdown();
            m_RequestedSeats.Clear();
            PrepareConnectionData();
            IsOpen = false;
            ShowTitle = false;
            Kind = kind;
            RoomCode = string.Empty;
            State = ConnectionState.Connecting;
            SetStatus(message);
            return true;
        }

        void SetConnected()
        {
            bool migrated = m_Migrating;
            m_Migrating = false;
            State = ConnectionState.Connected;
            if (migrated)
                Debug.Log($"[TheMovies] Host migration done, this device is {(IsHost ? "the new host" : "connected to the new host")}.");
            if (!IsHost) CinemaSync.ContinueNextSession(null);
            SetStatus(IsHost
                ? (migrated ? $"Du bist jetzt Gastgeber. Raum-Code: {RoomCode}"
                    : Kind == SessionKind.Online ? $"Kino offen. Raum-Code: {RoomCode}" : $"LAN-Kino offen auf {RoomCode}")
                : migrated ? "Weiter mit neuem Gastgeber." : "Verbunden.");
        }

        /// <summary>Tells the host which seat this device had (see <see cref="NetworkPlayer.PreferredSeat"/>).</summary>
        void PrepareConnectionData()
        {
            if (m_Network != null)
                m_Network.NetworkConfig.ConnectionData = new[] { (byte)(NetworkPlayer.PreferredSeat + 1) };
        }

        static void ForgetSeat()
        {
            NetworkPlayer.PreferredSeat = -1;
            CinemaSync.ForgetLast();
        }

        /// <summary>
        /// The connection to the host broke: in an online cinema the session picks a new host. Until the network is
        /// back, everybody keeps watching on their own (from <see cref="CinemaSync.Last"/>). False if there is nothing
        /// to wait for (LAN, never connected, leaving).
        /// </summary>
        bool TryWaitForNewHost()
        {
#if THEMOVIES_MPS
            if (m_Migrating) return true;
            if (Kind != SessionKind.Online || m_Session == null || m_LeavingOnPurpose || State != ConnectionState.Connected ||
                m_Session.State != SessionState.Connected)
                return false;
            m_Migrating = true;
            m_MigrationDeadline = Time.unscaledTime + k_MigrationTimeout;
            var last = CinemaSync.Last;
            if (last.Valid)
            {
                LocalAuthority.Instance.Import(last.Media, last.PositionAt(Time.realtimeSinceStartupAsDouble), last.Playing);
                CinemaSync.ContinueNextSession(last.VoiceChannel);
            }
            m_RequestedSeats.Clear();
            PrepareConnectionData();
            State = ConnectionState.Connecting;
            Debug.Log("[TheMovies] Host gone, waiting for the session to pick a new host.");
            SetStatus("Der Gastgeber ist weg – ein neuer übernimmt das Kino …");
            return true;
#else
            return false;
#endif
        }

        void SetOffline(string message)
        {
            State = ConnectionState.Offline;
            Kind = SessionKind.None;
            RoomCode = string.Empty;
            SetStatus(message);
        }

        void SetStatus(string message)
        {
            Status = message ?? string.Empty;
            Changed?.Invoke();
        }

        async void Fail(string message, Exception e)
        {
            if (e != null) Debug.LogException(e);
            string detail = e != null ? $"\n{TextUtil.Ellipsize(e.Message, 90)}" : string.Empty;
            m_LeavingOnPurpose = true;
            await Shutdown();
            m_LeavingOnPurpose = false;
            SetOffline(message + detail);
        }

        async Task Shutdown()
        {
            m_Migrating = false;
            if (m_Network != null && m_Network.IsListening)
                m_Network.Shutdown();
#if THEMOVIES_MPS
            var session = m_Session;
            m_Session = null;
            if (session != null)
            {
                UnhookSession(session);
                try
                {
                    // A host with company only leaves: the session makes someone else host (host migration).
                    if (session.IsHost && session.PlayerCount <= 1)
                        await session.AsHost().DeleteAsync();
                    else
                        await session.LeaveAsync();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TheMovies] Leaving session failed: {e.Message}");
                }
            }
#else
            await Task.Yield();
#endif
        }

#if THEMOVIES_MPS
        /// <summary>The connection failed or was left while the session request was still running.</summary>
        bool AbandonIfCancelled()
        {
            if (State != ConnectionState.Offline) return false;
            _ = Shutdown();
            return true;
        }

        void HookSession()
        {
            m_Session.RemovedFromSession += OnRemovedFromSession;
            m_Session.Deleted += OnSessionDeleted;
            m_Session.SessionHostChanged += OnSessionHostChanged;
            m_Session.SessionMigrated += OnSessionMigrated;
            if (m_Session.Network != null) m_Session.Network.MigrationFailed += OnMigrationFailed;
        }

        void UnhookSession(ISession session)
        {
            session.RemovedFromSession -= OnRemovedFromSession;
            session.Deleted -= OnSessionDeleted;
            session.SessionHostChanged -= OnSessionHostChanged;
            session.SessionMigrated -= OnSessionMigrated;
            if (session.Network != null) session.Network.MigrationFailed -= OnMigrationFailed;
        }

        void OnRemovedFromSession() => HandleLost("Du wurdest aus dem Kino entfernt.");

        void OnSessionDeleted() => HandleLost("Der Gastgeber hat das Kino geschlossen.");

        void OnSessionHostChanged(string newHostId)
        {
            bool me = m_Session != null && m_Session.CurrentPlayer != null && newHostId == m_Session.CurrentPlayer.Id;
            Debug.Log($"[TheMovies] Session host changed to {(me ? "this device" : newHostId)}.");
            // Often arrives before the network notices that the old host is gone.
            if (!TryWaitForNewHost()) return;
            if (me) SetStatus("Du übernimmst das Kino als Gastgeber …");
        }

        void OnSessionMigrated()
        {
            Debug.Log("[TheMovies] Session migrated to the new host.");
            if (m_Session != null && !string.IsNullOrEmpty(m_Session.Code)) RoomCode = m_Session.Code;
            if (m_Session != null && m_Session.IsHost)
            {
                // The cinema stays as open as it was; the movie title only if the new host shows it again.
                IsOpen = !m_Session.IsPrivate;
                ShowTitle = false;
                m_PushedListing = null;
                m_NextListingUpdate = 0;
            }
            if (m_Migrating && m_Network.IsListening && (m_Network.IsHost || m_Network.IsConnectedClient)) SetConnected();
            else Changed?.Invoke();
        }

        void OnMigrationFailed(SessionError error)
        {
            Debug.LogWarning($"[TheMovies] Host migration failed: {error}");
            m_Migrating = false;
            HandleLost("Der Gastgeber-Wechsel hat nicht geklappt – das Kino ist beendet.");
        }

        const string k_ListingProtocol = "tm1";
        const string k_PropProtocol = "proto";
        const string k_PropHost = "host";
        const string k_PropRoom = "room";
        const string k_PropTitle = "title";

        float m_NextListingUpdate;
        string m_PushedListing;
        bool m_PushingListing;

        static string Property(IReadOnlyDictionary<string, SessionProperty> properties, string key, string fallback) =>
            properties != null && properties.TryGetValue(key, out var p) && !string.IsNullOrEmpty(p?.Value) ? p.Value : fallback;

        /// <summary>Host of an open online cinema: keeps name, room and (if shown) title in the list up to date.</summary>
        async void UpdateOnlineListing()
        {
            if (m_PushingListing || Kind != SessionKind.Online || State != ConnectionState.Connected || !IsOpen ||
                m_Session == null || !m_Session.IsHost)
                return;
            var host = m_Session.AsHost();
            if (!ApplyListing(host, false)) return;
            m_PushingListing = true;
            try
            {
                await host.SavePropertiesAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Updating the cinema list entry failed: {e.Message}");
                m_PushedListing = null;
            }
            m_PushingListing = false;
        }

        /// <summary>Puts the listing into the session properties; false if nothing changed.</summary>
        bool ApplyListing(IHostSession host, bool force)
        {
            var info = ListingProvider?.Invoke() ?? default;
            string title = ShowTitle ? info.Title ?? string.Empty : string.Empty;
            string key = info.HostName + "\n" + info.Room + "\n" + title;
            if (!force && key == m_PushedListing) return false;
            m_PushedListing = key;
            host.SetProperty(k_PropHost, new SessionProperty(Limit(info.HostName, 40), VisibilityPropertyOptions.Public));
            host.SetProperty(k_PropRoom, new SessionProperty(Limit(info.Room, 40), VisibilityPropertyOptions.Public));
            host.SetProperty(k_PropTitle, new SessionProperty(Limit(title, 80), VisibilityPropertyOptions.Public));
            return true;
        }

        static string Limit(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
        }

        /// <summary>
        /// Session state for a new host, uploaded by the host every few seconds. Only a fallback: participants keep
        /// their own, more current copy (<see cref="CinemaSync.Last"/>), which the new host prefers.
        /// </summary>
        sealed class MigrationData : IMigrationDataHandler
        {
            const int k_Format = 1;

            public byte[] Generate()
            {
                var last = CinemaSync.Last;
                var m = last.Media;
                using (var stream = new MemoryStream())
                using (var w = new BinaryWriter(stream))
                {
                    w.Write(k_Format);
                    w.Write(m.Version);
                    w.Write(m.Key.ToString());
                    w.Write(m.Title.ToString());
                    w.Write(m.IsUrl);
                    w.Write(m.Size);
                    w.Write(m.Duration);
                    w.Write(m.Format);
                    w.Write(m.SwapEyes);
                    w.Write(m.HostStream);
                    w.Write(last.Valid ? last.PositionAt(Time.realtimeSinceStartupAsDouble) : 0.0);
                    w.Write(last.Playing);
                    w.Write(DateTime.UtcNow.Ticks);
                    w.Write(last.VoiceChannel ?? string.Empty);
                    w.Flush();
                    return stream.ToArray();
                }
            }

            public void Apply(byte[] data)
            {
                try
                {
                    using (var r = new BinaryReader(new MemoryStream(data)))
                    {
                        if (r.ReadInt32() != k_Format) return;
                        var media = new MediaDescriptor
                        {
                            Version = r.ReadInt32(),
                            Key = new Unity.Collections.FixedString512Bytes(r.ReadString()),
                            Title = new Unity.Collections.FixedString128Bytes(r.ReadString()),
                            IsUrl = r.ReadBoolean(),
                            Size = r.ReadInt64(),
                            Duration = r.ReadDouble(),
                            Format = r.ReadByte(),
                            SwapEyes = r.ReadBoolean(),
                            HostStream = r.ReadBoolean(),
                        };
                        double position = r.ReadDouble();
                        bool playing = r.ReadBoolean();
                        var saved = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
                        string voice = r.ReadString();
                        // This device's own copy (taken when the old host went away) is more current.
                        if (!LocalAuthority.Instance.Media.IsEmpty) return;
                        if (playing) position += Math.Max(0, (DateTime.UtcNow - saved).TotalSeconds);
                        LocalAuthority.Instance.Import(media, position, playing);
                        CinemaSync.ContinueNextSession(voice);
                        Debug.Log("[TheMovies] Host migration: continuing from the old host's data.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TheMovies] Host migration data unreadable: {e.Message}");
                }
            }
        }
#endif

#if THEMOVIES_UGS
        static async Task EnsureSignedIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                // Separate profiles let several instances on one computer (editor + build) sign in independently.
                options.SetProfile(AuthProfile());
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string AuthProfile()
        {
            int hash = (Application.dataPath ?? string.Empty).GetHashCode() & 0x7fffffff;
            return "tm" + (hash % 100000000).ToString();
        }
#endif

        /// <summary>Player id used for the voice chat (empty when Unity Services are not signed in).</summary>
        public static string ServicesPlayerId
        {
            get
            {
#if THEMOVIES_UGS
                try
                {
                    if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
                        return AuthenticationService.Instance.PlayerId ?? string.Empty;
                }
                catch (Exception)
                {
                    // Services not set up for this project.
                }
#endif
                return string.Empty;
            }
        }

        /// <summary>Signs in to Unity Services if possible (needed for voice chat in LAN mode). Never throws.</summary>
        public static async Task<bool> TrySignIn()
        {
#if THEMOVIES_UGS
            try
            {
                await EnsureSignedIn();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Unity Services sign-in failed: {e.Message}");
                return false;
            }
#else
            await Task.Yield();
            return false;
#endif
        }

        void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            bool isSelf = request.ClientNetworkId == m_Network.LocalClientId;
            var payload = request.Payload;
            m_RequestedSeats[request.ClientNetworkId] = payload != null && payload.Length > 0 ? payload[0] - 1 : -1;
            int connected = m_Network.ConnectedClientsIds.Count;
            response.Approved = isSelf || connected < MaxParticipants;
            response.CreatePlayerObject = false;
            response.Pending = false;
            if (!response.Approved)
                response.Reason = "Das Kino ist voll (maximal 4 Personen).";
        }

        void OnServerStarted()
        {
            if (m_CinemaSyncPrefab != null && CinemaSync.Instance == null)
            {
                var sync = Instantiate(m_CinemaSyncPrefab);
                sync.GetComponent<NetworkObject>().Spawn();
            }
            SpawnPlayer(m_Network.LocalClientId);
            SetConnected();
        }

        void OnClientConnected(ulong clientId)
        {
            if (m_Network.IsServer)
                SpawnPlayer(clientId);
            if (clientId == m_Network.LocalClientId)
                SetConnected();
            Changed?.Invoke();
        }

        void SpawnPlayer(ulong clientId)
        {
            if (m_PlayerPrefab == null)
            {
                Debug.LogError("[TheMovies] NetworkPlayer prefab missing. Run 'The Movies > Projekt einrichten'.");
                return;
            }
            if (m_Network.ConnectedClients.TryGetValue(clientId, out var client) && client.PlayerObject != null)
                return;
            var player = Instantiate(m_PlayerPrefab);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }

        void OnClientDisconnected(ulong clientId)
        {
            bool lostSelf = clientId == m_Network.LocalClientId || (!m_Network.IsServer && clientId == NetworkManager.ServerClientId);
            if (lostSelf && !m_LeavingOnPurpose)
            {
                if (TryWaitForNewHost()) return;
                string reason = m_Network.DisconnectReason;
                HandleLost(string.IsNullOrEmpty(reason)
                    ? (State == ConnectionState.Connecting ? "Verbindung fehlgeschlagen." : "Verbindung zum Kino verloren.")
                    : reason);
                return;
            }
            Changed?.Invoke();
        }

        void OnTransportFailure()
        {
            if (!m_LeavingOnPurpose && !TryWaitForNewHost())
                HandleLost("Netzwerkfehler – Verbindung getrennt.");
        }

        async void HandleLost(string message)
        {
            if (State == ConnectionState.Offline || m_LeavingOnPurpose) return;
            m_LeavingOnPurpose = true;
            await Shutdown();
            m_LeavingOnPurpose = false;
            ForgetSeat();
            SetOffline(message);
        }

        static string LocalIPv4()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(addr.Address))
                            return addr.Address.ToString();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] Could not determine local IP: {e.Message}");
            }

            try
            {
                // Fallback: the address the OS would use to reach the internet (no packets are sent).
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    socket.Connect("8.8.8.8", 65530);
                    if (socket.LocalEndPoint is IPEndPoint ep)
                        return ep.Address.ToString();
                }
            }
            catch (Exception)
            {
                // Offline
            }
            return null;
        }
    }
}
