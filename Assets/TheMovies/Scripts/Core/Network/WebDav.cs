using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Xml;

namespace TheMovies.Core.Network
{
    /// <summary>WebDAV folder listing (PROPFIND, depth 1).</summary>
    public static class WebDav
    {
        const string PropfindBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:displayname/><d:resourcetype/><d:getcontentlength/></d:prop></d:propfind>";

        public static List<NetworkItem> List(HttpClient http, string folderUrl)
        {
            if (!folderUrl.EndsWith("/", StringComparison.Ordinal)) folderUrl += "/";
            using (var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), folderUrl))
            {
                request.Headers.TryAddWithoutValidation("Depth", "1");
                request.Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml");
                using (var response = http.SendAsync(request).GetAwaiter().GetResult())
                {
                    NetHttp.ThrowIfFailed(response);
                    if ((int)response.StatusCode != 207)
                        throw new IOException("Das ist kein WebDAV-Ordner (keine Multi-Status-Antwort).");
                    string xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return ParseMultiStatus(xml, new Uri(folderUrl));
                }
            }
        }

        /// <summary>Parses a 207 Multi-Status answer; the folder itself is left out.</summary>
        public static List<NetworkItem> ParseMultiStatus(string xml, Uri folder)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                doc.Load(reader);

            string self = NormalizePath(folder.AbsolutePath);
            var result = new List<NetworkItem>();
            foreach (var response in Dlna.All(doc.DocumentElement, "response"))
            {
                string href = Dlna.First(response, "href")?.InnerText.Trim();
                if (string.IsNullOrEmpty(href) || !Uri.TryCreate(folder, href, out var uri)) continue;
                if (NormalizePath(uri.AbsolutePath) == self) continue;

                bool okFound = false;
                bool isFolder = false;
                long size = 0;
                foreach (var propstat in Dlna.All(response, "propstat"))
                {
                    string status = Dlna.First(propstat, "status")?.InnerText ?? "200";
                    if (status.IndexOf(" 200", StringComparison.Ordinal) < 0 && status != "200") continue;
                    okFound = true;
                    var type = Dlna.First(propstat, "resourcetype");
                    if (type != null && Dlna.First(type, "collection") != null) isFolder = true;
                    long.TryParse(Dlna.First(propstat, "getcontentlength")?.InnerText.Trim(), out long len);
                    if (len > 0) size = len;
                }
                if (!okFound) continue;

                string name = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/'));
                name = name.Substring(name.LastIndexOf('/') + 1);
                if (name.Length == 0 || name.StartsWith(".", StringComparison.Ordinal)) continue;
                string location = uri.GetLeftPart(UriPartial.Path);
                if (isFolder && !location.EndsWith("/", StringComparison.Ordinal)) location += "/";
                result.Add(new NetworkItem { Name = name, Location = location, IsFolder = isFolder, Size = size });
            }
            return result;
        }

        static string NormalizePath(string path) => Uri.UnescapeDataString(path ?? string.Empty).TrimEnd('/').ToLowerInvariant();
    }
}
