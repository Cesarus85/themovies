using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>A playable item: a local file, a stream URL or a file on a NAS / network share.</summary>
    public sealed class MediaEntry
    {
        /// <summary>Display name.</summary>
        public string Title;

        /// <summary>Absolute file path or URL.</summary>
        public string Location;

        public bool IsUrl;

        /// <summary>File size in bytes, 0 if unknown.</summary>
        public long Size;

        /// <summary>Format override from a playlist entry; null means auto detection.</summary>
        public StereoFormat? FormatHint;

        /// <summary>Key of the network source (NAS) the file lives on; null for files on this device and stream URLs.</summary>
        public string SourceId;

        /// <summary>File name when it can't be taken from <see cref="Location"/> (network items).</summary>
        public string Name;

        /// <summary>Poster image (URL, local path or network location), null if none is known.</summary>
        public string Poster;

        /// <summary>File on a NAS / network share. Treated like a local file for matching (others look for the same file name).</summary>
        public bool IsNetwork => SourceId != null;

        /// <summary>The key other participants use to find their own copy: file name for files, URL for streams.</summary>
        public string MatchKey => IsUrl ? Location : FileName;

        public string FileName => IsUrl ? Location : Name ?? Path.GetFileName(Location);

        public override string ToString() => $"{Title} ({Location})";
    }

    public static class MediaMatcher
    {
        /// <summary>
        /// Finds the local copy of a movie someone else selected. Prefers same name and size,
        /// then same name, then same size, then a name that only differs in punctuation/case/extension.
        /// </summary>
        public static MediaEntry FindBest(string wantedName, long wantedSize, IEnumerable<MediaEntry> candidates)
        {
            if (string.IsNullOrEmpty(wantedName) || candidates == null)
                return null;

            string wantedNorm = Normalize(Path.GetFileNameWithoutExtension(wantedName));
            MediaEntry nameAndSize = null, nameOnly = null, sizeOnly = null, fuzzy = null;

            foreach (var c in candidates)
            {
                if (c == null || c.IsUrl)
                    continue;

                bool sameName = string.Equals(c.FileName, wantedName, StringComparison.OrdinalIgnoreCase);
                bool sameSize = wantedSize > 0 && c.Size == wantedSize;

                if (sameName && sameSize)
                {
                    nameAndSize = c;
                    break;
                }
                if (sameName && nameOnly == null) nameOnly = c;
                if (sameSize && sizeOnly == null) sizeOnly = c;
                if (fuzzy == null && wantedNorm.Length > 0 &&
                    Normalize(Path.GetFileNameWithoutExtension(c.FileName)) == wantedNorm)
                    fuzzy = c;
            }

            return nameAndSize ?? nameOnly ?? sizeOnly ?? fuzzy;
        }

        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.ToString();
        }

        public static bool IsVideoFile(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            switch (ext)
            {
                case ".mp4":
                case ".m4v":
                case ".mkv":
                case ".webm":
                case ".mov":
                case ".3gp":
                case ".ts":
                    return true;
                default:
                    return false;
            }
        }

        public static bool LooksLikeUrl(string s) =>
            !string.IsNullOrEmpty(s) &&
            (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             s.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
    }
}
