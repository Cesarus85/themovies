using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace TheMovies.Core.Network
{
    /// <summary>Jellyfin (and Emby) REST API: login, libraries, items, stream and poster addresses.</summary>
    public static class Jellyfin
    {
        public const int DefaultPort = 8096;
        public const int DefaultHttpsPort = 8920;
        public const int DiscoveryPort = 7359;
        const string ViewPrefix = "view:";

        /// <summary>Identifies this headset to the server (one session per device). Set by the app at start.</summary>
        public static string DeviceId = "themovies-" + Environment.MachineName;

        public static string AuthorizationValue(string token)
        {
            var sb = new StringBuilder("MediaBrowser Client=\"The Movies\", Device=\"Meta Quest\", DeviceId=\"")
                .Append(DeviceId).Append("\", Version=\"1.0\"");
            if (!string.IsNullOrEmpty(token)) sb.Append(", Token=\"").Append(token).Append('"');
            return sb.ToString();
        }

        public static string LoginBody(string user, string password) =>
            "{\"Username\":" + Json.Quote(user) + ",\"Pw\":" + Json.Quote(password) + "}";

        /// <summary>Answer of /Users/AuthenticateByName → access token and user id.</summary>
        public static bool ParseLogin(object json, out string token, out string userId)
        {
            token = json.Str("AccessToken");
            userId = json.Get("User").Str("Id");
            return token.Length > 0 && userId.Length > 0;
        }

        /// <summary>Answer of /System/Info/Public: true for Jellyfin and Emby servers.</summary>
        public static bool IsServerInfo(object json, out string name)
        {
            name = json.Str("ServerName");
            string product = json.Str("ProductName");
            return json.Str("Id").Length > 0 && (name.Length > 0 || product.Length > 0) &&
                   (product.Length == 0 || product.IndexOf("Jellyfin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    product.IndexOf("Emby", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Libraries of /Users/{id}/Views; music, photo and book libraries are left out.</summary>
        public static List<NetworkItem> ParseViews(object json, string baseUrl)
        {
            var result = new List<NetworkItem>();
            foreach (var view in json.Array("Items"))
            {
                string type = view.Str("CollectionType").ToLowerInvariant();
                switch (type)
                {
                    case "music":
                    case "photos":
                    case "books":
                    case "livetv":
                    case "playlists":
                    case "musicvideos":
                        continue;
                }
                string id = view.Str("Id");
                if (id.Length == 0) continue;
                result.Add(new NetworkItem
                {
                    Name = view.Str("Name", "?"),
                    Location = ViewPrefix + type + ":" + id,
                    IsFolder = true,
                    Poster = PosterUrl(baseUrl, view),
                });
            }
            return result;
        }

        /// <summary>Query for the children of a location (library or folder/series/season id).</summary>
        public static string ItemsQuery(string userId, string location)
        {
            const string fields = "&Fields=Path,MediaSources,ProductionYear&EnableImageTypes=Primary&Limit=3000";
            string parent = location;
            string query = "&SortBy=SortName&SortOrder=Ascending";
            if (location.StartsWith(ViewPrefix, StringComparison.Ordinal))
            {
                string rest = location.Substring(ViewPrefix.Length);
                int colon = rest.IndexOf(':');
                string type = colon < 0 ? string.Empty : rest.Substring(0, colon);
                parent = colon < 0 ? rest : rest.Substring(colon + 1);
                // Movie and series libraries list their titles directly (not the folders on disk).
                if (type == "movies") query += "&Recursive=true&IncludeItemTypes=Movie";
                else if (type == "tvshows") query += "&Recursive=true&IncludeItemTypes=Series";
            }
            return "Users/" + userId + "/Items?ParentId=" + Uri.EscapeDataString(parent) + query + fields;
        }

        public static string SearchQuery(string userId, string text) =>
            "Users/" + userId + "/Items?searchTerm=" + Uri.EscapeDataString(text) +
            "&Recursive=true&IncludeItemTypes=Movie,Series,Episode,Video&Limit=200&Fields=Path,MediaSources,ProductionYear&EnableImageTypes=Primary";

        /// <summary>Items answer: folders (series, seasons, collections) and playable videos.</summary>
        public static List<NetworkItem> ParseItems(object json, string baseUrl, string token)
        {
            var result = new List<NetworkItem>();
            foreach (var item in json.Array("Items"))
            {
                string id = item.Str("Id");
                if (id.Length == 0) continue;
                string title = item.Str("Name", "?");
                string type = item.Str("Type");
                int year = (int)item.Long("ProductionYear");
                if (type == "Episode")
                {
                    long number = item.Long("IndexNumber", -1);
                    long season = item.Long("ParentIndexNumber", -1);
                    if (number >= 0)
                        title = (season >= 0 ? "S" + season.ToString("00") + "E" : "Folge ") + number.ToString("00") + " · " + title;
                }
                if (item.Bool("IsFolder"))
                {
                    result.Add(new NetworkItem { Name = title, Location = id, IsFolder = true, Poster = PosterUrl(baseUrl, item), Year = year });
                    continue;
                }
                if (!item.Str("MediaType").Equals("Video", StringComparison.OrdinalIgnoreCase)) continue;

                var source = item.Array("MediaSources").Count > 0 ? item.Array("MediaSources")[0] : null;
                string path = source != null ? source.Str("Path") : item.Str("Path");
                string container = source != null ? source.Str("Container") : item.Str("Container");
                string sourceId = source != null ? source.Str("Id") : string.Empty;
                result.Add(new NetworkItem
                {
                    Name = FileName(path, item.Str("Name", "Film"), container),
                    Title = title,
                    Year = year,
                    Location = StreamUrl(baseUrl, id, sourceId, token),
                    Size = source != null ? source.Long("Size") : 0,
                    Poster = PosterUrl(baseUrl, item),
                });
            }
            return result;
        }

        public static string StreamUrl(string baseUrl, string itemId, string mediaSourceId, string token)
        {
            var sb = new StringBuilder(baseUrl).Append("Videos/").Append(itemId).Append("/stream?static=true");
            if (!string.IsNullOrEmpty(mediaSourceId)) sb.Append("&MediaSourceId=").Append(Uri.EscapeDataString(mediaSourceId));
            // "ApiKey" for current Jellyfin versions (legacy "api_key" can be switched off there), "api_key" for Emby.
            string key = Uri.EscapeDataString(token ?? string.Empty);
            sb.Append("&ApiKey=").Append(key).Append("&api_key=").Append(key);
            return sb.ToString();
        }

        /// <summary>Poster scaled down by the server; null if the item has none.</summary>
        public static string PosterUrl(string baseUrl, object item)
        {
            string tag = item.Get("ImageTags").Str("Primary");
            if (tag.Length == 0) return null;
            return baseUrl + "Items/" + item.Str("Id") + "/Images/Primary?fillHeight=360&quality=85&tag=" + Uri.EscapeDataString(tag);
        }

        /// <summary>File name of the movie on the server (others in the cinema find their own copy by it); title plus extension otherwise.</summary>
        public static string FileName(string serverPath, string title, string container)
        {
            string name = LastSegment(serverPath);
            if (MediaMatcher.IsVideoFile(name)) return name;
            string ext = "." + (container ?? string.Empty).Split(',')[0].Trim().ToLowerInvariant();
            if (ext == ".mpegts") ext = ".ts";
            if (ext == ".matroska") ext = ".mkv";
            if (!MediaMatcher.IsVideoFile("x" + ext)) ext = MediaMatcher.IsVideoFile("x" + Path.GetExtension(name)) ? Path.GetExtension(name) : ".mkv";
            foreach (char c in Path.GetInvalidFileNameChars())
                title = title.Replace(c, ' ');
            return title.Trim() + ext;
        }

        /// <summary>Last part of a server path with "/" or "\" separators.</summary>
        public static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        /// <summary>Answer to the "who is JellyfinServer?" broadcast.</summary>
        public static NetworkSource ParseDiscoveryAnswer(string text)
        {
            object json;
            try { json = Json.Parse(text); }
            catch (FormatException) { return null; }
            string address = json.Str("Address");
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
            string url = uri.GetLeftPart(UriPartial.Path);
            if (!url.EndsWith("/", StringComparison.Ordinal)) url += "/";
            return new NetworkSource { Kind = NetworkSourceKind.Jellyfin, Name = json.Str("Name", uri.Host), Address = url, Discovered = true };
        }

        /// <summary>Broadcasts the Jellyfin/Emby discovery question and collects the servers that answer.</summary>
        public static List<NetworkSource> Discover(int timeoutMs)
        {
            var result = new List<NetworkSource>();
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.EnableBroadcast = true;
                udp.Client.ReceiveTimeout = 300;
                var target = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
                foreach (string question in new[] { "who is JellyfinServer?", "who is EmbyServer?" })
                {
                    byte[] data = Encoding.ASCII.GetBytes(question);
                    try { udp.Send(data, data.Length, target); }
                    catch (SocketException) { }
                }
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        var server = ParseDiscoveryAnswer(Encoding.UTF8.GetString(udp.Receive(ref from)));
                        if (server != null && !result.Exists(r => r.Address == server.Address))
                            result.Add(server);
                    }
                    catch (SocketException)
                    {
                        // Receive timeout: loop on
                    }
                }
            }
            return result;
        }
    }

    sealed class JellyfinBrowser : INetworkBrowser, ISearchableBrowser
    {
        readonly HttpClient m_Http;
        readonly string m_Base;
        string m_Token;
        string m_UserId;

        public JellyfinBrowser(NetworkSource source)
        {
            Source = source;
            m_Base = source.Address.EndsWith("/", StringComparison.Ordinal) ? source.Address : source.Address + "/";
            m_Http = NetHttp.CreateClient(new Uri(m_Base), null, null);
            SetAuthorization(null);
        }

        // "Authorization" for Jellyfin, "X-Emby-Authorization" for Emby and older Jellyfin versions.
        void SetAuthorization(string token)
        {
            var headers = m_Http.DefaultRequestHeaders;
            headers.Remove("Authorization");
            headers.Remove("X-Emby-Authorization");
            headers.TryAddWithoutValidation("Authorization", Jellyfin.AuthorizationValue(token));
            headers.TryAddWithoutValidation("X-Emby-Authorization", Jellyfin.AuthorizationValue(token));
            if (token != null) headers.TryAddWithoutValidation("X-Emby-Token", token);
        }

        public NetworkSource Source { get; }

        void Login()
        {
            if (m_Token != null) return;
            if (string.IsNullOrEmpty(Source.User)) throw new IOException("Anmeldung abgelehnt (bitte Benutzername eingeben).");
            object json;
            try
            {
                json = NetHttp.Json(m_Http, m_Base + "Users/AuthenticateByName", Jellyfin.LoginBody(Source.User, Source.Password ?? string.Empty));
            }
            catch (IOException e) when (e.Message.StartsWith("Nicht gefunden", StringComparison.Ordinal))
            {
                throw new IOException("Unter dieser Adresse antwortet kein Jellyfin/Emby-Server.");
            }
            if (!Jellyfin.ParseLogin(json, out string token, out string userId))
                throw new IOException("Anmeldung abgelehnt (keine Sitzung erhalten).");
            m_Token = token;
            m_UserId = userId;
            SetAuthorization(token);
        }

        public List<NetworkItem> List(string location)
        {
            Login();
            if (string.IsNullOrEmpty(location))
                return Jellyfin.ParseViews(NetHttp.Json(m_Http, m_Base + "Users/" + m_UserId + "/Views"), m_Base);
            return Jellyfin.ParseItems(NetHttp.Json(m_Http, m_Base + Jellyfin.ItemsQuery(m_UserId, location)), m_Base, m_Token);
        }

        public List<NetworkItem> Search(string text)
        {
            Login();
            return Jellyfin.ParseItems(NetHttp.Json(m_Http, m_Base + Jellyfin.SearchQuery(m_UserId, text)), m_Base, m_Token);
        }

        public byte[] ReadSmallFile(string location, int maxBytes) =>
            MediaMatcher.LooksLikeUrl(location) ? NetHttp.ReadSmall(m_Http, location, maxBytes) : null;

        public IRandomAccessSource OpenFile(string location) => NetworkBrowser.OpenFile(Source, location);

        public string FolderOf(string location) => null;

        public string DirectUrl(string location) => NetworkBrowser.PlayableDirectly(location) ? location : null;

        public void Dispose() => m_Http.Dispose();
    }
}
