using System;
using System.Collections.Generic;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>
    /// Fits text into a width measured with real character widths (the menu font is proportional, so counting
    /// characters lets "WWW" overflow while "iii" leaves gaps). Long words such as file names
    /// ("Avatar.2009.3D.HSBS") are broken at dots, underscores and dashes, if necessary anywhere.
    /// </summary>
    public static class TextFit
    {
        public const string Ellipsis = "…";

        public static float Width(string text, Func<char, float> charWidth)
        {
            float w = 0;
            foreach (char c in text ?? string.Empty) w += charWidth(c);
            return w;
        }

        /// <summary>The text, shortened with "…" if it is wider than <paramref name="maxWidth"/>.</summary>
        public static string Ellipsize(string text, float maxWidth, Func<char, float> charWidth)
        {
            if (string.IsNullOrEmpty(text) || Width(text, charWidth) <= maxWidth) return text ?? string.Empty;
            float budget = maxWidth - charWidth(Ellipsis[0]);
            var sb = new StringBuilder();
            float w = 0;
            foreach (char c in text)
            {
                float cw = charWidth(c);
                if (w + cw > budget) break;
                sb.Append(c);
                w += cw;
            }
            return sb.ToString().TrimEnd(' ', '.', '_', '-') + Ellipsis;
        }

        /// <summary>Breaks the text into at most <paramref name="maxLines"/> lines; the last one ends with "…" if text is left over.</summary>
        public static string Wrap(string text, float maxWidth, int maxLines, Func<char, float> charWidth)
        {
            if (string.IsNullOrEmpty(text) || maxLines <= 0) return string.Empty;
            var lines = new List<string>();
            var line = new StringBuilder();
            float lineWidth = 0;
            int pos = 0;
            while (pos < text.Length)
            {
                // Next piece: a word including its trailing break character ("Avatar." / "HSBS-" / "word ").
                int end = pos;
                while (end < text.Length && !IsBreak(text[end])) end++;
                if (end < text.Length) end++;
                string piece = text.Substring(pos, end - pos);
                string visible = piece.TrimEnd(' ');
                float pieceWidth = Width(visible, charWidth);

                if (line.Length > 0 && lineWidth + pieceWidth > maxWidth)
                {
                    lines.Add(line.ToString().TrimEnd(' '));
                    line.Clear();
                    lineWidth = 0;
                    if (lines.Count == maxLines) break;
                }
                if (line.Length == 0 && pieceWidth > maxWidth)
                {
                    // Too long even alone: cut it character by character.
                    int taken = 0;
                    float w = 0;
                    while (taken < visible.Length && w + charWidth(visible[taken]) <= maxWidth)
                        w += charWidth(visible[taken++]);
                    taken = Math.Max(1, taken);
                    lines.Add(visible.Substring(0, taken));
                    if (lines.Count == maxLines)
                    {
                        pos += taken;
                        break;
                    }
                    pos += taken;
                    continue;
                }
                line.Append(piece);
                lineWidth += Width(piece, charWidth);
                pos = end;
            }
            if (line.Length > 0 && lines.Count < maxLines)
            {
                lines.Add(line.ToString().TrimEnd(' '));
                line.Clear();
            }
            bool leftOver = pos < text.Length || line.Length > 0;
            if (leftOver && lines.Count > 0)
            {
                // Text did not fit: the last line ends with "…".
                int last = lines.Count - 1;
                string l = lines[last].TrimEnd(' ', '.', '_', '-');
                float ellipsis = charWidth(Ellipsis[0]);
                while (l.Length > 0 && Width(l, charWidth) + ellipsis > maxWidth) l = l.Substring(0, l.Length - 1);
                lines[last] = l.TrimEnd(' ', '.', '_', '-') + Ellipsis;
            }
            return string.Join("\n", lines);
        }

        static bool IsBreak(char c) => c == ' ' || c == '.' || c == '_' || c == '-' || c == ',' || c == ')';
    }
}
