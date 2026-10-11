using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Xml;

namespace TheMovies.Core.Network
{
    /// <summary>A UPnP media server and the address of its ContentDirectory service.</summary>
    public sealed class DlnaDevice
    {
        public string FriendlyName;
        public string ControlUrl;
        public string ServiceType = "urn:schemas-upnp-org:service:ContentDirectory:1";
    }

    /// <summary>UPnP/DLNA: finding media servers (SSDP) and browsing their ContentDirectory.</summary>
    public static class Dlna
    {
        public const string RootObjectId = "0";
        const int PageSize = 200;
        const int MaxItems = 5000;

        // ---------------- Discovery ----------------

        /// <summary>Sends SSDP searches for media servers and returns the description URLs that answered.</summary>
        public static List<string> Discover(int timeoutMs)
        {
            var locations = new List<string>();
            using (var udp = new UdpClient(AddressFamily.InterNetwork))
            {
                udp.Client.ReceiveTimeout = 300;
                byte[] search = Encoding.ASCII.GetBytes(
                    "M-SEARCH * HTTP/1.1\r\n" +
                    "HOST: 239.255.255.250:1900\r\n" +
                    "MAN: \"ssdp:discover\"\r\n" +
                    "MX: 2\r\n" +
                    "ST: urn:schemas-upnp-org:device:MediaServer:1\r\n" +
                    "USER-AGENT: Android/1 UPnP/1.1 TheMovies/1.0\r\n\r\n");
                var target = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                int sent = 0;
                var nextSend = DateTime.UtcNow;
                while (DateTime.UtcNow < deadline)
                {
                    if (sent < 3 && DateTime.UtcNow >= nextSend)
                    {
                        try { udp.Send(search, search.Length, target); }
                        catch (SocketException) { }
                        sent++;
                        nextSend = DateTime.UtcNow.AddMilliseconds(600);
                    }
                    try
                    {
                        var from = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref from);
                        string location = ParseSsdpLocation(Encoding.UTF8.GetString(data));
                        if (location != null && !locations.Contains(location))
                            locations.Add(location);
                    }
                    catch (SocketException)
                    {
                        // Receive timeout: loop on
                    }
                }
            }
            return locations;
        }

        public static string ParseSsdpLocation(string response)
        {
            foreach (string line in response.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (line.Substring(0, colon).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line.Substring(colon + 1).Trim();
                    return Uri.TryCreate(value, UriKind.Absolute, out _) ? value : null;
                }
            }
            return null;
        }

        public static DlnaDevice FetchDevice(HttpClient http, string descriptionUrl)
        {
            string xml = http.GetStringAsync(descriptionUrl).GetAwaiter().GetResult();
            return ParseDescription(xml, descriptionUrl);
        }

        /// <summary>Reads the device name and ContentDirectory control URL; null if the device has no ContentDirectory.</summary>
        public static DlnaDevice ParseDescription(string xml, string descriptionUrl)
        {
            var doc = LoadXml(xml);
            string baseUrl = First(doc.DocumentElement, "URLBase")?.InnerText.Trim();
            var baseUri = new Uri(string.IsNullOrEmpty(baseUrl) ? descriptionUrl : baseUrl);
            var device = new DlnaDevice { FriendlyName = First(doc.DocumentElement, "friendlyName")?.InnerText.Trim() ?? baseUri.Host };
            foreach (var service in All(doc.DocumentElement, "service"))
            {
                string type = First(service, "serviceType")?.InnerText.Trim() ?? string.Empty;
                if (type.IndexOf(":ContentDirectory:", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string control = First(service, "controlURL")?.InnerText.Trim();
                if (string.IsNullOrEmpty(control)) continue;
                device.ServiceType = type;
                device.ControlUrl = new Uri(baseUri, control).ToString();
                return device;
            }
            return null;
        }

        // ---------------- Browsing ----------------

        public static List<NetworkItem> Browse(HttpClient http, DlnaDevice device, string objectId)
        {
            var result = new List<NetworkItem>();
            int start = 0;
            while (result.Count < MaxItems)
            {
                string body = BuildBrowseRequest(device.ServiceType, objectId, start, PageSize);
                using (var request = new HttpRequestMessage(HttpMethod.Post, device.ControlUrl))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "text/xml");
                    request.Content.Headers.ContentType.CharSet = "utf-8";
                    request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{device.ServiceType}#Browse\"");
                    using (var response = http.SendAsync(request).GetAwaiter().GetResult())
                    {
                        if ((int)response.StatusCode == 500)
                            throw new IOException("Der Medienserver hat die Anfrage abgelehnt.");
                        NetHttp.ThrowIfFailed(response);
                        string xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        var page = ParseBrowseResponse(xml, out int returned, out int total);
                        result.AddRange(page);
                        start += returned;
                        if (returned <= 0 || start >= total) break;
                    }
                }
            }
            return result;
        }

