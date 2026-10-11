using System;
using System.Collections.Generic;

namespace TheMovies.Core
{
    public enum MediaTrackType
    {
        Audio,
        Subtitle,
    }

    /// <summary>An audio or subtitle track inside the movie file (own player).</summary>
    public sealed class MediaTrack
    {
        public MediaTrackType Type;
        public string Language = string.Empty;
        public string Label = string.Empty;
        public bool Selected;
        public bool Supported = true;
        public int Group;
        public int Index;

        public string LanguageName => Languages.Name(Language);

        public string DisplayName
        {
            get
            {
                string text = LanguageName;
                if (Label.Length > 0) text += " · " + Label;
                if (!Supported) text += " (nicht abspielbar)";
                return text;
            }
        }

        /// <summary>Parses the player's list: "a|de|AC3 5.1|1|1|2|0" per line (type|lang|label|selected|supported|group|track).</summary>
        public static List<MediaTrack> Parse(string text)
        {
            var result = new List<MediaTrack>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string line in text.Split('\n'))
            {
                string[] f = line.Split('|');
                if (f.Length < 7 || (f[0] != "a" && f[0] != "t")) continue;
                if (!int.TryParse(f[5], out int group) || !int.TryParse(f[6], out int index)) continue;
                result.Add(new MediaTrack
                {
                    Type = f[0] == "a" ? MediaTrackType.Audio : MediaTrackType.Subtitle,
                    Language = f[1],
                    Label = f[2].Trim(),
                    Selected = f[3] == "1",
                    Supported = f[4] == "1",
                    Group = group,
                    Index = index,
                });
            }
            return result;
        }
    }

    public static class Languages
    {
        static readonly Dictionary<string, string> k_Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "de", "Deutsch" }, { "deu", "Deutsch" }, { "ger", "Deutsch" }, { "gsw", "Schweizerdeutsch" },
            { "en", "Englisch" }, { "eng", "Englisch" },
            { "fr", "Französisch" }, { "fra", "Französisch" }, { "fre", "Französisch" },
            { "es", "Spanisch" }, { "spa", "Spanisch" },
            { "it", "Italienisch" }, { "ita", "Italienisch" },
            { "nl", "Niederländisch" }, { "nld", "Niederländisch" }, { "dut", "Niederländisch" },
            { "pl", "Polnisch" }, { "pol", "Polnisch" },
            { "pt", "Portugiesisch" }, { "por", "Portugiesisch" },
            { "ru", "Russisch" }, { "rus", "Russisch" },
            { "tr", "Türkisch" }, { "tur", "Türkisch" },
            { "ja", "Japanisch" }, { "jpn", "Japanisch" },
            { "ko", "Koreanisch" }, { "kor", "Koreanisch" },
            { "zh", "Chinesisch" }, { "zho", "Chinesisch" }, { "chi", "Chinesisch" },
            { "sv", "Schwedisch" }, { "swe", "Schwedisch" }, { "da", "Dänisch" }, { "dan", "Dänisch" },
            { "no", "Norwegisch" }, { "nor", "Norwegisch" }, { "fi", "Finnisch" }, { "fin", "Finnisch" },
            { "cs", "Tschechisch" }, { "ces", "Tschechisch" }, { "cze", "Tschechisch" },
            { "hu", "Ungarisch" }, { "hun", "Ungarisch" }, { "el", "Griechisch" }, { "gre", "Griechisch" }, { "ell", "Griechisch" },
        };

        public static string Name(string code)
        {
            if (string.IsNullOrEmpty(code) || code == "und") return "Unbekannt";
            string key = code;
            int dash = key.IndexOfAny(new[] { '-', '_' });
            if (dash > 0) key = key.Substring(0, dash);
            return k_Names.TryGetValue(key, out string name) ? name : code;
        }
    }
}
