using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TheMovies.Core.Network
{
    /// <summary>A Plex Media Server of the account, with its connection addresses in the order to try.</summary>
    public sealed class PlexServer
    {
        public string Name;
        public string Id;
        public string Token;
        public List<string> Connections = new List<string>();
    }

    /// <summary>Plex: login by code (plex.tv/link), the account's servers, libraries and items.</summary>
    public static class PlexServers
    {
        public const string AccountAddress = "https://plex.tv/";
        public const string LinkPage = "plex.tv/link";
        public const int DiscoveryPort = 32414;

        /// <summary>Identifies this headset to Plex. Set by the app at start.</summary>
        public static string ClientId = "themovies-" + Environment.MachineName;

        public static NetworkSource NewAccountSource() =>
            new NetworkSource { Kind = NetworkSourceKind.Plex, Name = "Plex", Address = AccountAddress, User = "plex" };

        public static HttpClient CreateClient(string token = null)
        {
            var http = NetHttp.CreateClient();
            var h = http.DefaultRequestHeaders;
            h.TryAddWithoutValidation("X-Plex-Product", "The Movies");
            h.TryAddWithoutValidation("X-Plex-Version", "1.0");
            h.TryAddWithoutValidation("X-Plex-Platform", "Android");
            h.TryAddWithoutValidation("X-Plex-Device", "Meta Quest");
            h.TryAddWithoutValidation("X-Plex-Device-Name", "The Movies (Quest)");
            h.TryAddWithoutValidation("X-Plex-Client-Identifier", ClientId);
            if (!string.IsNullOrEmpty(token)) h.TryAddWithoutValidation("X-Plex-Token", token);
            return http;
        }

        // ---------------- Login by code ----------------

        /// <summary>Asks plex.tv for a 4-character code the user enters on plex.tv/link.</summary>
        public static (long id, string code) CreatePin(HttpClient http)
        {
            var json = NetHttp.Json(http, "https://plex.tv/api/v2/pins?strong=false", "{}");
            return (json.Long("id"), json.Str("code"));
        }

        /// <summary>The account token once the code was entered, null while waiting.</summary>
        public static string CheckPin(HttpClient http, long id)
        {
            string token = NetHttp.Json(http, "https://plex.tv/api/v2/pins/" + id).Str("authToken");
            return token.Length > 0 ? token : null;
        }

        // ---------------- Servers ----------------

        /// <summary>
        /// Servers of /api/v2/resources. Connection order: local over plain HTTP (fast, works without internet),
        /// local over HTTPS (plex.direct), remote, relay last.
        /// </summary>
        public static List<PlexServer> ParseResources(object json)
        {
            var result = new List<PlexServer>();
            foreach (var r in json.Array())
            {
                if (r.Str("provides").IndexOf("server", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var server = new PlexServer { Name = r.Str("name", "Plex"), Id = r.Str("clientIdentifier"), Token = r.Str("accessToken") };
                var local = new List<string>();
                var localHttps = new List<string>();
                var remote = new List<string>();
                var relay = new List<string>();
                foreach (var c in r.Array("connections"))
                {
                    string uri = c.Str("uri").TrimEnd('/');
                    if (c.Bool("relay"))
                    {
                        relay.Add(uri);
                        continue;
                    }
                    if (c.Bool("local"))
                    {
                        string address = c.Str("address");
                        long port = c.Long("port", 32400);
                        if (address.Length > 0 && !c.Bool("IPv6") && address.IndexOf(':') < 0)
                            local.Add("http://" + address + ":" + port);
                        localHttps.Add(uri);
                    }
                    else
                    {
                        remote.Add(uri);
                    }
                }
                foreach (var list in new[] { local, localHttps, remote, relay })
                    foreach (string u in list)
                        if (u.Length > 0 && !server.Connections.Contains(u))
                            server.Connections.Add(u);
                if (server.Id.Length > 0 && server.Connections.Count > 0) result.Add(server);
            }
            return result;
        }

        /// <summary>Movie and TV libraries of /library/sections.</summary>
        public static List<NetworkItem> ParseSections(object json, string serverId)
        {
            var result = new List<NetworkItem>();
            foreach (var d in json.Path("MediaContainer").Array("Directory"))
            {
                string type = d.Str("type");
                if (type != "movie" && type != "show") continue;
                result.Add(new NetworkItem
                {
                    Name = d.Str("title", "?"),
                    Location = serverId + "|/library/sections/" + d.Str("key") + "/all",
                    IsFolder = true,
                });
            }
            return result;
        }

        /// <summary>Metadata list (library, show, season, search): items with media are files, the rest folders.</summary>
        public static List<NetworkItem> ParseMetadata(IEnumerable<object> metadata, string serverId, string baseUrl, string token)
        {
            var result = new List<NetworkItem>();
            foreach (var m in metadata)
            {
                string type = m.Str("type");
                if (type == "artist" || type == "album" || type == "track" || type == "photo") continue;
                string title = m.Str("title", "?");
                int year = (int)m.Long("year");
                string poster = PosterUrl(baseUrl, m.Str("thumb"), token);
                if (type == "episode")
                {
                    long number = m.Long("index", -1);
                    long season = m.Long("parentIndex", -1);
                    if (number >= 0)
                        title = (season >= 0 ? "S" + season.ToString("00") + "E" : "Folge ") + number.ToString("00") + " · " + title;
                }
                var media = m.Array("Media");
                if (media.Count == 0)
                {
                    string key = m.Str("key");
                    if (key.Length == 0) continue;
                    result.Add(new NetworkItem { Name = title, Location = serverId + "|" + key, IsFolder = true, Poster = poster, Year = year });
                    continue;
                }
                var parts = media[0].Array("Part");
                if (parts.Count == 0) continue;
                var part = parts[0];
                string partKey = part.Str("key");
                if (partKey.Length == 0) continue;
                result.Add(new NetworkItem
                {
                    Name = Jellyfin.FileName(part.Str("file"), m.Str("title", "Film"), part.Str("container", media[0].Str("container"))),
                    Title = title,
                    Year = year,
                    Size = part.Long("size"),
                    Location = baseUrl + partKey + (partKey.IndexOf('?') < 0 ? "?" : "&") + "X-Plex-Token=" + Uri.EscapeDataString(token ?? string.Empty),
                    Poster = poster,
                });
            }
            return result;
        }

        public static List<NetworkItem> ParseSearch(object json, string serverId, string baseUrl, string token)
        {
            var all = new List<object>();
            foreach (var hub in json.Path("MediaContainer").Array("Hub"))
                all.AddRange(hub.Array("Metadata"));
            all.AddRange(json.Path("MediaContainer").Array("Metadata"));
            return ParseMetadata(all, serverId, baseUrl, token);
        }

        public static string PosterUrl(string baseUrl, string thumb, string token)
        {
            if (string.IsNullOrEmpty(thumb)) return null;
            return baseUrl + "/photo/:/transcode?width=240&height=360&minSize=1&upscale=1&url=" + Uri.EscapeDataString(thumb) +
                   "&X-Plex-Token=" + Uri.EscapeDataString(token ?? string.Empty);
        }

        // ---------------- Discovery (GDM) ----------------

        /// <summary>Name of a server from a GDM answer, null if it is no media server.</summary>
        public static string ParseGdmAnswer(string text)
        {
            string name = null;
            bool server = false;
            foreach (string line in text.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) name = value;
                else if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && value.IndexOf("media-server", StringComparison.OrdinalIgnoreCase) >= 0) server = true;
            }
            return server ? name ?? "Plex" : null;
        }

        /// <summary>Looks for Plex servers in the local network; returns their names.</summary>
        public static List<string> Discover(int timeoutMs)
        {
            var names = new List<string>();
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 300;
                byte[] search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\n\r\n");
                foreach (var target in new[] { new IPEndPoint(IPAddress.Parse("239.0.0.250"), DiscoveryPort), new IPEndPoint(IPAddress.Broadcast, DiscoveryPort) })
                {
                    try { udp.Send(search, search.Length, target); }
                    catch (SocketException) { }
                }
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        string name = ParseGdmAnswer(Encoding.UTF8.GetString(udp.Receive(ref from)));
                        if (name != null && !names.Contains(name)) names.Add(name);
                    }
                    catch (SocketException)
                    {
                        // Receive timeout: loop on
                    }
                }
            }
            return names;
        }
    }

    /// <summary>
    /// Browses all servers of a Plex account. Locations: "" (servers, or the libraries if there is only one),
    /// "serverId|" (libraries of a server), "serverId|/library/…" (a library, show or season).
    /// </summary>
    sealed class PlexBrowser : INetworkBrowser, ISearchableBrowser
    {
        readonly HttpClient m_Account;
        readonly Dictionary<string, HttpClient> m_ServerHttp = new Dictionary<string, HttpClient>();
        readonly Dictionary<string, string> m_BaseUrls = new Dictionary<string, string>();
        List<PlexServer> m_Servers;

        public PlexBrowser(NetworkSource source)
        {
            Source = source;
            m_Account = PlexServers.CreateClient(source.Password);
        }

        public NetworkSource Source { get; }

        List<PlexServer> Servers()
        {
            if (m_Servers != null) return m_Servers;
            if (string.IsNullOrEmpty(Source.Password)) throw new IOException("Anmeldung abgelehnt (bitte mit Plex verbinden).");
            var servers = PlexServers.ParseResources(NetHttp.Json(m_Account, "https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1"));
            if (servers.Count == 0) throw new IOException("Zu diesem Plex-Konto gehört kein Medienserver.");
            m_Servers = servers;
            return servers;
        }

        PlexServer Server(string id)
        {
            foreach (var s in Servers())
                if (s.Id == id) return s;
            throw new IOException("Dieser Plex-Server gehört nicht mehr zum Konto.");
        }

        /// <summary>Picks the first connection of a server that answers (tried in parallel, earlier ones preferred).</summary>
        string BaseUrl(PlexServer server, out HttpClient http)
        {
            if (!m_ServerHttp.TryGetValue(server.Id, out http))
                m_ServerHttp[server.Id] = http = PlexServers.CreateClient(server.Token);
            if (m_BaseUrls.TryGetValue(server.Id, out string known)) return known;

            var client = http;
            var probes = new List<Task<bool>>();
            foreach (string url in server.Connections)
            {
                probes.Add(Task.Run(() =>
                {
                    try
                    {
                        using (var cts = new System.Threading.CancellationTokenSource(4000))
                        using (var response = client.GetAsync(url + "/identity", cts.Token).GetAwaiter().GetResult())
                            return response.IsSuccessStatusCode;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }));
            }
            // Wait for the preferred ones in order, so a local address wins over a relay that answers sooner.
            for (int i = 0; i < probes.Count; i++)
            {
                if (!probes[i].Result) continue;
                m_BaseUrls[server.Id] = server.Connections[i];
                return server.Connections[i];
            }
            throw new IOException($"Der Plex-Server „{server.Name}“ ist nicht erreichbar.");
        }

        public List<NetworkItem> List(string location)
        {
            var servers = Servers();
            if (string.IsNullOrEmpty(location))
            {
                if (servers.Count == 1) return List(servers[0].Id + "|");
                var result = new List<NetworkItem>();
                foreach (var s in servers)
                    result.Add(new NetworkItem { Name = s.Name, Location = s.Id + "|", IsFolder = true });
                return result;
            }
            int bar = location.IndexOf('|');
            if (bar < 0) throw new IOException("Ungültiger Plex-Ort.");
            var server = Server(location.Substring(0, bar));
            string path = location.Substring(bar + 1);
            string baseUrl = BaseUrl(server, out var http);
            if (path.Length == 0)
                return PlexServers.ParseSections(NetHttp.Json(http, baseUrl + "/library/sections"), server.Id);
            var json = NetHttp.Json(http, baseUrl + path);
            return PlexServers.ParseMetadata(json.Path("MediaContainer").Array("Metadata"), server.Id, baseUrl, server.Token);
        }

        public List<NetworkItem> Search(string text)
        {
            var result = new List<NetworkItem>();
            foreach (var server in Servers())
            {
                try
                {
                    string baseUrl = BaseUrl(server, out var http);
                    var json = NetHttp.Json(http, baseUrl + "/hubs/search?limit=50&query=" + Uri.EscapeDataString(text));
                    result.AddRange(PlexServers.ParseSearch(json, server.Id, baseUrl, server.Token));
                }
                catch (IOException) when (Servers().Count > 1)
                {
                    // Skip a server that is offline
                }
            }
            return result;
        }

        public byte[] ReadSmallFile(string location, int maxBytes) =>
            MediaMatcher.LooksLikeUrl(location) ? NetHttp.ReadSmall(m_Account, location, maxBytes) : null;

        public IRandomAccessSource OpenFile(string location) => NetworkBrowser.OpenFile(Source, location);

        public string FolderOf(string location) => null;

        public string DirectUrl(string location) => NetworkBrowser.PlayableDirectly(location) ? location : null;

        public void Dispose()
        {
            m_Account.Dispose();
            foreach (var h in m_ServerHttp.Values) h.Dispose();
            m_ServerHttp.Clear();
        }
    }
}
