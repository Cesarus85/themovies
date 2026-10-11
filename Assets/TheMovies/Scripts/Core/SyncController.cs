using System;

namespace TheMovies.Core
{
    /// <summary>What the local video player currently reports.</summary>
    public struct LocalPlayerState
    {
        public double MediaTime;
        public bool IsPlaying;
        public bool IsSeeking;

        /// <summary>Media length in seconds, 0 if unknown (e.g. live streams).</summary>
        public double Duration;

        public bool CanSetSpeed;

        /// <summary>Measured time a seek takes on this device, used to aim ahead while playing.</summary>
        public double SeekLatency;
    }

    /// <summary>What the local video player should do this frame.</summary>
    public struct SyncCommand
    {
        public bool ShouldPlay;
        public bool Seek;
        public double SeekTarget;
        public float Speed;

        /// <summary>Local minus expected media time (positive = ahead).</summary>
        public double Drift;

        /// <summary>The expected media time right now.</summary>
        public double Target;

        public bool ReachedEnd;
    }

    public sealed class SyncSettings
    {
        /// <summary>Above this drift (seconds) the player jumps instead of speeding up/slowing down.</summary>
        public double HardSeekThreshold = 1.0;

        /// <summary>Drift (seconds) at which gentle speed correction starts.</summary>
        public double SoftCorrectionStart = 0.06;

        /// <summary>Drift (seconds) at which gentle speed correction stops again (hysteresis).</summary>
        public double SoftCorrectionStop = 0.015;

        /// <summary>Speed change per second of drift.</summary>
        public double SpeedGain = 0.5;

        /// <summary>Maximum relative speed change (0.05 = ±5%).</summary>
        public double MaxSpeedAdjust = 0.05;

        /// <summary>When the platform cannot change speed, seek above this drift.</summary>
        public double SeekThresholdWithoutSpeed = 0.25;

        /// <summary>Allowed position error while paused before re-seeking.</summary>
        public double PausedTolerance = 0.08;

        /// <summary>Minimum time between two corrective seeks while playing.</summary>
        public double SeekCooldown = 2.0;

        /// <summary>Upper bound for the seek-ahead compensation.</summary>
        public double MaxSeekLead = 1.5;

        public bool AllowSpeedCorrection = true;
    }

    /// <summary>
    /// Decides how the local player follows the shared <see cref="PlaybackTimeline"/>.
    /// Pure logic so it can be unit tested outside of Unity.
    /// </summary>
    public sealed class SyncController
    {
        public const double EndMargin = 0.05;

        public SyncSettings Settings { get; }

        bool m_Correcting;
        double m_LastSeekAt = double.NegativeInfinity;

        public SyncController(SyncSettings settings = null)
        {
            Settings = settings ?? new SyncSettings();
        }

        /// <summary>Forget correction state, e.g. after loading another movie.</summary>
        public void Reset()
        {
            m_Correcting = false;
            m_LastSeekAt = double.NegativeInfinity;
        }

        public SyncCommand Update(in PlaybackTimeline timeline, double hostNow, in LocalPlayerState local)
        {
            var s = Settings;
            var cmd = new SyncCommand { Speed = 1f };

            bool preRoll = timeline.IsPreRoll(hostNow);
            double target = timeline.MediaTimeAt(hostNow);

            if (local.Duration > 0 && target >= local.Duration - EndMargin)
            {
                // Park just before the last frame; seeking exactly to the end can stop some decoders.
                target = Math.Max(0, local.Duration - EndMargin);
                cmd.ReachedEnd = true;
            }

            cmd.Target = target;
            cmd.Drift = local.MediaTime - target;
            cmd.ShouldPlay = timeline.Playing && !preRoll && !cmd.ReachedEnd;

            // A seek is in flight: wait for it, the reported time is meaningless meanwhile.
            if (local.IsSeeking)
            {
                m_Correcting = false;
                return cmd;
            }

            double absDrift = Math.Abs(cmd.Drift);

            if (!timeline.Playing || preRoll || cmd.ReachedEnd)
            {
                // Paused, waiting for a scheduled start, or at the end: park on the exact frame.
                m_Correcting = false;
                if (absDrift > s.PausedTolerance)
                    RequestSeek(ref cmd, target, hostNow);
                return cmd;
            }

            bool cooledDown = hostNow - m_LastSeekAt >= s.SeekCooldown;
            bool canSpeed = s.AllowSpeedCorrection && local.CanSetSpeed;

            if (absDrift > s.HardSeekThreshold && (cooledDown || absDrift > s.HardSeekThreshold * 4))
            {
                double lead = Math.Min(Math.Max(local.SeekLatency, 0), s.MaxSeekLead);
                double seekTarget = target + lead;
                if (local.Duration > 0)
                    seekTarget = Math.Min(seekTarget, local.Duration);
                m_Correcting = false;
                RequestSeek(ref cmd, seekTarget, hostNow);
                return cmd;
            }

            if (canSpeed)
            {
                if (!m_Correcting && absDrift > s.SoftCorrectionStart)
                    m_Correcting = true;
                else if (m_Correcting && absDrift < s.SoftCorrectionStop)
                    m_Correcting = false;

                if (m_Correcting)
                {
                    double adjust = Clamp(-cmd.Drift * s.SpeedGain, -s.MaxSpeedAdjust, s.MaxSpeedAdjust);
                    cmd.Speed = (float)(1.0 + adjust);
                }
            }
            else if (absDrift > s.SeekThresholdWithoutSpeed && cooledDown)
            {
                double lead = Math.Min(Math.Max(local.SeekLatency, 0), s.MaxSeekLead);
                RequestSeek(ref cmd, target + lead, hostNow);
            }

            return cmd;
        }

        void RequestSeek(ref SyncCommand cmd, double target, double hostNow)
        {
            cmd.Seek = true;
            cmd.SeekTarget = Math.Max(0, target);
            m_LastSeekAt = hostNow;
        }

        static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
