using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>
    /// Remembers where each movie was left off. Keyed by the movie's match key (file name or URL).
    /// Stored as simple tab separated text so it is easy to inspect.
    /// </summary>
    public sealed class ResumeStore
    {
        /// <summary>Positions closer than this to the start are not worth resuming.</summary>
        public const double MinimumPosition = 60;

        /// <summary>Positions closer than this to the end count as "finished".</summary>
        public const double EndMargin = 180;

        public const int MaxEntries = 200;

        struct Entry
        {
            public double Position;
            public double Duration;
            public long UpdatedTicks;
        }

        readonly Dictionary<string, Entry> m_Entries = new Dictionary<string, Entry>();

        public int Count => m_Entries.Count;

        public bool IsDirty { get; private set; }

        /// <summary>Records the position; finished movies (and the first minute) clear the entry.</summary>
        public void Record(string key, double position, double duration, DateTime now)
        {
            if (string.IsNullOrEmpty(key) || double.IsNaN(position)) return;
            bool finished = duration > 0 && position > duration - EndMargin;
            if (position < MinimumPosition || finished)
            {
                if (m_Entries.Remove(key)) IsDirty = true;
                return;
            }
            m_Entries[key] = new Entry { Position = position, Duration = duration, UpdatedTicks = now.Ticks };
            IsDirty = true;
            Trim();
        }

        /// <returns>The saved position, or null if there is nothing to resume.</returns>
        public double? Get(string key)
        {
            if (string.IsNullOrEmpty(key) || !m_Entries.TryGetValue(key, out var e)) return null;
            return e.Position;
        }

        public void Remove(string key)
        {
            if (key != null && m_Entries.Remove(key)) IsDirty = true;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var kv in m_Entries)
            {
                if (kv.Key.IndexOf('\t') >= 0 || kv.Key.IndexOf('\n') >= 0) continue;
                sb.Append(kv.Value.UpdatedTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(kv.Value.Position.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(kv.Value.Duration.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(kv.Key).Append('\n');
            }
            IsDirty = false;
            return sb.ToString();
        }

        public static ResumeStore Deserialize(string text)
        {
            var store = new ResumeStore();
            if (string.IsNullOrEmpty(text)) return store;
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Split(new[] { '\t' }, 4);
                if (parts.Length != 4) continue;
                if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double position)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double duration)) continue;
                string key = parts[3].TrimEnd('\r');
                if (key.Length == 0) continue;
                store.m_Entries[key] = new Entry { Position = position, Duration = duration, UpdatedTicks = ticks };
            }
            store.Trim();
            store.IsDirty = false;
            return store;
        }

        void Trim()
        {
            while (m_Entries.Count > MaxEntries)
            {
                string oldest = null;
                long oldestTicks = long.MaxValue;
                foreach (var kv in m_Entries)
                {
                    if (kv.Value.UpdatedTicks < oldestTicks)
                    {
                        oldestTicks = kv.Value.UpdatedTicks;
                        oldest = kv.Key;
                    }
                }
                m_Entries.Remove(oldest);
            }
        }
    }
}
