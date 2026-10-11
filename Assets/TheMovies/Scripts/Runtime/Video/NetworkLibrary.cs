using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheMovies.Core;
using TheMovies.Core.Network;
using TheMovies.Core.Streaming;
using UnityEngine;

namespace TheMovies.Video
{
    /// <summary>
    /// Movies on a NAS, network drive or media server: the saved sources (SMB shares, WebDAV folders, DLNA,
    /// Jellyfin/Emby, Plex), the network search, folder browsing for the menu and playback. Files that need a
    /// login (SMB, WebDAV) are fed to the video player through a local HTTP proxy; DLNA, Jellyfin and Plex files
    /// are played from their URL directly (HTTPS with a self-signed certificate goes through the proxy too).
    /// All network work runs on worker threads; results are applied on the main thread.
    /// </summary>
    public sealed class NetworkLibrary : MonoBehaviour
    {
        const string k_PrefsKey = "TheMovies.NetworkSources";
        const int k_DiscoveryMs = 3000;
        const int k_MaxSubtitleBytes = 2 * 1024 * 1024;

        public static NetworkLibrary Instance { get; private set; }

        readonly List<NetworkSource> m_Saved = new List<NetworkSource>();
        readonly List<NetworkSource> m_Discovered = new List<NetworkSource>();
        readonly Dictionary<string, INetworkBrowser> m_Browsers = new Dictionary<string, INetworkBrowser>();
        readonly ConcurrentQueue<Action> m_MainThread = new ConcurrentQueue<Action>();
        readonly Stack<(string location, string title)> m_Path = new Stack<(string, string)>();
        int m_BrowseToken;
        const string k_SearchPrefix = "\u0001search:";

        LocalStreamServer m_Server;
        SourceStreamer m_Streamer;
        int m_StreamCounter;
        int m_RequestsAtServe;

        /// <summary>Fires on the main thread whenever sources, the folder listing or the busy state change.</summary>
        public event Action Changed;

        public bool IsDiscovering { get; private set; }
        public bool IsBusy { get; private set; }

        /// <summary>Last error of browsing or adding a source, empty if none.</summary>
        public string Error { get; private set; } = string.Empty;

        /// <summary>The last error was a rejected login (wrong or missing user/password).</summary>
        public bool LoginRejected { get; private set; }

        /// <summary>The current source could not be opened at all (nothing listed yet).</summary>
        public bool CurrentFailed => Current != null && !IsBusy && m_Path.Count == 0 && Error.Length > 0;

        /// <summary>The source being browsed, or null while the source list is shown.</summary>
        public NetworkSource Current { get; private set; }

        /// <summary>Folders and movies of the current folder.</summary>
        public List<NetworkItem> Items { get; private set; } = new List<NetworkItem>();

        /// <summary>Folder names from the source root to the current folder.</summary>
        public string PathLabel
        {
            get
            {
                if (Current == null) return string.Empty;
                var titles = new List<string>();
                foreach (var (_, title) in m_Path) titles.Insert(0, title);
                return string.Join(" / ", titles);
            }
        }

        /// <summary>Saved sources first, then discovered ones that are not saved yet.</summary>
        public List<NetworkSource> Sources
        {
            get
            {
                var result = new List<NetworkSource>(m_Saved);
                foreach (var d in m_Discovered)
                {
                    bool known = false;
                    foreach (var s in m_Saved)
                        if (s.Kind == d.Kind && SameServer(s, d)) known = true;
                    if (!known) result.Add(d);
                }
                return result;
            }
        }

        public bool IsSaved(NetworkSource source) => source != null && m_Saved.Exists(s => s.Key == source.Key);

        /// <summary>Error of the running NAS stream (connection lost …), null while it works.</summary>
        public string StreamError => m_Streamer?.Error;

        public bool IsProxyStreaming => m_Streamer != null;

        public static NetworkLibrary Create()
        {
            var go = new GameObject("Network Library");
            return go.AddComponent<NetworkLibrary>();
        }