        public static string BuildBrowseRequest(string serviceType, string objectId, int start, int count)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                   "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
                   "<s:Body><u:Browse xmlns:u=\"" + SecurityElement.Escape(serviceType) + "\">" +
                   "<ObjectID>" + SecurityElement.Escape(objectId) + "</ObjectID>" +
                   "<BrowseFlag>BrowseDirectChildren</BrowseFlag>" +
                   "<Filter>*</Filter>" +
                   "<StartingIndex>" + start + "</StartingIndex>" +
                   "<RequestedCount>" + count + "</RequestedCount>" +
                   "<SortCriteria></SortCriteria>" +
                   "</u:Browse></s:Body></s:Envelope>";
        }

        /// <summary>Parses a Browse SOAP response: containers become folders, video items become files.</summary>
        public static List<NetworkItem> ParseBrowseResponse(string soap, out int returned, out int total)
        {
            var doc = LoadXml(soap);
            string didl = First(doc.DocumentElement, "Result")?.InnerText ?? string.Empty;
            int.TryParse(First(doc.DocumentElement, "NumberReturned")?.InnerText, out returned);
            int.TryParse(First(doc.DocumentElement, "TotalMatches")?.InnerText, out total);
            var items = ParseDidl(didl);
            if (returned == 0) returned = items.Count;
            return items;
        }

        public static List<NetworkItem> ParseDidl(string didl)
        {
            var result = new List<NetworkItem>();
            if (string.IsNullOrWhiteSpace(didl)) return result;
            var doc = LoadXml(didl);
            foreach (XmlNode node in doc.DocumentElement.ChildNodes)
            {
                if (!(node is XmlElement e)) continue;
                string title = First(e, "title")?.InnerText.Trim() ?? "?";
                string art = First(e, "albumArtURI")?.InnerText.Trim();
                if (art != null && !MediaMatcher.LooksLikeUrl(art)) art = null;
                if (e.LocalName == "container")
                {
                    string id = e.GetAttribute("id");
                    if (id.Length > 0)
                        result.Add(new NetworkItem { Name = title, Location = id, IsFolder = true, Poster = art });
                }
                else if (e.LocalName == "item")
                {
                    XmlElement best = null;
                    string bestMime = null;
                    foreach (var res in All(e, "res"))
                    {
                        string info = res.GetAttribute("protocolInfo");
                        string[] parts = info.Split(':');
                        if (parts.Length < 3 || !parts[0].Equals("http-get", StringComparison.OrdinalIgnoreCase)) continue;
                        string mime = parts[2].ToLowerInvariant();
                        if (!mime.StartsWith("video/", StringComparison.Ordinal)) continue;
                        // Several <res> are usually the original plus transcodes; the original comes first and has a size.
                        if (best == null || (best.GetAttribute("size").Length == 0 && res.GetAttribute("size").Length > 0))
                        {
                            best = res;
                            bestMime = mime;
                        }
                    }
                    if (best == null) continue;
                    string url = best.InnerText.Trim();
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
                    long.TryParse(best.GetAttribute("size"), out long size);
                    int.TryParse((First(e, "date")?.InnerText ?? string.Empty).Split('-')[0], out int year);
                    result.Add(new NetworkItem
                    {
                        Name = FileNameFor(title, uri, bestMime),
                        Title = MediaMatcher.IsVideoFile(title) ? null : title,
                        Location = url,
                        Size = size,
                        Poster = art,
                        Year = year > 1800 && year < 2200 ? year : 0,
                    });
                }
            }
            return result;
        }

        /// <summary>Title plus a video extension (taken from the URL or the MIME type), so 3D detection and matching work.</summary>
        public static string FileNameFor(string title, Uri url, string mime)
        {
            if (MediaMatcher.IsVideoFile(title)) return title;
            string ext = Path.GetExtension(Uri.UnescapeDataString(url.AbsolutePath)).ToLowerInvariant();
            if (!MediaMatcher.IsVideoFile("x" + ext))
            {
                switch (mime)
                {
                    case "video/x-matroska":
                    case "video/x-mkv":
                    case "video/mkv": ext = ".mkv"; break;
                    case "video/webm": ext = ".webm"; break;
                    case "video/quicktime": ext = ".mov"; break;
                    case "video/mp2t":
                    case "video/vnd.dlna.mpeg-tts":
                    case "video/mpeg": ext = ".ts"; break;
                    case "video/3gpp": ext = ".3gp"; break;
                    default: ext = ".mp4"; break;
                }
            }
            return title + ext;
        }

        // ---------------- XML helpers (namespace-agnostic) ----------------

        static XmlDocument LoadXml(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), settings))
                doc.Load(reader);
            return doc;
        }

        internal static XmlElement First(XmlNode root, string localName)
        {
            foreach (var e in All(root, localName))
                return e;
            return null;
        }

        internal static IEnumerable<XmlElement> All(XmlNode root, string localName)
        {
            if (root == null) yield break;
            foreach (XmlNode child in root.ChildNodes)
            {
                if (!(child is XmlElement e)) continue;
                if (e.LocalName == localName) yield return e;
                foreach (var nested in All(e, localName))
                    yield return nested;
            }
        }
    }
}
