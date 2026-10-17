using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using TheMovies.Core;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>
    /// The "Offene Kinos" list: open online cinemas (session list of Unity Multiplayer Services) and open LAN
    /// cinemas in the same network (<see cref="LanDiscovery"/>), searched only while the list is on screen.
    /// Also answers the searches of others while this device hosts an open LAN cinema.
    /// </summary>
    public sealed class CinemaFinder : MonoBehaviour
    {
        const float k_LanQueryInterval = 2f;
        const float k_OnlineQueryInterval = 8f;
        const double k_LanTimeout = 7.0;

        SessionManager m_Sessions;
        UdpClient m_Search;
        UdpClient m_Responder;
        float m_NextLanQuery;
        float m_NextOnlineQuery;
        bool m_OnlineBusy;
        bool m_LoggedSendError;
        float m_ResponderRetryAt;
        readonly Dictionary<string, CinemaListing> m_Lan = new Dictionary<string, CinemaListing>();
        List<CinemaListing> m_Online = new List<CinemaListing>();
        readonly List<CinemaListing> m_All = new List<CinemaListing>();
        HashSet<string> m_OwnAddresses;

        public static CinemaFinder Instance { get; private set; }

        /// <summary>Set while the list is visible: only then the network is searched.</summary>
        public bool Searching { get; set; }

        /// <summary>LAN cinemas first, then online ones.</summary>
        public IReadOnlyList<CinemaListing> Listings => m_All;

        /// <summary>True until the first online answer arrived (for "Suche …" instead of "nichts gefunden").</summary>
        public bool StillLooking { get; private set; } = true;

        public event Action Changed;

        public static CinemaFinder Create(SessionManager sessions)
        {
            var finder = new GameObject("Cinema Finder").AddComponent<CinemaFinder>();
            finder.m_Sessions = sessions;
            Instance = finder;
            return finder;
        }

        void OnDestroy()
        {
            CloseSearch();
            CloseResponder();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            bool offline = m_Sessions != null && m_Sessions.State == ConnectionState.Offline;
            UpdateResponder();

            if (!Searching || !offline)
            {
                if (m_Search != null) CloseSearch();
                return;
            }

            if (m_Search == null) OpenSearch();
            if (Time.unscaledTime >= m_NextLanQuery)
            {
                m_NextLanQuery = Time.unscaledTime + k_LanQueryInterval;
                SendLanQuery();
            }
            ReceiveAnswers();
            DropQuietLan();

            if (SessionManager.OnlineAvailable && !m_OnlineBusy && Time.unscaledTime >= m_NextOnlineQuery)
            {
                m_NextOnlineQuery = Time.unscaledTime + k_OnlineQueryInterval;
                QueryOnline();
            }
        }

        /// <summary>Searches again right away (e.g. when the main menu opens).</summary>
        public void Refresh()
        {
            m_NextLanQuery = 0;
            m_NextOnlineQuery = 0;
        }

        // ---------------- Online ----------------

        async void QueryOnline()
        {
            m_OnlineBusy = true;
            var found = await m_Sessions.QueryOpenCinemas();
            m_OnlineBusy = false;
            if (this == null) return;
            m_Online = found;
            StillLooking = false;
            Rebuild();
        }

        // ---------------- LAN search ----------------

        void OpenSearch()
        {
            try
            {
                m_Search = new UdpClient(0) { EnableBroadcast = true };
                m_OwnAddresses = OwnAddresses();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] LAN search not possible: {e.Message}");
                m_Search = null;
            }
        }

        void CloseSearch()
        {
            m_Search?.Close();
            m_Search = null;
            if (m_Lan.Count > 0)
            {
                m_Lan.Clear();
                Rebuild();
            }
        }

        void SendLanQuery()
        {
            if (m_Search == null) return;
            byte[] query = LanDiscovery.Query();
            foreach (var address in BroadcastAddresses())
            {
                try
                {
                    m_Search.Send(query, query.Length, new IPEndPoint(address, LanDiscovery.Port));
                }
                catch (Exception e)
                {
                    if (!m_LoggedSendError) Debug.LogWarning($"[TheMovies] LAN search to {address}: {e.Message}");
                    m_LoggedSendError = true;
                }
            }
        }

        void ReceiveAnswers()
        {
            if (m_Search == null) return;
            bool changed = false;
            try
            {
                while (m_Search.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = m_Search.Receive(ref from);
                    string address = from.Address.ToString();
                    if (m_OwnAddresses != null && m_OwnAddresses.Contains(address)) continue;
                    var listing = LanDiscovery.ParseAnswer(data, data.Length, address);
                    if (listing == null) continue;
                    listing.SeenAt = Time.realtimeSinceStartupAsDouble;
                    if (!m_Lan.ContainsKey(address))
                        Debug.Log($"[TheMovies] LAN cinema found at {address}.");
                    m_Lan[address] = listing;
                    changed = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] LAN search: {e.Message}");
            }
            if (changed) Rebuild();
        }

        void DropQuietLan()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            List<string> quiet = null;
            foreach (var pair in m_Lan)
                if (now - pair.Value.SeenAt > k_LanTimeout)
                    (quiet ??= new List<string>()).Add(pair.Key);
            if (quiet == null) return;
            foreach (string key in quiet) m_Lan.Remove(key);
            Rebuild();
        }

        // ---------------- LAN answers (host of an open LAN cinema) ----------------

        void UpdateResponder()
        {
            bool answer = m_Sessions != null && m_Sessions.Kind == SessionKind.Lan && m_Sessions.IsHost && m_Sessions.IsOpen &&
                          m_Sessions.State == ConnectionState.Connected;
            if (!answer)
            {
                if (m_Responder != null) CloseResponder();
                return;
            }
            if (m_Responder == null && (Time.unscaledTime < m_ResponderRetryAt || !OpenResponder())) return;

            try
            {
                while (m_Responder.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = m_Responder.Receive(ref from);
                    if (!LanDiscovery.IsQuery(data, data.Length)) continue;
                    var info = m_Sessions.ListingProvider?.Invoke() ?? default;
                    byte[] reply = LanDiscovery.Answer(info.HostName, info.Room, m_Sessions.PlayerCount, SessionManager.MaxParticipants,
                        m_Sessions.ShowTitle ? info.Title : null);
                    m_Responder.Send(reply, reply.Length, from);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] LAN answer: {e.Message}");
            }
        }

        bool OpenResponder()
        {
            try
            {
                var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscovery.Port));
                client.EnableBroadcast = true;
                m_Responder = client;
                Debug.Log($"[TheMovies] Open LAN cinema: answering searches on port {LanDiscovery.Port}.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TheMovies] LAN cinema cannot be announced (port {LanDiscovery.Port}): {e.Message}");
                m_ResponderRetryAt = Time.unscaledTime + 10f; // don't retry every frame
                return false;
            }
        }

        void CloseResponder()
        {
            m_Responder?.Close();
            m_Responder = null;
        }

        // ---------------- Helpers ----------------

        void Rebuild()
        {
            m_All.Clear();
            m_All.AddRange(m_Lan.Values);
            m_All.AddRange(m_Online);
            Changed?.Invoke();
        }

        /// <summary>The general broadcast address plus the one of each local network (some routers drop the first).</summary>
        static List<IPAddress> BroadcastAddresses()
        {
            var result = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address)) continue;
                        byte[] ip = unicast.Address.GetAddressBytes();
                        byte[] mask = Mask(unicast);
                        var broadcast = new byte[4];
                        for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                        var address = new IPAddress(broadcast);
                        if (!result.Contains(address)) result.Add(address);
                    }
                }
            }
            catch (Exception)
            {
                // Interfaces not readable on this platform: the general broadcast still works in most networks.
            }
            return result;
        }

        static byte[] Mask(UnicastIPAddressInformation unicast)
        {
            try
            {
                var mask = unicast.IPv4Mask;
                if (mask != null && !mask.Equals(IPAddress.Any)) return mask.GetAddressBytes();
            }
            catch (Exception)
            {
                // Not implemented on some Android versions.
            }
            return new byte[] { 255, 255, 255, 0 };
        }

        static HashSet<string> OwnAddresses()
        {
            var own = new HashSet<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                        own.Add(unicast.Address.ToString());
            }
            catch (Exception)
            {
                // Then an own open cinema may show up in the list; joining it fails harmlessly.
            }
            return own;
        }
    }
}
