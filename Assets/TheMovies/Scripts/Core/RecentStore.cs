using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>The movies watched last (newest first), for the "Zuletzt" tab and "Weiterschauen" on the start page.</summary>
    public sealed class RecentStore
    {
        public const int MaxEntries = 30;
        const string Version = "r1";

        readonly List<(MediaEntry entry, long ticks)> m_Entries = new List<(MediaEntry, long)>();

        public bool IsDirty { get; private set; }

        public int Count => m_Entries.Count;

        public List<MediaEntry> Entries
        {
            get
            {
                var result = new List<MediaEntry>(m_Entries.Count);
                foreach (var e in m_Entries) result.Add(e.entry);
                return result;
            }
        }

        public MediaEntry Latest => m_Entries.Count > 0 ? m_Entries[0].entry : null;

        static string KeyOf(MediaEntry e) => (e.SourceId ?? string.Empty) + "\n" + (e.IsUrl ? e.Location : e.FileName);

        /// <summary>Puts the movie at the top (replacing an older entry of the same movie).</summary>
        public void Add(MediaEntry entry, DateTime now)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Location)) return;
            string key = KeyOf(entry);
            m_Entries.RemoveAll(e => KeyOf(e.entry) == key);
            var copy = new MediaEntry
            {
                Title = entry.Title, Location = entry.Location, IsUrl = entry.IsUrl, Size = entry.Size,
                SourceId = entry.SourceId, Name = entry.Name, Poster = entry.Poster, FormatHint = entry.FormatHint,
            };
            m_Entries.Insert(0, (copy, now.Ticks));
            if (m_Entries.Count > MaxEntries) m_Entries.RemoveRange(MaxEntries, m_Entries.Count - MaxEntries);
            IsDirty = true;
        }

        public void Remove(MediaEntry entry)
        {
            if (entry == null) return;
            string key = KeyOf(entry);
            if (m_Entries.RemoveAll(e => KeyOf(e.entry) == key) > 0) IsDirty = true;
        }

        public void Clear()
        {
            if (m_Entries.Count == 0) return;
            m_Entries.Clear();
            IsDirty = true;
        }

        static string Esc(string s) => Uri.EscapeDataString(s ?? string.Empty);

        static string Unesc(string s) => s.Length == 0 ? null : Uri.UnescapeDataString(s);

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var (e, ticks) in m_Entries)
            {
                sb.Append(Version).Append('\t')
                  .Append(ticks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.IsUrl ? '1' : '0').Append('\t')
                  .Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(Esc(e.Title)).Append('\t')
                  .Append(Esc(e.Location)).Append('\t')
                  .Append(Esc(e.SourceId)).Append('\t')
                  .Append(Esc(e.Name)).Append('\t')
                  .Append(Esc(e.Poster)).Append('\t')
                  .Append(e.FormatHint.HasValue ? ((int)e.FormatHint.Value).ToString(CultureInfo.InvariantCulture) : string.Empty)
                  .Append('\n');
            }
            IsDirty = false;
            return sb.ToString();
        }

        public static RecentStore Deserialize(string text)
        {
            var store = new RecentStore();
            if (string.IsNullOrEmpty(text)) return store;
            foreach (string raw in text.Split('\n'))
            {
                string[] f = raw.TrimEnd('\r').Split('\t');
                if (f.Length < 10 || f[0] != Version) continue;
                try
                {
                    if (!long.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) continue;
                    long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size);
                    var e = new MediaEntry
                    {
                        IsUrl = f[2] == "1",
                        Size = size,
                        Title = Unesc(f[4]) ?? string.Empty,
                        Location = Unesc(f[5]),
                        SourceId = Unesc(f[6]),
                        Name = Unesc(f[7]),
                        Poster = Unesc(f[8]),
                    };
                    if (byte.TryParse(f[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte format) && Enum.IsDefined(typeof(StereoFormat), format))
                        e.FormatHint = (StereoFormat)format;
                    if (string.IsNullOrEmpty(e.Location) || store.m_Entries.Count >= MaxEntries) continue;
                    store.m_Entries.Add((e, ticks));
                }
                catch (UriFormatException)
                {
                    // Skip broken lines
                }
            }
            return store;
        }
    }
}
