using System;
using System.Collections.Generic;
using System.Text;

namespace TheMovies.Core
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
    }

    /// <summary>The last log lines of the app (thread-safe ring buffer) for the in-app log page.</summary>
    public sealed class LogBuffer
    {
        public struct Entry
        {
            public DateTime Time;
            public LogLevel Level;
            public string Text;

            public override string ToString() =>
                Time.ToString("HH:mm:ss") + " " + (Level == LogLevel.Error ? "F" : Level == LogLevel.Warning ? "W" : "·") + " " + Text;
        }

        readonly Entry[] m_Entries;
        readonly object m_Lock = new object();
        int m_Next;
        int m_Count;
        int m_Version;

        public LogBuffer(int capacity = 2000)
        {
            m_Entries = new Entry[Math.Max(16, capacity)];
        }

        /// <summary>Changes with every new entry (lets the menu skip redrawing).</summary>
        public int Version
        {
            get { lock (m_Lock) return m_Version; }
        }

        public void Add(LogLevel level, string text, DateTime time)
        {
            text = (text ?? string.Empty).Replace("\r", string.Empty);
            lock (m_Lock)
            {
                // Multi-line messages (stack traces) become several entries, so pages stay readable.
                foreach (string line in text.Split('\n'))
                {
                    if (line.Trim().Length == 0) continue;
                    m_Entries[m_Next] = new Entry { Time = time, Level = level, Text = line };
                    m_Next = (m_Next + 1) % m_Entries.Length;
                    if (m_Count < m_Entries.Length) m_Count++;
                }
                m_Version++;
            }
        }

        /// <summary>Oldest first; with <paramref name="problemsOnly"/> only warnings and errors.</summary>
        public List<Entry> Snapshot(bool problemsOnly = false)
        {
            var result = new List<Entry>();
            lock (m_Lock)
            {
                int start = (m_Next - m_Count + m_Entries.Length) % m_Entries.Length;
                for (int i = 0; i < m_Count; i++)
                {
                    var e = m_Entries[(start + i) % m_Entries.Length];
                    if (!problemsOnly || e.Level != LogLevel.Info) result.Add(e);
                }
            }
            return result;
        }

        /// <summary>The lines of one page, counted from the newest end (page 0 = newest lines).</summary>
        public static List<string> Page(List<Entry> entries, int page, int linesPerPage, int maxChars)
        {
            var lines = new List<string>();
            int end = entries.Count - page * linesPerPage;
            int start = Math.Max(0, end - linesPerPage);
            for (int i = start; i < end; i++)
                lines.Add(TextUtil.Ellipsize(entries[i].ToString(), maxChars));
            return lines;
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            foreach (var e in Snapshot()) sb.Append(e).Append('\n');
            return sb.ToString();
        }
    }
}
