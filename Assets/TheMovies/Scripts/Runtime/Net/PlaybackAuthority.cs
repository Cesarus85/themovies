using System;
using TheMovies.Core;
using Unity.Collections;
using UnityEngine;

namespace TheMovies.Net
{
    /// <summary>
    /// The source of truth for which movie plays and where. In a shared cinema this is
    /// <see cref="CinemaSync"/> (host authoritative); alone it is <see cref="LocalAuthority"/>.
    /// </summary>
    public interface IPlaybackAuthority
    {
        bool IsNetworked { get; }
        MediaDescriptor Media { get; }
        PlaybackTimeline Timeline { get; }

        /// <summary>Current time on the host clock, the time base of <see cref="Timeline"/>.</summary>
        double HostNow { get; }

        /// <summary>False until the clock offset to the host is known.</summary>
        bool ClockReady { get; }

        /// <summary>Last measured round trip to the host in seconds.</summary>
        double RoundTrip { get; }

        void RequestPlay();
        void RequestPause();
        void RequestSeek(double mediaTime);
        void RequestSeekRelative(double delta);
        void RequestSelectMedia(MediaEntry entry, StereoFormat format);
        void RequestFormat(StereoFormat format, bool swapEyes);

        /// <summary>Ends the current movie for everyone (back to "no movie").</summary>
        void RequestStop();

        /// <summary>The selecting participant reports the length once the movie is prepared.</summary>
        void ReportDuration(int mediaVersion, double duration);
    }

    public static class Playback
    {
        /// <summary>Start delay so every participant receives the command before the movie starts.</summary>
        public const double StartDelay = 0.45;

        public static IPlaybackAuthority Current =>
            CinemaSync.Instance != null && CinemaSync.Instance.IsSpawned ? (IPlaybackAuthority)CinemaSync.Instance : LocalAuthority.Instance;

        public static MediaDescriptor Describe(MediaEntry entry, StereoFormat format, int version)
        {
            var media = new MediaDescriptor
            {
                Version = version,
                IsUrl = entry.IsUrl,
                Size = entry.Size,
                Format = (byte)format,
            };
            media.Key = Truncate512(entry.MatchKey);
            media.Title = Truncate128(entry.Title);
            return media;
        }

        /// <summary>Stream URLs must fit into the network message.</summary>
        public static bool FitsNetwork(MediaEntry entry) =>
            System.Text.Encoding.UTF8.GetByteCount(entry.MatchKey ?? string.Empty) <= FixedString512Bytes.UTF8MaxLengthInBytes;

        static FixedString512Bytes Truncate512(string s) =>
            new FixedString512Bytes(TruncateUtf8(s, FixedString512Bytes.UTF8MaxLengthInBytes));

        static FixedString128Bytes Truncate128(string s) =>
            new FixedString128Bytes(TruncateUtf8(s, FixedString128Bytes.UTF8MaxLengthInBytes));

