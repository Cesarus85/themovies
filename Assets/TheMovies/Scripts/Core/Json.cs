using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>
    /// Small JSON reader/writer for the media server APIs (Jellyfin, Plex). Objects become
    /// <see cref="Dictionary{TKey,TValue}"/>, arrays <see cref="List{T}"/>, numbers double, plus string/bool/null.
    /// </summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            int pos = 0;
            object value = ParseValue(text ?? string.Empty, ref pos);
            SkipWhitespace(text, ref pos);
            if (pos != text.Length) throw new FormatException("Unerwartete Zeichen nach JSON-Ende bei " + pos);
            return value;
        }

        // ---------------- Access helpers ----------------

        public static object Get(this object node, string key) =>
            node is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v : null;

        /// <summary>Path like "MediaContainer.Metadata" or "Items".</summary>
        public static object Path(this object node, string path)
        {
            foreach (string part in path.Split('.'))
                node = node.Get(part);
            return node;
        }

        public static string Str(this object node, string key, string fallback = "")
        {
            var v = node.Get(key);
            switch (v)
            {
                case null: return fallback;
                case string s: return s;
                case double d: return d.ToString(CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                default: return fallback;
            }
        }

        public static long Long(this object node, string key, long fallback = 0)
        {
            var v = node.Get(key);
            if (v is double d) return (long)d;
            if (v is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
            return fallback;
        }

        public static bool Bool(this object node, string key, bool fallback = false)
        {
            var v = node.Get(key);
            if (v is bool b) return b;
            if (v is double d) return d != 0;
            if (v is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
            return fallback;
        }

        public static List<object> Array(this object node, string key = null)
        {
            var v = key == null ? node : node.Get(key);
            return v as List<object> ?? new List<object>();
        }

        // ---------------- Writer ----------------

        public static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        // ---------------- Parser ----------------

        static object ParseValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new FormatException("JSON endet unerwartet");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject(s, ref pos);
                case '[': return ParseArray(s, ref pos);
                case '"': return ParseString(s, ref pos);
                case 't': Expect(s, ref pos, "true"); return true;
                case 'f': Expect(s, ref pos, "false"); return false;
                case 'n': Expect(s, ref pos, "null"); return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref pos);
                    throw new FormatException($"Unerwartetes Zeichen '{c}' bei {pos}");
            }
        }

        static Dictionary<string, object> ParseObject(string s, ref int pos)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            pos++;
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == '}')
            {
                pos++;
                return result;
            }
            while (true)
            {
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != '"') throw new FormatException("Schlüssel erwartet bei " + pos);
                string key = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length || s[pos] != ':') throw new FormatException("':' erwartet bei " + pos);
                pos++;
                result[key] = ParseValue(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) throw new FormatException("JSON endet unerwartet");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return result; }
                throw new FormatException("',' oder '}' erwartet bei " + pos);
            }
        }

        static List<object> ParseArray(string s, ref int pos)
        {
            var result = new List<object>();
            pos++;
            SkipWhitespace(s, ref pos);
            if (pos < s.Length && s[pos] == ']')
            {
                pos++;
                return result;
            }
            while (true)
            {
                result.Add(ParseValue(s, ref pos));
                SkipWhitespace(s, ref pos);
                if (pos >= s.Length) throw new FormatException("JSON endet unerwartet");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return result; }
                throw new FormatException("',' oder ']' erwartet bei " + pos);
            }
        }

        static string ParseString(string s, ref int pos)
        {
            var sb = new StringBuilder();
            pos++;
            while (pos < s.Length)
            {
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (pos >= s.Length) break;
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw new FormatException("Ungültiges \\u-Zeichen");
                        sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        pos += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("Zeichenkette nicht abgeschlossen");
        }

        static double ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0) pos++;
            return double.Parse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static void Expect(string s, ref int pos, string word)
        {
            if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0) throw new FormatException(word + " erwartet bei " + pos);
            pos += word.Length;
        }

        static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }
    }
}
