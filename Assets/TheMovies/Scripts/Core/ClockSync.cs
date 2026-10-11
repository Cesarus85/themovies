using System;
using System.Collections.Generic;

namespace TheMovies.Core
{
    /// <summary>
    /// NTP-style estimator for the offset between the local clock and the host clock.
    /// Each sample is a ping: local send time, host time when the host answered, local receive time.
    /// Samples with the lowest round trip time are the most trustworthy, so the estimate is the
    /// average offset of the fastest recent samples.
    /// </summary>
    public sealed class ClockSync
    {
        struct Sample
        {
            public double Offset;
            public double Rtt;
        }

        readonly int m_MaxSamples;
        readonly List<Sample> m_Samples = new List<Sample>();

        public ClockSync(int maxSamples = 24)
        {
            if (maxSamples < 1) throw new ArgumentOutOfRangeException(nameof(maxSamples));
            m_MaxSamples = maxSamples;
        }

        /// <summary>True once at least one valid sample was recorded.</summary>
        public bool HasEstimate { get; private set; }

        /// <summary>hostTime = localTime + Offset.</summary>
        public double Offset { get; private set; }

        /// <summary>Round trip time of the best (fastest) recent sample in seconds.</summary>
        public double BestRtt { get; private set; }

        /// <summary>Round trip time of the most recent sample in seconds.</summary>
        public double LastRtt { get; private set; }

        public int SampleCount => m_Samples.Count;

        public void Reset()
        {
            m_Samples.Clear();
            HasEstimate = false;
            Offset = 0;
            BestRtt = 0;
            LastRtt = 0;
        }

        /// <returns>False if the sample was rejected (negative round trip).</returns>
        public bool AddSample(double localSendTime, double hostTime, double localReceiveTime)
        {
            double rtt = localReceiveTime - localSendTime;
            if (rtt < 0 || double.IsNaN(rtt) || double.IsInfinity(rtt))
                return false;

            // The host stamped its clock roughly half way through the round trip.
            double offset = hostTime + rtt * 0.5 - localReceiveTime;

            m_Samples.Add(new Sample { Offset = offset, Rtt = rtt });
            if (m_Samples.Count > m_MaxSamples)
                m_Samples.RemoveAt(0);

            LastRtt = rtt;
            Recompute();
            return true;
        }

        public double ToHost(double localTime) => localTime + Offset;

        public double ToLocal(double hostTime) => hostTime - Offset;

        void Recompute()
        {
            double minRtt = double.MaxValue;
            foreach (var s in m_Samples)
                minRtt = Math.Min(minRtt, s.Rtt);

            // Accept samples that are not much slower than the best one.
            double limit = minRtt * 1.5 + 0.004;
            double sum = 0;
            int count = 0;
            foreach (var s in m_Samples)
            {
                if (s.Rtt <= limit)
                {
                    sum += s.Offset;
                    count++;
                }
            }

            Offset = sum / count;
            BestRtt = minRtt;
            HasEstimate = true;
        }
    }
}
