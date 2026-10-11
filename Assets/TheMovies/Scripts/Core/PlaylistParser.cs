using System;
using System.Collections.Generic;

namespace TheMovies.Core
{
    /// <summary>
    /// Parses the optional "urls.txt" playlist. One entry per line:
    /// <code>
    /// # comment
    /// https://example.com/movie.mp4
    /// Big Buck Bunny | https://example.com/bbb.mp4
    /// Avatar 3D | https://example.com/avatar.mp4 | sbs
    /// </code>
    /// The optional third column is a format: 2d, sbs, hsbs, fsbs, ou, hou, fou.
    /// </summary>
    public static class PlaylistParser
    {
        public static List<MediaEntry> Parse(string text)
        {
            var result = new List<MediaEntry>();
            if (string.IsNullOrEmpty(text))
                return result;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split('|');
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = parts[i].Trim();

                string title, url;
                StereoFormat? format = null;

                if (parts.Length == 1)
                {
                    url = parts[0];
                    title = TitleFromUrl(url);
                }
                else
                {
                    title = parts[0];
                    url = parts[1];
                    if (parts.Length > 2)
                        format = ParseFormat(parts[2]);
                }

                if (!MediaMatcher.LooksLikeUrl(url))
                    continue;
                if (string.IsNullOrEmpty(title))
                    title = TitleFromUrl(url);

                result.Add(new MediaEntry { Title = title, Location = url, IsUrl = true, FormatHint = format });
            }

            return result;
        }

        public static StereoFormat? ParseFormat(string s)
        {
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "2d":
                case "mono":
                    return StereoFormat.Mono;
                case "sbs":
                case "hsbs":
                case "halfsbs":
                    return StereoFormat.SideBySideHalf;
                case "fsbs":
                case "fullsbs":
                    return StereoFormat.SideBySideFull;
                case "ou":
                case "tab":
                case "hou":
                case "htab":
                    return StereoFormat.TopBottomHalf;
                case "fou":
                case "ftab":
                    return StereoFormat.TopBottomFull;
                default:
                    return null;
            }
        }

        static string TitleFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string last = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : uri.Host;
                last = Uri.UnescapeDataString(last).Trim('/');
                return last.Length > 0 ? last : uri.Host;
            }
            catch (UriFormatException)
            {
                return url;
            }
        }
    }
}
