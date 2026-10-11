using System;

namespace TheMovies.Core
{
    /// <summary>
    /// The shared playback timeline. Everything is expressed in host clock seconds, so every
    /// participant can compute where the movie should be right now without further messages:
    /// while playing, media time advances 1:1 with host time starting at the anchor.
    /// </summary>
    public struct PlaybackTimeline : IEquatable<PlaybackTimeline>
    {
        public bool Playing;

        /// <summary>Media position (seconds) at <see cref="AnchorHostTime"/>.</summary>
        public double AnchorMediaTime;

        /// <summary>Host clock time at which the media is at <see cref="AnchorMediaTime"/>.
        /// May lie slightly in the future so that everyone can start at the same moment.</summary>
        public double AnchorHostTime;

        /// <summary>Incremented on every change, handy for UI and debugging.</summary>
        public int Revision;

        public static PlaybackTimeline Stopped => new PlaybackTimeline();

        /// <summary>Where the media should be at the given host time.</summary>
        public double MediaTimeAt(double hostTime)
        {
            if (!Playing)
                return AnchorMediaTime;
            return AnchorMediaTime + Math.Max(0.0, hostTime - AnchorHostTime);
        }

        /// <summary>True while playing but the scheduled start lies in the future.</summary>
        public bool IsPreRoll(double hostTime) => Playing && hostTime < AnchorHostTime;

        public PlaybackTimeline WithPlay(double hostNow, double startDelay)
        {
            var next = this;
            next.AnchorMediaTime = MediaTimeAt(hostNow);
            next.AnchorHostTime = hostNow + Math.Max(0, startDelay);
            next.Playing = true;
            next.Revision++;
            return next;
        }

        public PlaybackTimeline WithPause(double hostNow)
        {
            var next = this;
            next.AnchorMediaTime = MediaTimeAt(hostNow);
            next.AnchorHostTime = hostNow;
            next.Playing = false;
            next.Revision++;
            return next;
        }

        public PlaybackTimeline WithSeek(double hostNow, double mediaTime, double startDelay)
        {
            var next = this;
            next.AnchorMediaTime = Math.Max(0, mediaTime);
            next.AnchorHostTime = Playing ? hostNow + Math.Max(0, startDelay) : hostNow;
            next.Revision++;
            return next;
        }

        public bool Equals(PlaybackTimeline other) =>
            Playing == other.Playing &&
            AnchorMediaTime.Equals(other.AnchorMediaTime) &&
            AnchorHostTime.Equals(other.AnchorHostTime) &&
            Revision == other.Revision;

        public override bool Equals(object obj) => obj is PlaybackTimeline other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Playing ? 1 : 0;
                h = h * 397 ^ AnchorMediaTime.GetHashCode();
                h = h * 397 ^ AnchorHostTime.GetHashCode();
                h = h * 397 ^ Revision;
                return h;
            }
        }

        public override string ToString() =>
            $"{(Playing ? "Play" : "Pause")} media={AnchorMediaTime:0.000} @host={AnchorHostTime:0.000} rev={Revision}";
    }
}
