using System;
using System.Text;

namespace TheMovies.Core
{
    public enum ListingKind
    {
        Online,
        Lan,
    }

    /// <summary>
    /// An open cinema that can be joined without a code: from the online session list or announced in the local
    /// network. The movie title is only there if the host chose to show it.
    /// </summary>
    public sealed class CinemaListing
    {
        public ListingKind Kind;

        /// <summary>Session id (online) or IP address (LAN).</summary>
        public string Id = string.Empty;

        public string HostName = string.Empty;
        public string Room = string.Empty;
        public int Players;
        public int MaxPlayers;
        public string Title = string.Empty;

        /// <summary>When it was last seen (realtime seconds), to drop LAN cinemas that went quiet.</summary>
        public double SeenAt;

        public bool IsFull => MaxPlayers > 0 && Players >= MaxPlayers;
    }

    /// <summary>
    /// Finding cinemas in the local network without typing an IP address: a searching device broadcasts a short
    /// query on <see cref="Port"/>, open LAN cinemas answer it directly (unicast) with their name, room and seats.
    /// </summary>
    public static class LanDiscovery
    {
        public const int Port = 7778;
        const string k_Query = "TheMovies?1";
        const string k_Answer = "TheMovies!1";
        const int k_MaxField = 60;

        public static byte[] Query() => Encoding.ASCII.GetBytes(k_Query);

        public static bool IsQuery(byte[] data, int length) =>
            data != null && length == k_Query.Length && Encoding.ASCII.GetString(data, 0, length) == k_Query;

        public static byte[] Answer(string hostName, string room, int players, int maxPlayers, string title)
        {
            var text = new StringBuilder(k_Answer);
            foreach (string field in new[] { hostName, room, players.ToString(), maxPlayers.ToString(), title })
                text.Append('\n').Append(Clean(field));
            return Encoding.UTF8.GetBytes(text.ToString());
        }

        /// <summary>The cinema in an answer from <paramref name="address"/>, or null if it is none.</summary>
        public static CinemaListing ParseAnswer(byte[] data, int length, string address)
        {
            if (data == null || length <= k_Answer.Length || length > 2048) return null;
            string text;
            try
            {
                text = Encoding.UTF8.GetString(data, 0, length);
            }
            catch (ArgumentException)
            {
                return null;
            }
            var parts = text.Split('\n');
            if (parts.Length < 6 || parts[0] != k_Answer) return null;
            if (!int.TryParse(parts[3], out int players) || !int.TryParse(parts[4], out int max)) return null;
            return new CinemaListing
            {
                Kind = ListingKind.Lan,
                Id = address ?? string.Empty,
                HostName = Clean(parts[1]),
                Room = Clean(parts[2]),
                Players = Math.Max(0, players),
                MaxPlayers = Math.Max(0, max),
                Title = Clean(parts[5]),
            };
        }

        static string Clean(string value)
        {
            value = (value ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            return value.Length > k_MaxField ? value.Substring(0, k_MaxField) : value;
        }
    }
}
