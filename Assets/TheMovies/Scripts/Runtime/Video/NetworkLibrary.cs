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
    /// Movies on a NAS or network drive: the saved sources (SMB shares, WebDAV folders, DLNA servers), the
    /// network search, folder browsing for the menu and playback. Files that need a login (SMB, WebDAV) are fed
    /// to the video player through a local HTTP proxy; DLNA files are played from their URL directly.
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

        LocalStreamServer m_Server;
        SourceStreamer m_Streamer;
        int m_StreamCounter;

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
            if (e is System.Xml.XmlException) return "Unerwartete Antwort vom Server.";
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
            RunAsync(browser, () => browser.List(location), (items, error) =>
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
                Items = Sorted(items);
                onSuccess?.Invoke();
                Changed?.Invoke();
            });
        }

        /// <summary>Folders first, then movies; other files are hidden.</summary>
        static List<NetworkItem> Sorted(List<NetworkItem> items)
        {
            var result = new List<NetworkItem>();
            foreach (var i in items)
                if (i.IsFolder || MediaMatcher.IsVideoFile(i.Name))
                    result.Add(i);
            result.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder) return a.IsFolder ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public MediaEntry ToEntry(NetworkItem item)
        {
            if (Current == null || item == null || item.IsFolder) return null;
            return new MediaEntry
            {
                Title = Path.GetFileNameWithoutExtension(item.Name),
                Name = item.Name,
                Location = item.Location,
                Size = item.Size,
                SourceId = Current.Key,
            };
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
            m_Streamer = new SourceStreamer(() => browser.OpenFile(location), entry.Size);
            string ext = Path.GetExtension(entry.FileName).ToLowerInvariant();
            if (ext.Length < 2 || ext.Length > 6) ext = ".mp4";
            return m_Server.Serve($"/nas/{++m_StreamCounter}/movie{ext}", m_Streamer, LocalStreamServer.ContentTypeFor(entry.FileName));
        }

        public void StopPlayback()
        {
            if (m_Streamer == null) return;
            m_Server?.StopServing();
            m_Streamer.Dispose();
            m_Streamer = null;
        }

        /// <summary>Seconds buffered ahead of the playback position (estimated from the average bitrate).</summary>
        public float BufferedSeconds(double mediaTime, double duration)
        {
            var streamer = m_Streamer;
            if (streamer == null || duration <= 0) return float.MaxValue;
            double fraction = Math.Min(1.0, Math.Max(0.0, mediaTime / duration));
            long ahead = streamer.BufferedBytesFrom((long)(streamer.TotalSize * fraction));
            if (ahead == long.MaxValue) return float.MaxValue;
            return (float)(ahead / (streamer.TotalSize / duration));
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
