using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TheMovies.Core.Network
{
    public enum NetworkSourceKind
    {
        /// <summary>Windows/Samba file share (NAS, PC).</summary>
        Smb,

        /// <summary>WebDAV folder over HTTP(S).</summary>
        WebDav,

        /// <summary>UPnP/DLNA media server.</summary>
        Dlna,
    }

    /// <summary>A NAS or network folder the user added (or that was found in the local network).</summary>
    public sealed class NetworkSource
    {
        public NetworkSourceKind Kind;
        public string Name = string.Empty;

        /// <summary>smb://host[:port][/share[/path]], http(s)://… (WebDAV) or the DLNA device description URL.</summary>
        public string Address = string.Empty;

        public string User = string.Empty;
        public string Password = string.Empty;

        /// <summary>Found by the network search, not saved by the user.</summary>
        public bool Discovered;

        public string Key => (Kind + ":" + Address).ToLowerInvariant();

        /// <summary>Location of the top folder, as understood by the source's browser.</summary>
        public string RootLocation => Kind == NetworkSourceKind.Dlna ? Dlna.RootObjectId : Address;

        public bool HasLogin => !string.IsNullOrEmpty(User);

        public string KindLabel
        {
            get
            {
                switch (Kind)
                {
                    case NetworkSourceKind.Smb: return "SMB";
                    case NetworkSourceKind.WebDav: return "WebDAV";
                    default: return "DLNA";
                }
            }
        }

        public NetworkSource Clone() => (NetworkSource)MemberwiseClone();

        /// <summary>
        /// Interprets what the user typed: "\\nas\filme", "smb://nas/filme", "nas/filme" or "nas" (SMB),
        /// "http(s)://…" or "dav(s)://…" (WebDAV).
        /// </summary>
        public static bool TryParse(string input, out NetworkSource source, out string error)
        {
            source = null;
            error = null;
            input = (input ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                error = "Bitte eine Adresse eingeben.";
                return false;
            }

            string lower = input.ToLowerInvariant();
            if (lower.StartsWith("dav://", StringComparison.Ordinal)) input = "http://" + input.Substring(6);
            else if (lower.StartsWith("davs://", StringComparison.Ordinal)) input = "https://" + input.Substring(7);
            else if (lower.StartsWith("webdav://", StringComparison.Ordinal)) input = "http://" + input.Substring(9);
            else if (lower.StartsWith("webdavs://", StringComparison.Ordinal)) input = "https://" + input.Substring(10);

            if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                {
                    error = "Ungültige Adresse.";
                    return false;
                }
                string url = uri.GetLeftPart(UriPartial.Path);
                if (!url.EndsWith("/", StringComparison.Ordinal)) url += "/";
                source = new NetworkSource
                {
                    Kind = NetworkSourceKind.WebDav,
                    Address = url,
                    Name = uri.Host + Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/'),
                };
                return true;
            }

            if (!SmbAddress.TryParse(input, out var smb))
            {
                error = "Ungültige Adresse. Beispiele: \\\\nas\\filme, smb://192.168.1.10/filme, https://nas:5006/filme";
                return false;
            }
            source = new NetworkSource
            {
                Kind = NetworkSourceKind.Smb,
                Address = smb.ToString(),
                Name = smb.DisplayName,
            };
            return true;
        }
    }

    /// <summary>A folder or file in a network source.</summary>
    public sealed class NetworkItem
    {
        public string Name;

        /// <summary>Folder: what <see cref="INetworkBrowser.List"/> accepts. File: what <see cref="INetworkBrowser.OpenFile"/> accepts.</summary>
        public string Location;

        public bool IsFolder;
        public long Size;

        public override string ToString() => IsFolder ? Name + "/" : Name;
    }

    /// <summary>smb://host[:port]/share/path</summary>
    public struct SmbAddress
    {
        public string Host;
        public int Port;
        public string Share;

        /// <summary>Path inside the share with "/" separators, no leading slash; empty for the share root.</summary>
        public string Path;

        public string DisplayName
        {
            get
            {
                string s = Host;
                if (!string.IsNullOrEmpty(Share)) s += "/" + Share;
                if (!string.IsNullOrEmpty(Path)) s += "/" + Path;
                return s;
            }
        }

        public static bool TryParse(string input, out SmbAddress address)
        {
            address = default;
            string s = (input ?? string.Empty).Trim().Replace('\\', '/');
            if (s.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(6);
            else if (s.StartsWith("cifs://", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7);
            s = s.TrimStart('/');
            if (s.Length == 0 || s.Contains("://")) return false;

            string[] parts = s.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;
            string hostPart = parts[0];
            int at = hostPart.LastIndexOf('@');
            if (at >= 0) hostPart = hostPart.Substring(at + 1); // ignore user info, the login is entered separately

            string host = hostPart;
            int port = Smb2Client.DefaultPort;
            if (hostPart.StartsWith("[", StringComparison.Ordinal))
            {
                int close = hostPart.IndexOf(']');
                if (close < 0) return false;
                host = hostPart.Substring(1, close - 1);
                if (close + 2 < hostPart.Length && hostPart[close + 1] == ':' && !int.TryParse(hostPart.Substring(close + 2), out port)) return false;
            }
            else
            {
                int colon = hostPart.IndexOf(':');
                if (colon >= 0)
                {
                    if (hostPart.IndexOf(':', colon + 1) >= 0) host = hostPart; // bare IPv6
                    else
                    {
                        host = hostPart.Substring(0, colon);
                        if (!int.TryParse(hostPart.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
                    }
                }
            }
            if (host.Length == 0) return false;
            foreach (char c in host)
                if (char.IsWhiteSpace(c)) return false;

            address = new SmbAddress
            {
                Host = host,
                Port = port,
                Share = parts.Length > 1 ? parts[1] : string.Empty,
                Path = parts.Length > 2 ? string.Join("/", parts, 2, parts.Length - 2) : string.Empty,
            };
            return true;
        }

        public SmbAddress Child(string name)
        {
            var a = this;
            if (string.IsNullOrEmpty(Share)) a.Share = name;
            else a.Path = string.IsNullOrEmpty(Path) ? name : Path + "/" + name;
            return a;
        }

        public override string ToString()
        {
            var sb = new StringBuilder("smb://");
            sb.Append(Host.IndexOf(':') >= 0 ? "[" + Host + "]" : Host);
            if (Port != Smb2Client.DefaultPort && Port > 0) sb.Append(':').Append(Port);
            if (!string.IsNullOrEmpty(Share)) sb.Append('/').Append(Share);
            if (!string.IsNullOrEmpty(Path)) sb.Append('/').Append(Path);
            return sb.ToString();
        }
    }

    public static class NetAddress
    {
        /// <summary>True for private/link-local addresses and typical local host names (no dot, .local, .lan, .home, .fritz.box …).</summary>
        public static bool IsLocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            host = host.Trim('[', ']');
            if (IPAddress.TryParse(host, out var ip))
            {
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.AddressFamily == AddressFamily.InterNetworkV6)
                    return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
                byte[] b = ip.GetAddressBytes();
                return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
            }
            string h = host.ToLowerInvariant().TrimEnd('.');
            if (h.IndexOf('.') < 0) return true;
            string[] localSuffixes = { ".local", ".lan", ".home", ".home.arpa", ".internal", ".intranet", ".fritz.box", ".box", ".localdomain" };
            foreach (string suffix in localSuffixes)
                if (h.EndsWith(suffix, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }

    /// <summary>Saves the user's network sources. Stored by the app in its private preferences.</summary>
    public static class NetworkSourceStore
    {
        const string Version = "v1";

        public static string Serialize(IEnumerable<NetworkSource> sources)
        {
            var sb = new StringBuilder();
            foreach (var s in sources)
            {
                if (s == null || s.Discovered) continue;
                sb.Append(Version).Append('\t')
                    .Append((int)s.Kind).Append('\t')
                    .Append(Uri.EscapeDataString(s.Name ?? string.Empty)).Append('\t')
                    .Append(Uri.EscapeDataString(s.Address ?? string.Empty)).Append('\t')
                    .Append(Uri.EscapeDataString(s.User ?? string.Empty)).Append('\t')
                    .Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Password ?? string.Empty)))
                    .Append('\n');
            }
            return sb.ToString();
        }

        public static List<NetworkSource> Deserialize(string text)
        {
            var result = new List<NetworkSource>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string line in text.Split('\n'))
            {
                string[] f = line.Trim('\r').Split('\t');
                if (f.Length < 6 || f[0] != Version) continue;
                try
                {
                    if (!int.TryParse(f[1], out int kind) || kind < 0 || kind > (int)NetworkSourceKind.Dlna) continue;
                    var s = new NetworkSource
                    {
                        Kind = (NetworkSourceKind)kind,
                        Name = Uri.UnescapeDataString(f[2]),
                        Address = Uri.UnescapeDataString(f[3]),
                        User = Uri.UnescapeDataString(f[4]),
                        Password = Encoding.UTF8.GetString(Convert.FromBase64String(f[5])),
                    };
                    if (s.Address.Length > 0) result.Add(s);
                }
                catch (FormatException)
                {
                    // Skip broken lines
                }
            }
            return result;
        }
    }
}
