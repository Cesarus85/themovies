using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using TheMovies.Core.Network;

namespace TheMovies.Core
{
    /// <summary>Title, year and poster of a movie from a Kodi .nfo file.</summary>
    public sealed class NfoInfo
    {
        public string Title;
        public int Year;

        /// <summary>Poster URL from the .nfo, null if none.</summary>
        public string Thumb;
    }

    /// <summary>
    /// Finds posters and .nfo files next to movies, the way Kodi, Jellyfin and Plex store them:
    /// "Film-poster.jpg", "Film.jpg", "Film.tbn", "Film.nfo", and for a folder holding one movie
    /// "poster.jpg", "folder.jpg", "cover.jpg", "movie.nfo".
    /// </summary>
    public static class Artwork
    {
        static readonly string[] k_ImageExtensions = { ".jpg", ".jpeg", ".png", ".tbn" };
        static readonly string[] k_MovieSuffixes = { "-poster", "-cover", "", "-thumb", ".poster" };
        static readonly string[] k_FolderNames = { "poster", "folder", "cover", "movie", "default" };

        public static bool IsImageFile(string name)
        {
            string ext = Path.GetExtension(name ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(k_ImageExtensions, ext) >= 0;
        }

        /// <summary>Assigns <see cref="NetworkItem.Poster"/> and <see cref="NetworkItem.Nfo"/> of the movies in a folder listing.</summary>
        public static void AttachSidecars(List<NetworkItem> items)
        {
            var files = new Dictionary<string, NetworkItem>(StringComparer.OrdinalIgnoreCase);
            int videos = 0;
            foreach (var i in items)
            {
                if (i.IsFolder) continue;
                files[i.Name] = i;
                if (MediaMatcher.IsVideoFile(i.Name)) videos++;
            }
            if (files.Count <= videos) return; // nothing but movies here

            foreach (var item in items)
            {
                if (item.IsFolder || !MediaMatcher.IsVideoFile(item.Name)) continue;
                string stem = Path.GetFileNameWithoutExtension(item.Name);
                if (item.Poster == null)
                {
                    item.Poster = Find(files, stem, k_MovieSuffixes, k_ImageExtensions);
                    if (item.Poster == null && videos == 1) item.Poster = Find(files, null, k_FolderNames, k_ImageExtensions);
                }
                if (item.Nfo == null)
                {
                    item.Nfo = Find(files, stem, new[] { "" }, new[] { ".nfo" });
                    if (item.Nfo == null && videos == 1) item.Nfo = Find(files, null, new[] { "movie" }, new[] { ".nfo" });
                }
            }
        }

        /// <summary>Poster for a folder tile from the folder's listing: poster.jpg/folder.jpg/…, else the poster of its only movie.</summary>
        public static string FolderPoster(List<NetworkItem> children)
        {
            var files = new Dictionary<string, NetworkItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in children)
                if (!i.IsFolder) files[i.Name] = i;
            string poster = Find(files, null, k_FolderNames, k_ImageExtensions);
            if (poster != null) return poster;
            AttachSidecars(children);
            NetworkItem only = null;
            foreach (var i in children)
            {
                if (i.IsFolder || !MediaMatcher.IsVideoFile(i.Name)) continue;
                if (only != null) return null;
                only = i;
            }
            return only?.Poster;
        }

        static string Find(Dictionary<string, NetworkItem> files, string stem, string[] names, string[] extensions)
        {
            foreach (string name in names)
                foreach (string ext in extensions)
                    if (files.TryGetValue((stem ?? string.Empty) + name + ext, out var hit))
                        return hit.Location;
            return null;
        }

        /// <summary>Reads a Kodi movie/episode .nfo; null if it is no XML (some only hold an IMDb link).</summary>
        public static NfoInfo ParseNfo(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int start = text.IndexOf('<');
            if (start < 0) return null;
            // Some tools append the IMDb URL after the XML: only parse up to the end of the root element.
            int end = text.LastIndexOf('>');
            if (end <= start) return null;
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
                using (var reader = XmlReader.Create(new StringReader(text.Substring(start, end - start + 1)), settings))
                    doc.Load(reader);
            }
            catch (XmlException)
            {
                return null;
            }
            var root = doc.DocumentElement;
            if (root == null) return null;
            var info = new NfoInfo();
            foreach (XmlNode node in root.ChildNodes)
            {
                if (!(node is XmlElement e)) continue;
                string value = e.InnerText.Trim();
                switch (e.LocalName)
                {
                    case "title":
                        if (info.Title == null && value.Length > 0) info.Title = value;
                        break;
                    case "year":
                        if (int.TryParse(value, out int year) && year > 1800 && year < 2200) info.Year = year;
                        break;
                    case "premiered":
                    case "aired":
                        if (info.Year == 0 && value.Length >= 4 && int.TryParse(value.Substring(0, 4), out int y) && y > 1800 && y < 2200) info.Year = y;
                        break;
                    case "thumb":
                        string aspect = e.GetAttribute("aspect");
                        if (MediaMatcher.LooksLikeUrl(value) && (info.Thumb == null || aspect == "poster") && (aspect.Length == 0 || aspect == "poster"))
                            info.Thumb = value;
                        break;
                }
            }
            return info.Title == null && info.Year == 0 && info.Thumb == null ? null : info;
        }
    }
}
