using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheMovies.Core
{
    public struct SubtitleCue
    {
        public double Start;
        public double End;
        public string Text;
    }

    /// <summary>A parsed subtitle track with fast lookup by time.</summary>
    public sealed class SubtitleTrack
    {
        readonly List<SubtitleCue> m_Cues;
        int m_LastIndex;

        public SubtitleTrack(List<SubtitleCue> cues)
        {
            m_Cues = cues ?? new List<SubtitleCue>();
            m_Cues.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        public int Count => m_Cues.Count;
        public IReadOnlyList<SubtitleCue> Cues => m_Cues;

        /// <summary>Text visible at <paramref name="time"/> (several overlapping cues are joined), or null.</summary>
        public string TextAt(double time)
        {
            if (m_Cues.Count == 0) return null;

            // Playback mostly moves forward: start searching at the last hit.
            int i = m_LastIndex;
            if (i >= m_Cues.Count || m_Cues[i].Start > time)
                i = FindFirstStartingAfter(time) - 1;
            while (i + 1 < m_Cues.Count && m_Cues[i + 1].Start <= time)
                i++;
            if (i < 0) return null;
            m_LastIndex = i;

            string result = null;
            // Look back a few cues for overlaps (cues are sorted by start, not by end).
            for (int k = Math.Max(0, i - 3); k <= i; k++)
            {
                var cue = m_Cues[k];
                if (time >= cue.Start && time < cue.End)
                    result = result == null ? cue.Text : result + "\n" + cue.Text;
            }
            return result;
        }

        int FindFirstStartingAfter(double time)
        {
            int lo = 0, hi = m_Cues.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (m_Cues[mid].Start <= time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }

    /// <summary>Reads SubRip (.srt) subtitles, tolerant of the usual sloppiness in downloaded files.</summary>
    public static class SrtParser
    {
        static readonly Regex k_Timing = new Regex(
            @"(\d{1,2}):(\d{1,2}):(\d{1,2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{1,2}):(\d{1,2})[,.](\d{1,3})",
            RegexOptions.Compiled);

        static readonly Regex k_Tags = new Regex(@"<[^>]*>|\{\\[^}]*\}", RegexOptions.Compiled);

        public static SubtitleTrack Parse(string text)
        {
            var cues = new List<SubtitleCue>();
            if (string.IsNullOrEmpty(text))
                return new SubtitleTrack(cues);

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var match = k_Timing.Match(lines[i]);
                if (!match.Success) continue;

                double start = ToSeconds(match, 1);
                double end = ToSeconds(match, 5);
                var body = new StringBuilder();
                int j = i + 1;
                for (; j < lines.Length; j++)
                {
                    string line = lines[j].Trim();
                    if (line.Length == 0) break;
                    // A missing blank line before the next cue: number line followed by a timing line
                    if (j + 1 < lines.Length && IsNumber(line) && k_Timing.IsMatch(lines[j + 1])) break;
                    if (body.Length > 0) body.Append('\n');
                    body.Append(CleanLine(line));
                }
                i = j - 1;

                string cueText = body.ToString().Trim();
                if (cueText.Length > 0 && end > start)
                    cues.Add(new SubtitleCue { Start = start, End = end, Text = cueText });
            }
            return new SubtitleTrack(cues);
        }

        /// <summary>
        /// Decodes subtitle bytes: UTF-8 (with or without BOM), UTF-16 with BOM, otherwise Latin-1/Windows-1252
        /// (common for German subtitles) so that umlauts survive.
        /// </summary>
        public static string Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                return utf8.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                var chars = new char[bytes.Length];
                for (int i = 0; i < bytes.Length; i++)
                    chars[i] = Windows1252(bytes[i]);
                return new string(chars);
            }
        }

        /// <summary>Breaks long lines so they fit on the screen (subtitle lines are kept, long ones wrapped).</summary>
        public static string Wrap(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return text;
            var result = new StringBuilder();
            foreach (string line in text.Split('\n'))
            {
                if (result.Length > 0) result.Append('\n');
                int lineLength = 0;
                foreach (string word in line.Split(' '))
                {
                    if (word.Length == 0) continue;
                    if (lineLength > 0 && lineLength + 1 + word.Length > maxChars)
                    {
                        result.Append('\n');
                        lineLength = 0;
                    }
                    else if (lineLength > 0)
                    {
                        result.Append(' ');
                        lineLength++;
                    }
                    result.Append(word);
                    lineLength += word.Length;
                }
            }
            return result.ToString();
        }

        static string CleanLine(string line) => k_Tags.Replace(line, string.Empty).Trim();

        static bool IsNumber(string s)
        {
            foreach (char c in s)
                if (!char.IsDigit(c)) return false;
            return s.Length > 0;
        }

        static double ToSeconds(Match m, int group)
        {
            int h = int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);
            int min = int.Parse(m.Groups[group + 1].Value, CultureInfo.InvariantCulture);
            int s = int.Parse(m.Groups[group + 2].Value, CultureInfo.InvariantCulture);
            string msText = m.Groups[group + 3].Value.PadRight(3, '0');
            int ms = int.Parse(msText, CultureInfo.InvariantCulture);
            return h * 3600 + min * 60 + s + ms / 1000.0;
        }

        static char Windows1252(byte b)
        {
            // 0x80-0x9F differ from Latin-1; map the printable ones that occur in subtitles.
            switch (b)
            {
                case 0x80: return '€';
                case 0x84: return '„';
                case 0x85: return '…';
                case 0x91: return '‘';
                case 0x92: return '’';
                case 0x93: return '“';
                case 0x94: return '”';
                case 0x96: return '–';
                case 0x97: return '—';
                default: return (char)b;
            }
        }
    }
}
