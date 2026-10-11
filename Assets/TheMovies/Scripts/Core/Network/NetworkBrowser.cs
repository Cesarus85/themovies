using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TheMovies.Core.Network
{
    /// <summary>Lists folders of a network source. Blocking calls; use from a worker thread, one call at a time.</summary>
    public interface INetworkBrowser : IDisposable
    {
        NetworkSource Source { get; }

        /// <summary>Folders and files at a location (<see cref="NetworkSource.RootLocation"/> or a folder's <see cref="NetworkItem.Location"/>).</summary>
        List<NetworkItem> List(string location);

        /// <summary>Reads a small file completely (subtitles); null if it is larger than <paramref name="maxBytes"/>.</summary>
        byte[] ReadSmallFile(string location, int maxBytes);

        /// <summary>Opens a file for streaming. The returned source has its own connection.</summary>
        IRandomAccessSource OpenFile(string location);

        /// <summary>The folder containing a file location, or null if that cannot be derived (DLNA).</summary>
        string FolderOf(string location);

        /// <summary>Address a player can use directly (no login needed), or null when the file must go through the local proxy.</summary>
        string DirectUrl(string location);
    }

    public static class NetworkBrowser
    {
        public static INetworkBrowser Create(NetworkSource source)
        {
            switch (source.Kind)
            {
                case NetworkSourceKind.Smb: return new SmbBrowser(source);
                case NetworkSourceKind.WebDav: return new WebDavBrowser(source);
                default: return new DlnaBrowser(source);
            }
        }

        static bool IsPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (var tcp = new System.Net.Sockets.TcpClient())
                {
                    var connect = tcp.ConnectAsync(host, port);
                    return connect.Wait(timeoutMs) && !connect.IsFaulted && tcp.Connected;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Opens a file of a source for streaming (own connection, opened lazily by <see cref="IRandomAccessSource.Open"/>).</summary>
        public static IRandomAccessSource OpenFile(NetworkSource source, string location)
        {
            switch (source.Kind)
            {
                case NetworkSourceKind.Smb:
                    if (!SmbAddress.TryParse(location, out var address)) throw new IOException("Ungültige SMB-Adresse.");
                    return new SmbFileSource(address, source.User, source.Password);
                case NetworkSourceKind.WebDav:
                    return new HttpRangeSource(location, source.User, source.Password);
                default:
                    return new HttpRangeSource(location);
            }
        }

        /// <summary>Searches the local network for NAS devices (Bonjour "_smb._tcp") and DLNA media servers.</summary>
        public static List<NetworkSource> Discover(int timeoutMs)
        {
            var smb = Task.Run(() =>
            {
                try { return Mdns.Discover("_smb._tcp.local", timeoutMs); }
                catch (Exception) { return new List<MdnsService>(); }
            });
            var dlna = Task.Run(() =>
            {
                try { return Dlna.Discover(timeoutMs); }
                catch (Exception) { return new List<string>(); }
            });

            var result = new List<NetworkSource>();
            foreach (var s in smb.Result)
            {
                var address = new SmbAddress { Host = s.Address, Port = s.Port };
                result.Add(new NetworkSource { Kind = NetworkSourceKind.Smb, Name = s.Name, Address = address.ToString(), Discovered = true });
            }

            var devices = new List<(NetworkSource source, string host)>();
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) })
            {
                var fetches = new List<Task<(NetworkSource, string)>>();
                foreach (string location in dlna.Result)
                {
                    fetches.Add(Task.Run(() =>
                    {
                        try
                        {
                            var device = Dlna.FetchDevice(http, location);
                            if (device == null) return ((NetworkSource)null, (string)null);
                            var source = new NetworkSource { Kind = NetworkSourceKind.Dlna, Name = device.FriendlyName, Address = location, Discovered = true };
                            return (source, new Uri(location).Host);
                        }
                        catch (Exception)
                        {
                            return ((NetworkSource)null, (string)null);
                        }
                    }));
                }
                foreach (var f in fetches)
                    if (f.Result.Item1 != null)
                        devices.Add(f.Result);
            }

            // A NAS found as media server usually also has file shares: offer them when port 445 answers.
            var probes = new List<Task<NetworkSource>>();
            var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (source, host) in devices)
            {
                result.Add(source);
                bool known = result.Exists(r => r.Kind == NetworkSourceKind.Smb && SmbAddress.TryParse(r.Address, out var a) && a.Host == host);
                if (known || !probed.Add(host)) continue;
                string name = source.Name;
                probes.Add(Task.Run(() => IsPortOpen(host, Smb2Client.DefaultPort, 800)
                    ? new NetworkSource { Kind = NetworkSourceKind.Smb, Name = name, Address = new SmbAddress { Host = host, Port = Smb2Client.DefaultPort }.ToString(), Discovered = true }
                    : null));
            }
            foreach (var p in probes)
                if (p.Result != null && !result.Exists(r => r.Kind == NetworkSourceKind.Smb && r.Address == p.Result.Address))
                    result.Add(p.Result);
            return result;
        }
    }

    sealed class SmbBrowser : INetworkBrowser
    {
        Smb2Client m_Client;
        readonly Dictionary<string, uint> m_Trees = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        public SmbBrowser(NetworkSource source)
        {
            Source = source;
        }

        public NetworkSource Source { get; }

        public List<NetworkItem> List(string location)
        {
            var address = Parse(location);
            return Run(client =>
            {
                var items = new List<NetworkItem>();
                if (string.IsNullOrEmpty(address.Share))
                {
                    foreach (string share in client.ListShares())
                        items.Add(new NetworkItem { Name = share, Location = address.Child(share).ToString(), IsFolder = true });
                    return items;
                }
                uint tree = Tree(client, address.Share);
                foreach (var e in client.ListDirectory(tree, address.Path))
                {
                    if (e.IsHidden) continue;
                    items.Add(new NetworkItem { Name = e.Name, Location = address.Child(e.Name).ToString(), IsFolder = e.IsDirectory, Size = e.Size });
                }
                return items;
            });
        }

        public byte[] ReadSmallFile(string location, int maxBytes)
        {
            var address = Parse(location);
            return Run(client =>
            {
                uint tree = Tree(client, address.Share);
                byte[] id = client.Open(tree, address.Path, false, out long size);
                try
                {
                    if (size > maxBytes) return null;
                    var data = new byte[size];
                    int read = 0;
                    while (read < size)
                    {
                        int n = client.Read(tree, id, read, data, read, (int)size - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    return data;
                }
                finally
                {
                    client.Close(tree, id);
                }
            });
        }

        public IRandomAccessSource OpenFile(string location) => NetworkBrowser.OpenFile(Source, location);

        public string FolderOf(string location)
        {
            var a = Parse(location);
            if (string.IsNullOrEmpty(a.Path)) return null;
            int slash = a.Path.LastIndexOf('/');
            a.Path = slash < 0 ? string.Empty : a.Path.Substring(0, slash);
            return a.ToString();
        }

        public string DirectUrl(string location) => null;

        static SmbAddress Parse(string location)
        {
            if (!SmbAddress.TryParse(location, out var a)) throw new IOException("Ungültige SMB-Adresse.");
            return a;
        }

        uint Tree(Smb2Client client, string share)
        {
            if (!m_Trees.TryGetValue(share, out uint tree))
                m_Trees[share] = tree = client.TreeConnect(share);
            return tree;
        }

        /// <summary>Runs an operation, reconnecting once if the old connection died (NAS idle timeout).</summary>
        T Run<T>(Func<Smb2Client, T> operation)
        {
            bool fresh = m_Client == null;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (m_Client == null)
                    {
                        if (!SmbAddress.TryParse(Source.Address, out var root)) throw new IOException("Ungültige SMB-Adresse.");
                        var client = new Smb2Client(root.Host, root.Port);
                        try
                        {
                            client.Connect();
                            client.Login(Source.User, Source.Password);
                        }
                        catch
                        {
                            client.Dispose();
                            throw;
                        }
                        m_Client = client;
                        m_Trees.Clear();
                    }
                    return operation(m_Client);
                }
                catch (IOException e) when (!(e is SmbException) && !fresh && attempt == 0)
                {
                    Dispose();
                }
                catch (System.Net.Sockets.SocketException) when (!fresh && attempt == 0)
                {
                    Dispose();
                }
            }
        }

        public void Dispose()
        {
            m_Client?.Dispose();
            m_Client = null;
            m_Trees.Clear();
        }
    }

    sealed class WebDavBrowser : INetworkBrowser
    {
        readonly HttpClient m_Http;

        public WebDavBrowser(NetworkSource source)
        {
            Source = source;
            m_Http = NetHttp.CreateClient(new Uri(source.Address), source.User, source.Password);
        }

        public NetworkSource Source { get; }

        public List<NetworkItem> List(string location) => WebDav.List(m_Http, location);

        public byte[] ReadSmallFile(string location, int maxBytes)
        {
            using (var response = m_Http.GetAsync(location, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
            {
                NetHttp.ThrowIfFailed(response);
                if (response.Content.Headers.ContentLength > maxBytes) return null;
                byte[] data = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                return data.Length > maxBytes ? null : data;
            }
        }

        public IRandomAccessSource OpenFile(string location) => NetworkBrowser.OpenFile(Source, location);

        public string FolderOf(string location)
        {
            var uri = new Uri(location);
            return new Uri(uri, ".").ToString();
        }

        // Always through the proxy: the video player would reject the self-signed certificates NAS devices use.
        public string DirectUrl(string location) => null;

        public void Dispose() => m_Http.Dispose();
    }

    sealed class DlnaBrowser : INetworkBrowser
    {
        readonly HttpClient m_Http = new HttpClient { Timeout = NetHttp.Timeout };
        DlnaDevice m_Device;

        public DlnaBrowser(NetworkSource source)
        {
            Source = source;
        }

        public NetworkSource Source { get; }

        public List<NetworkItem> List(string location)
        {
            if (m_Device == null)
            {
                m_Device = Dlna.FetchDevice(m_Http, Source.Address);
                if (m_Device == null) throw new IOException("Das Gerät ist kein Medienserver.");
            }
            return Dlna.Browse(m_Http, m_Device, string.IsNullOrEmpty(location) ? Dlna.RootObjectId : location);
        }

        public byte[] ReadSmallFile(string location, int maxBytes) => null;

        public IRandomAccessSource OpenFile(string location) => NetworkBrowser.OpenFile(Source, location);

        public string FolderOf(string location) => null;

        public string DirectUrl(string location) => location;

        public void Dispose() => m_Http.Dispose();
    }
}
