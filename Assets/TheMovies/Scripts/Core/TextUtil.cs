using System;
using System.Text;

namespace TheMovies.Core
{
    public static class TextUtil
    {
        /// <summary>"1:05:09" for long media, "5:09" for short media.</summary>
        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                seconds = 0;
            long total = (long)Math.Floor(seconds);
            long h = total / 3600;
            long m = total / 60 % 60;
            long s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return string.Empty;
            const double gb = 1024.0 * 1024 * 1024;
            const double mb = 1024.0 * 1024;
            return bytes >= gb
                ? (bytes / gb).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB"
                : (bytes / mb).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        }

        /// <summary>Cuts a string to the given length, adding an ellipsis.</summary>
        public static string Ellipsize(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s) || maxChars <= 0) return string.Empty;
            if (s.Length <= maxChars) return s;
            if (maxChars <= 1) return s.Substring(0, maxChars);
            return s.Substring(0, maxChars - 1) + "…";
        }

        /// <summary>Normalizes a room code typed by the user: upper case, only letters and digits.</summary>
        public static string NormalizeRoomCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            var sb = new StringBuilder(code.Length);
            foreach (char c in code.ToUpperInvariant())
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Accepts IPv4 addresses and host names (no scheme, no port).</summary>
        public static bool IsPlausibleHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253) return false;
            foreach (char c in host)
                if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-'))
                    return false;
            return !host.StartsWith(".", StringComparison.Ordinal) && !host.EndsWith(".", StringComparison.Ordinal);
        }

        /// <summary>Removes characters that the voice service does not allow in channel names.</summary>
        public static string SafeChannelName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? string.Empty)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : "TheMovies";
        }
    }
}
