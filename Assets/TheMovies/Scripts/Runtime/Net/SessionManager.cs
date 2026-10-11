using System;
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
    /// </summary>
    public sealed class SessionManager : MonoBehaviour
    {
        public const int MaxParticipants = 4;
        public const ushort LanPort = 7777;

        public static SessionManager Instance { get; private set; }

        public event Action Changed;

        [SerializeField] GameObject m_PlayerPrefab;
        [SerializeField] GameObject m_CinemaSyncPrefab;

        NetworkManager m_Network;
        bool m_Hooked;
        bool m_LeavingOnPurpose;
#if THEMOVIES_MPS
        ISession m_Session;
        bool m_SessionIsHost;
#endif

        public ConnectionState State { get; private set; } = ConnectionState.Offline;
        public SessionKind Kind { get; private set; }

        /// <summary>Room code (online) or IP address (LAN) others need to join.</summary>
        public string RoomCode { get; private set; } = string.Empty;

        /// <summary>Human readable status or error message.</summary>
        public string Status { get; private set; } = string.Empty;

        public bool IsHost => m_Network != null && m_Network.IsHost;

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
                }.WithRelayNetwork();
                m_Session = await MultiplayerService.Instance.CreateSessionAsync(options);
                m_SessionIsHost = true;
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
                m_SessionIsHost = false;
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
            m_LeavingOnPurpose = true;
            await Shutdown();
            m_LeavingOnPurpose = false;
            SetOffline("Kino verlassen.");
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
            Kind = kind;
            RoomCode = string.Empty;
            State = ConnectionState.Connecting;
            SetStatus(message);
            return true;
        }

        void SetConnected()
        {
            State = ConnectionState.Connected;
            SetStatus(IsHost
                ? (Kind == SessionKind.Online ? $"Kino offen. Raum-Code: {RoomCode}" : $"LAN-Kino offen auf {RoomCode}")
                : "Verbunden.");
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
                    if (m_SessionIsHost)
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
        }

        void UnhookSession(ISession session)
        {
            session.RemovedFromSession -= OnRemovedFromSession;
            session.Deleted -= OnSessionDeleted;
        }

        void OnRemovedFromSession() => HandleLost("Du wurdest aus dem Kino entfernt.");

        void OnSessionDeleted() => HandleLost("Der Gastgeber hat das Kino geschlossen.");
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
            if (!m_LeavingOnPurpose)
                HandleLost("Netzwerkfehler – Verbindung getrennt.");
        }

        async void HandleLost(string message)
        {
            if (State == ConnectionState.Offline || m_LeavingOnPurpose) return;
            m_LeavingOnPurpose = true;
            await Shutdown();
            m_LeavingOnPurpose = false;
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