        void Awake()
        {
            Instance = this;
            string device = SystemInfo.deviceUniqueIdentifier;
            if (!string.IsNullOrEmpty(device) && device != SystemInfo.unsupportedIdentifier)
            {
                Jellyfin.DeviceId = "themovies-" + device;
                PlexServers.ClientId = "themovies-" + device;
            }
            m_Saved.AddRange(NetworkSourceStore.Deserialize(PlayerPrefs.GetString(k_PrefsKey, string.Empty)));
        }

        void OnDestroy()
        {
            StopPlayback();
            m_Server?.Dispose();
            foreach (var b in m_Browsers.Values) DisposeQuietly(b);
            m_Browsers.Clear();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            UpdateStreamRate();
            while (m_MainThread.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>Runs <paramref name="work"/> on a worker thread (serialized per browser) and <paramref name="done"/> on the main thread.</summary>
        void RunAsync<T>(object gate, Func<T> work, Action<T, Exception> done)
        {
            Task.Run(() =>
            {
                T result = default;
                Exception error = null;
                try
                {
                    lock (gate) result = work();
                }
                catch (Exception e)
                {
                    error = e;
                }
                m_MainThread.Enqueue(() => done(result, error));
            });
        }

        static bool IsLoginError(Exception e)
        {
            e = e is AggregateException agg ? agg.GetBaseException() : e;
            if (e is SmbException smb)
                return smb.Status == Smb2Client.StatusLogonFailure || smb.Status == Smb2Client.StatusAccessDenied ||
                       smb.Status == Smb2Client.StatusAccountDisabled || smb.Status == Smb2Client.StatusPasswordExpired;
            return e.Message.StartsWith("Anmeldung abgelehnt", StringComparison.Ordinal);
        }

        static string Describe(Exception e)
        {
            e = e is AggregateException agg ? agg.GetBaseException() : e;
            if (e is SmbException || e is IOException) return e.Message;
            if (e is System.Net.Sockets.SocketException) return "Keine Verbindung: " + e.Message;
            if (e is System.Net.Http.HttpRequestException) return "Keine Verbindung: " + (e.InnerException?.Message ?? e.Message);
            if (e is TaskCanceledException) return "Zeitüberschreitung – ist das Gerät erreichbar?";
            if (e is System.Xml.XmlException || e is FormatException) return "Unerwartete Antwort vom Server.";
            return e.Message;
        }

        static void DisposeQuietly(IDisposable d)
        {
            try { d?.Dispose(); }
            catch (Exception) { }
        }

        // ---------------- Sources ----------------

        public void Discover()
        {
            if (IsDiscovering) return;
            IsDiscovering = true;
            Changed?.Invoke();
            RunAsync(new object(), () => NetworkBrowser.Discover(k_DiscoveryMs), (found, error) =>
            {
                IsDiscovering = false;
                m_Discovered.Clear();
                if (found != null) m_Discovered.AddRange(found);
                if (error != null) Debug.LogWarning($"[TheMovies] Network search failed: {error.Message}");
                Changed?.Invoke();
            });
        }

        /// <summary>Tries the source (lists its top folder); saves and opens it on success.</summary>
        public void AddAndOpen(NetworkSource source)
        {
            source.Discovered = false;
            OpenSource(source, onSuccess: () =>
            {
                m_Saved.RemoveAll(s => s.Key == source.Key);
                m_Saved.Add(source);
                Save();
            });
        }

        public void Remove(NetworkSource source)
        {
            if (source == null) return;
            m_Saved.RemoveAll(s => s.Key == source.Key);
            if (m_Browsers.TryGetValue(source.Key, out var browser))
            {
                m_Browsers.Remove(source.Key);
                RunAsync(browser, () => { browser.Dispose(); return 0; }, (_, __) => { });
            }
            if (Current != null && Current.Key == source.Key) CloseSource();
            Save();
            Changed?.Invoke();
        }

        void Save()
        {
            PlayerPrefs.SetString(k_PrefsKey, NetworkSourceStore.Serialize(m_Saved));
            PlayerPrefs.Save();
        }

        static bool SameServer(NetworkSource a, NetworkSource b)
        {
            if (a.Kind == NetworkSourceKind.Smb && SmbAddress.TryParse(a.Address, out var x) && SmbAddress.TryParse(b.Address, out var y))
                return string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase) && x.Port == y.Port;
            return string.Equals(a.Address, b.Address, StringComparison.OrdinalIgnoreCase);
        }

        NetworkSource FindSource(string key)
        {
            foreach (var s in m_Saved)
                if (s.Key == key) return s;
            foreach (var s in m_Discovered)
                if (s.Key == key) return s;
            if (Current != null && Current.Key == key) return Current;
            return null;
        }

        INetworkBrowser BrowserFor(NetworkSource source)
        {
            if (m_Browsers.TryGetValue(source.Key, out var browser) &&
                browser.Source.User == source.User && browser.Source.Password == source.Password)
                return browser;
            if (browser != null) DisposeQuietly(browser);
            browser = NetworkBrowser.Create(source.Clone());
            m_Browsers[source.Key] = browser;
            return browser;
        }

        // ---------------- Browsing ----------------

        public void OpenSource(NetworkSource source, Action onSuccess = null)
        {
            Current = source;
            m_Path.Clear();
            Items = new List<NetworkItem>();
            Navigate(source.RootLocation, source.Name, push: true, onSuccess);
        }

        public void OpenFolder(NetworkItem folder)
        {
            if (Current == null || folder == null || !folder.IsFolder) return;
            Navigate(folder.Location, folder.Name, push: true, null);
        }

        /// <summary>Goes up one folder. Returns false when already at the top (then the source list is shown).</summary>
        public bool Back()
        {
            if (Current == null) return false;
            if (m_Path.Count <= 1)
            {
                CloseSource();
                return true;
            }
            m_Path.Pop();
            var (location, title) = m_Path.Peek();
            Navigate(location, title, push: false, null);
            return true;
        }

        public void Reload()
        {
            if (Current == null) return;
            if (m_Path.Count == 0)
            {
                OpenSource(Current);
                return;
            }
            var (location, title) = m_Path.Peek();
            Navigate(location, title, push: false, null);
        }

        public void CloseSource()
        {
            m_BrowseToken++;
            Current = null;
            m_Path.Clear();
            Items = new List<NetworkItem>();
            IsBusy = false;
            Error = string.Empty;
            LoginRejected = false;
            Changed?.Invoke();
        }

        void Navigate(string location, string title, bool push, Action onSuccess)
        {
            var source = Current;
            int token = ++m_BrowseToken;
            INetworkBrowser browser;
            try
            {
                browser = BrowserFor(source);
            }
            catch (Exception e)
            {
                IsBusy = false;
                Error = Describe(e);
                Debug.LogWarning($"[TheMovies] Network source '{source.Name}' ({source.Address}): {e}");
                Changed?.Invoke();
                return;
            }
            IsBusy = true;
            Error = string.Empty;
            LoginRejected = false;
            Changed?.Invoke();
            RunAsync(browser, () => location.StartsWith(k_SearchPrefix, StringComparison.Ordinal) && browser is ISearchableBrowser searchable
                ? searchable.Search(location.Substring(k_SearchPrefix.Length))
                : browser.List(location), (items, error) =>
            {
                if (token != m_BrowseToken) return;
                IsBusy = false;
                if (error != null)
                {
                    // Stay in the source and show the error, so the user sees what went wrong and can log in.
                    Error = Describe(error);
                    LoginRejected = IsLoginError(error);
                    Debug.LogWarning($"[TheMovies] Network source '{source.Name}' ({source.Address}), '{location}': {error}");
                    Changed?.Invoke();
                    return;
                }
                if (push) m_Path.Push((location, title));
                Items = Sorted(items, source.IsMediaServer);
                onSuccess?.Invoke();
                Changed?.Invoke();
            });
        }

        /// <summary>Folders first, then movies; other files are hidden (after their posters/.nfo were assigned).</summary>
        static List<NetworkItem> Sorted(List<NetworkItem> items, bool keepOrder)
        {
            Artwork.AttachSidecars(items);
            var result = new List<NetworkItem>();
            foreach (var i in items)
                if (i.IsFolder || MediaMatcher.IsVideoFile(i.Name))
                    result.Add(i);
            if (keepOrder) return result; // media servers sort by title themselves (articles, series order)
            result.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                return string.Compare(a.DisplayTitle, b.DisplayTitle, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public MediaEntry ToEntry(NetworkItem item)
        {
            if (Current == null || item == null || item.IsFolder) return null;
            return new MediaEntry
            {
                Title = item.DisplayTitle,
                Name = item.Name,
                Poster = item.Poster,
                Location = item.Location,
                Size = item.Size,
                SourceId = Current.Key,
            };
        }

        /// <summary>The current source searches its whole library itself (Jellyfin, Plex).</summary>
        public bool CanSearchServer => Current != null && Current.IsMediaServer && !IsBusy;

        /// <summary>Searches the library of the current media server; the results are shown like a folder.</summary>
        public void SearchServer(string text)
        {
            text = (text ?? string.Empty).Trim();
            if (!CanSearchServer || text.Length == 0) return;
            Navigate(k_SearchPrefix + text, "Suche „" + text + "“", push: true, null);
        }

        /// <summary>
        /// Checks what answers at an http(s) address the user typed: Jellyfin/Emby (→ <see cref="NetworkSourceKind.Jellyfin"/>),
        /// Plex (→ account login by code) or anything else (WebDAV). Calls back on the main thread.
        /// </summary>
        public void Identify(NetworkSource source, Action<NetworkSource> done)
        {
            if (source.Kind != NetworkSourceKind.WebDav)
            {
                done(source);
                return;
            }
            RunAsync(new object(), () =>
            {
                using (var http = NetHttp.CreateClient(new Uri(source.Address), null, null))
                {
                    http.Timeout = TimeSpan.FromSeconds(4);
                    try
                    {
                        if (Jellyfin.IsServerInfo(NetHttp.Json(http, source.Address + "System/Info/Public"), out string name))
                            return new NetworkSource { Kind = NetworkSourceKind.Jellyfin, Address = source.Address, Name = name.Length > 0 ? name : source.Name };
                    }
                    catch (Exception)
                    {
                    }
                    try
                    {
                        if (NetHttp.Json(http, source.Address + "identity").Path("MediaContainer").Str("machineIdentifier").Length > 0)
                            return PlexServers.NewAccountSource();
                    }
                    catch (Exception)
                    {
                    }
                }
                return source;
            }, (result, error) => done(result ?? source));
        }

        // ---------------- Plex login by code ----------------

        /// <summary>Code to enter on plex.tv/link while the Plex login runs, empty otherwise.</summary>
        public string PlexCode { get; private set; } = string.Empty;

        public bool IsLinkingPlex { get; private set; }
        int m_PlexLinkToken;

        /// <summary>Gets a code from plex.tv, waits (up to 10 minutes) until it was entered, then adds and opens the account.</summary>
        public void StartPlexLink()
        {
            int token = ++m_PlexLinkToken;
            IsLinkingPlex = true;
            PlexCode = string.Empty;
            Error = string.Empty;
            Changed?.Invoke();
            var gate = new object();
            var http = PlexServers.CreateClient();
            RunAsync(gate, () => PlexServers.CreatePin(http), (pin, error) =>
            {
                if (token != m_PlexLinkToken)
                {
                    http.Dispose();
                    return;
                }
                if (error != null || string.IsNullOrEmpty(pin.code))
                {
                    http.Dispose();
                    IsLinkingPlex = false;
                    Error = "Plex: " + (error != null ? Describe(error) : "kein Code erhalten");
                    Changed?.Invoke();
                    return;
                }
                PlexCode = pin.code;
                Changed?.Invoke();
                RunAsync(gate, () =>
                {
                    var until = DateTime.UtcNow.AddMinutes(10);
                    while (DateTime.UtcNow < until && token == m_PlexLinkToken)
                    {
                        System.Threading.Thread.Sleep(2000);
                        try
                        {
                            string authToken = PlexServers.CheckPin(http, pin.id);
                            if (authToken != null) return authToken;
                        }
                        catch (IOException)
                        {
                            // Keep waiting (network hiccup)
                        }
                    }
                    return null;
                }, (authToken, pollError) =>
                {
                    http.Dispose();
                    if (token != m_PlexLinkToken) return;
                    IsLinkingPlex = false;
                    PlexCode = string.Empty;
                    if (authToken == null)
                    {
                        Error = pollError != null ? "Plex: " + Describe(pollError) : "Der Plex-Code ist abgelaufen – bitte noch einmal verbinden.";
                        Changed?.Invoke();
                        return;
                    }
                    var account = PlexServers.NewAccountSource();
                    account.Password = authToken;
                    AddAndOpen(account);
                });
            });
        }

        public void CancelPlexLink()
        {
            m_PlexLinkToken++;
            IsLinkingPlex = false;
            PlexCode = string.Empty;
            Changed?.Invoke();
        }

        // ---------------- Posters ----------------

        const int k_MaxPosterBytes = 6 * 1024 * 1024;
        static System.Net.Http.HttpClient s_PosterHttp;

        /// <summary>
        /// Loads the poster of a listed item (its own picture, the one named in its .nfo, or for SMB/WebDAV folders
        /// poster.jpg/folder.jpg inside). Title and year from the .nfo are applied to the item. Calls back on the
        /// main thread with the image bytes (null if there is none).
        /// </summary>
        public void LoadArtwork(NetworkItem item, Action<byte[]> done)
        {
            var source = Current;
            if (source == null || item == null)
            {
                done(null);
                return;
            }
            var browser = BrowserFor(source);
            string poster = item.Poster;
            string nfoLocation = item.Nfo;
            bool folderLookup = item.IsFolder && poster == null && (source.Kind == NetworkSourceKind.Smb || source.Kind == NetworkSourceKind.WebDav);
            RunAsync<(byte[] data, NfoInfo nfo, string poster)>(browser, () =>
            {
                NfoInfo nfo = null;
                if (nfoLocation != null)
                {
                    byte[] text = browser.ReadSmallFile(nfoLocation, 512 * 1024);
                    if (text != null) nfo = Artwork.ParseNfo(System.Text.Encoding.UTF8.GetString(text));
                }
                string picture = poster ?? nfo?.Thumb;
                if (picture == null && folderLookup)
                    picture = Artwork.FolderPoster(browser.List(item.Location)) ?? string.Empty;
                if (string.IsNullOrEmpty(picture)) return (null, nfo, picture);
                return (ReadPicture(browser, picture), nfo, picture);
            }, (result, error) =>
            {
                if (error != null)
                {
                    Debug.Log($"[TheMovies] No poster for '{item.Name}': {Describe(error)}");
                    done(null);
                    return;
                }
                if (result.nfo != null)
                {
                    if (!string.IsNullOrEmpty(result.nfo.Title)) item.Title = result.nfo.Title;
                    if (result.nfo.Year > 0) item.Year = result.nfo.Year;
                }
                item.Nfo = null; // read once
                if (item.Poster == null && result.poster != null) item.Poster = result.poster; // "" = looked, nothing there
                done(result.data);
            });
        }

        /// <summary>Loads a poster remembered with a movie (recently watched list).</summary>
        public void LoadPoster(MediaEntry entry, Action<byte[]> done)
        {
            var source = entry?.SourceId != null ? FindSource(entry.SourceId) : null;
            if (entry == null || string.IsNullOrEmpty(entry.Poster) || (source == null && !MediaMatcher.LooksLikeUrl(entry.Poster)))
            {
                done(null);
                return;
            }
            INetworkBrowser browser = source != null ? BrowserFor(source) : null;
            string picture = entry.Poster;
            RunAsync(browser ?? new object(), () => ReadPicture(browser, picture), (data, error) => done(error == null ? data : null));
        }

        static byte[] ReadPicture(INetworkBrowser browser, string picture)
        {
            // Pictures from the internet (TMDB address in a .nfo) are not on the NAS.
            bool fromInternet = MediaMatcher.LooksLikeUrl(picture) &&
                                (browser == null || browser.Source.Kind == NetworkSourceKind.Smb ||
                                 (browser.Source.Kind == NetworkSourceKind.WebDav && !picture.StartsWith(browser.Source.Address, StringComparison.OrdinalIgnoreCase)));
            if (!fromInternet) return browser.ReadSmallFile(picture, k_MaxPosterBytes);
            s_PosterHttp ??= NetHttp.CreateClient();
            return NetHttp.ReadSmall(s_PosterHttp, picture, k_MaxPosterBytes);
        }

        // ---------------- Playback ----------------

        /// <summary>Returns the address the video player should open, or null (see <paramref name="error"/>).</summary>
        public string Resolve(MediaEntry entry, out string error)
        {
            error = null;
            StopPlayback();
            var source = entry != null && entry.IsNetwork ? FindSource(entry.SourceId) : null;
            if (source == null)
            {
                error = "Die Netzwerkquelle dieses Films ist nicht mehr gespeichert.";
                return null;
            }
            var browser = BrowserFor(source);
            string direct = browser.DirectUrl(entry.Location);
            if (direct != null) return direct;
            if (entry.Size <= 0)
            {
                error = "Dateigröße unbekannt – Datei kann nicht gestreamt werden.";
                return null;
            }

            m_Server ??= new LocalStreamServer();
            string location = entry.Location;
            // 3 parallel connections. The own player buffers up to a minute itself, so the proxy only needs a
            // small window then; Unity's VideoPlayer gets ~40 MB ahead.
            bool native = MoviePlayer.PreferNative && NativeVideoPlayer.Supported;
            m_Streamer = native
                ? new SourceStreamer(() => browser.OpenFile(location), entry.Size, capacityBlocks: 128, readAheadBlocks: 64)
                : new SourceStreamer(() => browser.OpenFile(location), entry.Size, capacityBlocks: 320, readAheadBlocks: 160);
            string ext = Path.GetExtension(entry.FileName).ToLowerInvariant();
            if (ext.Length < 2 || ext.Length > 6) ext = ".mp4";
            m_RequestsAtServe = m_Server.RequestCount;
            m_RateBytes = 0;
            StreamRate = 0;
            string url = m_Server.Serve($"/nas/{++m_StreamCounter}/movie{ext}", m_Streamer, LocalStreamServer.ContentTypeFor(entry.FileName));
            Debug.Log($"[TheMovies] NAS stream {entry.Location} ({entry.Size} bytes) via {url}");
            return url;
        }

        /// <summary>After a player error: what the proxy saw, to tell "player can't reach the proxy" from "NAS doesn't deliver".</summary>
        public string DescribePlaybackFailure(MediaEntry entry)
        {
            if (entry == null || !entry.IsNetwork) return string.Empty;
            if (m_Streamer == null) return "direkt vom Medienserver";
            int requests = m_Server.RequestCount - m_RequestsAtServe;
            if (m_Streamer.Error != null) return "NAS: " + m_Streamer.Error;
            if (requests == 0) return "Der Player hat den lokalen NAS-Proxy nicht erreicht (HTTP blockiert?)";
            if (m_Streamer.BytesReceived == 0) return $"Proxy: {requests} Anfragen, noch keine Daten vom NAS";
            return $"Proxy: {requests} Anfragen, {m_Streamer.BytesReceived / 1024} KB vom NAS gelesen – Format/Codec?";
        }

        public void StopPlayback()
        {
            if (m_Streamer == null) return;
            m_Server?.StopServing();
            m_Streamer.Dispose();
            m_Streamer = null;
        }

        /// <summary>Seconds of video the read-ahead window can hold at most (estimated from the average bitrate).</summary>
        public float ReadAheadSeconds(double duration)
        {
            var streamer = m_Streamer;
            if (streamer == null || duration <= 0) return float.MaxValue;
            return (float)(streamer.ReadAheadBytes / (streamer.TotalSize / duration));
        }

        /// <summary>Seconds of video cached ahead of where the player reads (estimated from the average bitrate).</summary>
        public float BufferedSeconds(double duration)
        {
            var streamer = m_Streamer;
            if (streamer == null || duration <= 0) return float.MaxValue;
            long ahead = streamer.BufferedAheadOfReader();
            return ahead == long.MaxValue ? float.MaxValue : (float)(ahead / (streamer.TotalSize / duration));
        }

        /// <summary>Download rate of the running NAS stream in bytes per second (smoothed).</summary>
        public float StreamRate { get; private set; }
        long m_RateBytes;
        float m_RateAt;

        void UpdateStreamRate()
        {
            if (Time.unscaledTime < m_RateAt) return;
            m_RateAt = Time.unscaledTime + 1f;
            long bytes = m_Streamer?.BytesReceived ?? 0;
            float rate = Mathf.Max(0, bytes - m_RateBytes);
            m_RateBytes = bytes;
            StreamRate = m_Streamer == null ? 0 : Mathf.Lerp(StreamRate, rate, 0.5f);
        }

        // ---------------- Copy to the headset ----------------

        const string k_CopyFirstKey = "TheMovies.NasCopyFirst";

        /// <summary>Folder with the movies copied from a NAS (part of the local library).</summary>
        public static string CopyFolder => Path.Combine(MediaLibrary.AppMoviesFolder, "NAS");

        /// <summary>Copy NAS movies to the headset before playing (reliable seeking) instead of streaming them.</summary>
        public bool CopyFirst
        {
            // Streaming is the default with the own player; Unity's VideoPlayer copes badly with big streams.
            get => PlayerPrefs.GetInt(k_CopyFirstKey, MoviePlayer.PreferNative && NativeVideoPlayer.Supported ? 0 : 1) == 1;
            set
            {
                PlayerPrefs.SetInt(k_CopyFirstKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The running (or last) copy, null if none was started.</summary>
        public FileDownload Copy { get; private set; }
        public string CopyTitle { get; private set; } = string.Empty;
        public bool IsCopying => Copy != null && !Copy.Finished && Copy.Error == null;

        /// <summary>Copies a NAS movie to the headset; <paramref name="done"/> gets the local entry on the main thread.</summary>
        public bool StartCopy(MediaEntry entry, Action<MediaEntry> done, out string error)
        {
            error = null;
            var source = entry != null && entry.IsNetwork ? FindSource(entry.SourceId) : null;
            if (source == null)
            {
                error = "Die Netzwerkquelle dieses Films ist nicht mehr gespeichert.";
                return false;
            }
            if (IsCopying)
            {
                error = "Es wird schon ein Film kopiert.";
                return false;
            }
            string target = Path.Combine(CopyFolder, FileDownload.SafeFileName(entry.FileName));
            long have = File.Exists(target + ".part") ? new FileInfo(target + ".part").Length : 0;
            long free = FreeSpace(MediaLibrary.AppMoviesFolder);
            if (free >= 0 && entry.Size - have > free - 512L * 1024 * 1024)
            {
                error = $"Zu wenig Speicher auf der Quest: {TextUtil.FormatSize(entry.Size)} nötig, {TextUtil.FormatSize(Math.Max(0, free))} frei.";
                return false;
            }

            var copySource = source.Clone();
            string location = entry.Location;
            var download = new FileDownload(() => NetworkBrowser.OpenFile(copySource, location), target, entry.Size);
            Copy = download;
            CopyTitle = entry.Title;
            Changed?.Invoke();
            download.Start().ContinueWith(_ => m_MainThread.Enqueue(() =>
            {
                Changed?.Invoke();
                if (!download.Finished)
                {
                    if (!download.Canceled) Debug.LogWarning($"[TheMovies] Copy of {location} failed: {download.Error}");
                    return;
                }
                var local = new MediaEntry
                {
                    Title = entry.Title,
                    Location = download.TargetPath,
                    Size = new FileInfo(download.TargetPath).Length,
                };
                done?.Invoke(local);
            }));
            return true;
        }

        public void CancelCopy()
        {
            Copy?.Cancel();
        }

        /// <summary>One line for the menu while copying (empty otherwise).</summary>
        public string CopyStatus
        {
            get
            {
                var c = Copy;
                if (c == null) return string.Empty;
                if (c.Finished) return string.Empty;
                if (c.Error != null) return c.Canceled ? string.Empty : $"Kopieren fehlgeschlagen: {c.Error}";
                string left = c.SecondsLeft >= 0 ? " · noch " + TextUtil.FormatTime(c.SecondsLeft) : string.Empty;
                return $"Kopiere „{TextUtil.Ellipsize(CopyTitle, 30)}“ auf die Quest … {c.Progress * 100f:0} %  ·  {c.Rate / 1e6:0.0} MB/s{left}";
            }
        }

        /// <summary>Size of all copies on the headset (bytes).</summary>
        public static long CopiesSize()
        {
            try
            {
                if (!Directory.Exists(CopyFolder)) return 0;
                long total = 0;
                foreach (string f in Directory.GetFiles(CopyFolder))
                    total += new FileInfo(f).Length;
                return total;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Deletes all copies except <paramref name="keep"/> (the movie playing right now).</summary>
        public int DeleteCopies(string keep)
        {
            if (IsCopying) CancelCopy();
            int deleted = 0;
            try
            {
                if (!Directory.Exists(CopyFolder)) return 0;
                foreach (string f in Directory.GetFiles(CopyFolder))
                {
                    if (keep != null && string.Equals(Path.GetFullPath(f), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        File.Delete(f);
                        deleted++;
                    }
                    catch (IOException)
                    {
                        // Still in use (copy just being canceled): next time
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[TheMovies] Could not delete copies: " + e.Message);
            }
            return deleted;
        }

        static long FreeSpace(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var stat = new AndroidJavaObject("android.os.StatFs", path))
                    return stat.Call<long>("getAvailableBytes");
#else
                return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).AvailableFreeSpace;
#endif
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>A separate reader for the host stream (participants without their own copy). Opened lazily.</summary>
        public IRandomAccessSource OpenForHosting(MediaEntry entry)
        {
            var source = entry != null && entry.IsNetwork ? FindSource(entry.SourceId) : null;
            return source == null ? null : NetworkBrowser.OpenFile(source.Clone(), entry.Location);
        }

        /// <summary>Looks for "Movie.srt" (or "Movie.de.srt" …) next to the movie and calls back on the main thread.</summary>
        public void LoadSubtitles(MediaEntry entry, Action<byte[], string> callback)
        {
            var source = entry != null && entry.IsNetwork ? FindSource(entry.SourceId) : null;
            if (source == null) return;
            var browser = BrowserFor(source);
            string folder = browser.FolderOf(entry.Location);
            if (folder == null) return;
            string movieName = entry.FileName;
            RunAsync<(byte[] data, string name)>(browser, () =>
            {
                var files = browser.List(folder);
                var names = new List<string>();
                foreach (var f in files)
                    if (!f.IsFolder) names.Add(f.Name);
                string pick = SubtitleFiles.PickSidecar(movieName, names);
                if (pick == null) return (null, null);
                var file = files.Find(f => f.Name == pick);
                return (browser.ReadSmallFile(file.Location, k_MaxSubtitleBytes), pick);
            }, (result, error) =>
            {
                if (error != null)
                {
                    Debug.LogWarning($"[TheMovies] Could not load NAS subtitles: {error.Message}");
                    return;
                }
                if (result.data != null) callback(result.data, result.name);
            });
        }
    }
}