        public static string TruncateUtf8(string s, int maxBytes)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var enc = System.Text.Encoding.UTF8;
            if (enc.GetByteCount(s) <= maxBytes) return s;
            int len = s.Length;
            while (len > 0 && enc.GetByteCount(s.Substring(0, len)) > maxBytes)
                len--;
            if (len > 0 && char.IsHighSurrogate(s[len - 1])) len--;
            return s.Substring(0, len);
        }
    }

    /// <summary>Playback authority when watching alone (no network session).</summary>
    public sealed class LocalAuthority : IPlaybackAuthority
    {
        public static readonly LocalAuthority Instance = new LocalAuthority();

        MediaDescriptor m_Media;
        PlaybackTimeline m_Timeline;
        int m_LastVersion;
        readonly AutoWait m_AutoWait = new AutoWait();

        /// <summary>True while paused automatically because the stream is still buffering.</summary>
        public bool WaitingForBuffer => m_AutoWait.Waiting;

        /// <summary>Called every frame with whether this device has enough of the movie to play on.</summary>
        public void UpdateBuffering(bool ready)
        {
            if (m_Media.IsEmpty) return;
            double now = HostNow;
            switch (m_AutoWait.Update(m_Timeline.Playing && !m_Timeline.IsPreRoll(now), ready, now))
            {
                case AutoWaitAction.Pause:
                    m_Timeline = m_Timeline.WithPause(now);
                    break;
                case AutoWaitAction.Resume:
                    m_Timeline = m_Timeline.WithPlay(now, 0.05);
                    break;
            }
        }

        public bool IsNetworked => false;
        public MediaDescriptor Media => m_Media;
        public PlaybackTimeline Timeline => m_Timeline;
        public double HostNow => Time.realtimeSinceStartupAsDouble;
        public bool ClockReady => true;
        public double RoundTrip => 0;

        public void RequestPlay()
        {
            m_AutoWait.Cancel();
            if (m_Media.IsEmpty || m_Timeline.Playing) return;
            m_Timeline = m_Timeline.WithPlay(HostNow, 0.05);
        }

        public void RequestPause()
        {
            m_AutoWait.Cancel();
            if (!m_Timeline.Playing) return;
            m_Timeline = m_Timeline.WithPause(HostNow);
        }

        public void RequestSeek(double mediaTime)
        {
            if (m_Media.IsEmpty) return;
            m_Timeline = m_Timeline.WithSeek(HostNow, Clamp(mediaTime), 0.05);
        }

        public void RequestSeekRelative(double delta) => RequestSeek(m_Timeline.MediaTimeAt(HostNow) + delta);

        public void RequestSelectMedia(MediaEntry entry, StereoFormat format)
        {
            m_AutoWait.Cancel();
            m_LastVersion = Math.Max(m_LastVersion, m_Media.Version) + 1;
            m_Media = Playback.Describe(entry, format, m_LastVersion);
            m_Timeline = new PlaybackTimeline { Revision = m_Timeline.Revision + 1 };
        }

        public void RequestStop()
        {
            m_AutoWait.Cancel();
            m_LastVersion = Math.Max(m_LastVersion, m_Media.Version);
            m_Media = default;
            m_Timeline = new PlaybackTimeline { Revision = m_Timeline.Revision + 1 };
        }

        public void RequestFormat(StereoFormat format, bool swapEyes)
        {
            m_Media.Format = (byte)format;
            m_Media.SwapEyes = swapEyes;
        }

        public void ReportDuration(int mediaVersion, double duration)
        {
            if (mediaVersion == m_Media.Version && duration > 0)
                m_Media.Duration = duration;
        }

        /// <summary>Stops at the end of the movie.</summary>
        public void Tick()
        {
            if (m_Timeline.Playing && m_Media.Duration > 0 && m_Timeline.MediaTimeAt(HostNow) > m_Media.Duration + 0.5)
                m_Timeline = m_Timeline.WithSeek(HostNow, m_Media.Duration, 0).WithPause(HostNow);
        }

        /// <summary>Called when a session starts so the host can continue what it was watching.</summary>
        public void Export(out MediaDescriptor media, out PlaybackTimeline timeline)
        {
            media = m_Media;
            timeline = m_Timeline;
        }

        /// <summary>
        /// Takes over a movie from a session that broke away (host migration): keeps it running at
        /// <paramref name="position"/> without reloading, until the new host's session continues it.
        /// </summary>
        public void Import(MediaDescriptor media, double position, bool playing)
        {
            m_AutoWait.Cancel();
            m_LastVersion = Math.Max(m_LastVersion, media.Version);
            m_Media = media;
            m_Timeline = new PlaybackTimeline
            {
                Playing = playing && !media.IsEmpty,
                AnchorMediaTime = Math.Max(0, position),
                AnchorHostTime = HostNow,
                Revision = m_Timeline.Revision + 1,
            };
        }

        public void Clear()
        {
            m_LastVersion = Math.Max(m_LastVersion, m_Media.Version);
            m_Media = default;
            m_Timeline = default;
        }

        double Clamp(double t)
        {
            t = Math.Max(0, t);
            return m_Media.Duration > 0 ? Math.Min(t, m_Media.Duration) : t;
        }
    }
}
