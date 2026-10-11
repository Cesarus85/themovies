using System;
using System.Collections.Generic;
using System.Text;

namespace TheMovies.Core
{
    /// <summary>How the two eye images are packed into a video frame.</summary>
    public enum StereoFormat : byte
    {
        /// <summary>Normal 2D video, both eyes see the same image.</summary>
        Mono = 0,

        /// <summary>Side by side, each eye squeezed to half width (most common for 3D downloads, "HSBS").</summary>
        SideBySideHalf = 1,

        /// <summary>Side by side, each eye at full resolution (frame is twice as wide, e.g. 3840x1080).</summary>
        SideBySideFull = 2,

        /// <summary>Top/bottom (over/under), each eye squeezed to half height ("HOU", "HTAB").</summary>
        TopBottomHalf = 3,

        /// <summary>Top/bottom, each eye at full resolution (frame is twice as tall).</summary>
        TopBottomFull = 4,
    }

    /// <summary>Texture scale (X,Y) and offset (Z,W) for one eye.</summary>
    public struct UvRect
    {
        public float ScaleX, ScaleY, OffsetX, OffsetY;

        public UvRect(float scaleX, float scaleY, float offsetX, float offsetY)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public override string ToString() => $"({ScaleX}, {ScaleY}, {OffsetX}, {OffsetY})";
    }

    public static class StereoFormats
    {
        public static readonly StereoFormat[] All =
        {
            StereoFormat.Mono,
            StereoFormat.SideBySideHalf,
            StereoFormat.SideBySideFull,
            StereoFormat.TopBottomHalf,
            StereoFormat.TopBottomFull,
        };

        public static bool IsStereo(this StereoFormat f) => f != StereoFormat.Mono;

        public static string DisplayName(this StereoFormat f)
        {
            switch (f)
            {
                case StereoFormat.Mono: return "2D";
                case StereoFormat.SideBySideHalf: return "3D SBS (Half)";
                case StereoFormat.SideBySideFull: return "3D SBS (Full)";
                case StereoFormat.TopBottomHalf: return "3D OU (Half)";
                case StereoFormat.TopBottomFull: return "3D OU (Full)";
                default: return f.ToString();
            }
        }

        public static StereoFormat Next(this StereoFormat f)
        {
            int i = Array.IndexOf(All, f);
            return All[(i + 1) % All.Length];
        }

        /// <summary>Aspect ratio (width/height) of the picture one eye sees.</summary>
        public static double DisplayAspect(this StereoFormat f, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return 16.0 / 9.0;
            double w = width, h = height;
            switch (f)
            {
                case StereoFormat.SideBySideFull: return w * 0.5 / h;
                case StereoFormat.TopBottomFull: return w / (h * 0.5);
                default: return w / h; // half formats are shown stretched back to the full frame
            }
        }

        /// <summary>
        /// UV transform for an eye (0 = left, 1 = right). For top/bottom the left eye is the upper half;
        /// in texture space V=1 is the top, so the left eye gets offset 0.5.
        /// </summary>
        public static UvRect EyeUv(this StereoFormat f, int eye, bool swapEyes)
        {
            bool left = (eye == 0) != swapEyes;
            switch (f)
            {
                case StereoFormat.SideBySideHalf:
                case StereoFormat.SideBySideFull:
                    return new UvRect(0.5f, 1f, left ? 0f : 0.5f, 0f);
                case StereoFormat.TopBottomHalf:
                case StereoFormat.TopBottomFull:
                    return new UvRect(1f, 0.5f, 0f, left ? 0.5f : 0f);
                default:
                    return new UvRect(1f, 1f, 0f, 0f);
            }
        }

        /// <summary>
        /// Guesses the stereo format from typical file name tags ("HSBS", "3D.SBS", "Half-OU", "TAB", ...)
        /// and, when known, from the frame size. Returns <see cref="StereoFormat.Mono"/> if nothing hints at 3D.
        /// </summary>
        public static StereoFormat Detect(string fileName, int width = 0, int height = 0)
        {
            double aspect = width > 0 && height > 0 ? (double)width / height : 0;
            var tokens = Tokenize(fileName ?? string.Empty);

            bool sbs = false, tb = false, half = false, full = false, is3D = false;
            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i];
                string prev = i > 0 ? tokens[i - 1] : string.Empty;
                bool prevHalf = prev == "half" || prev == "h";
                bool prevFull = prev == "full" || prev == "f";

                switch (t)
                {
                    case "hsbs":
                    case "halfsbs":
                        sbs = half = true;
                        break;
                    case "fsbs":
                    case "fullsbs":
                        sbs = full = true;
                        break;
                    case "sbs":
                    case "3dsbs":
                    case "sidebyside":
                    case "lr":
                        sbs = true;
                        half |= prevHalf;
                        full |= prevFull;
                        break;
                    case "hou":
                    case "htab":
                    case "htb":
                    case "halfou":
                    case "halftab":
                        tb = half = true;
                        break;
                    case "fou":
                    case "ftab":
                    case "fullou":
                    case "fulltab":
                        tb = full = true;
                        break;
                    case "ou":
                    case "tab":
                    case "tb":
                    case "3dou":
                    case "3dtab":
                    case "overunder":
                    case "topbottom":
                        tb = true;
                        half |= prevHalf;
                        full |= prevFull;
                        break;
                    case "3d":
                        is3D = true;
                        break;
                }

                // "side by side", "over under", "top bottom" written as separate words
                if (t == "side" && i + 2 < tokens.Count && tokens[i + 1] == "by" && tokens[i + 2] == "side") sbs = true;
                if (t == "over" && i + 1 < tokens.Count && tokens[i + 1] == "under") tb = true;
                if (t == "top" && i + 1 < tokens.Count && (tokens[i + 1] == "bottom" || tokens[i + 1] == "and")) tb = true;
            }

            if (sbs)
            {
                if (full) return StereoFormat.SideBySideFull;
                if (half) return StereoFormat.SideBySideHalf;
                return aspect >= 3.0 ? StereoFormat.SideBySideFull : StereoFormat.SideBySideHalf;
            }

            if (tb)
            {
                if (full) return StereoFormat.TopBottomFull;
                if (half) return StereoFormat.TopBottomHalf;
                return aspect > 0 && aspect <= 1.2 ? StereoFormat.TopBottomFull : StereoFormat.TopBottomHalf;
            }

            if (is3D && aspect > 0)
            {
                if (aspect >= 3.0) return StereoFormat.SideBySideFull;
                if (aspect <= 1.2) return StereoFormat.TopBottomFull;
                return StereoFormat.SideBySideHalf;
            }

            return StereoFormat.Mono;
        }

        static List<string> Tokenize(string s)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            foreach (char c in s.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                }
            }
            if (sb.Length > 0)
                tokens.Add(sb.ToString());
            return tokens;
        }
    }
}
