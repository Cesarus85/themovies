namespace TheMovies.Core
{
    public enum AutoWaitAction
    {
        None,
        Pause,
        Resume,
    }

    /// <summary>
    /// Pauses playback when the picture can't keep up (stream buffering) and resumes once there is enough
    /// reserve again. A pause by the user is never resumed automatically.
    /// </summary>
    public sealed class AutoWait
    {
        /// <summary>Not ready for this long while playing → pause.</summary>
        public double PauseAfter = 1.0;

        /// <summary>Ready again for this long while auto-paused → resume.</summary>
        public double ResumeAfter = 1.0;

        double m_NotReadySince = -1;
        double m_ReadySince = -1;

        /// <summary>True while paused automatically.</summary>
        public bool Waiting { get; private set; }

        public AutoWaitAction Update(bool playing, bool ready, double now)
        {
            if (playing)
            {
                m_ReadySince = -1;
                Waiting = false;
                if (ready)
                {
                    m_NotReadySince = -1;
                    return AutoWaitAction.None;
                }
                if (m_NotReadySince < 0)
                {
                    m_NotReadySince = now;
                    return AutoWaitAction.None;
                }
                if (now - m_NotReadySince < PauseAfter) return AutoWaitAction.None;
                m_NotReadySince = -1;
                Waiting = true;
                return AutoWaitAction.Pause;
            }

            m_NotReadySince = -1;
            if (!Waiting || !ready)
            {
                m_ReadySince = -1;
                return AutoWaitAction.None;
            }
            if (m_ReadySince < 0)
            {
                m_ReadySince = now;
                return AutoWaitAction.None;
            }
            if (now - m_ReadySince < ResumeAfter) return AutoWaitAction.None;
            m_ReadySince = -1;
            Waiting = false;
            return AutoWaitAction.Resume;
        }

        /// <summary>The user paused, played or picked something else: forget the automatic pause.</summary>
        public void Cancel()
        {
            Waiting = false;
            m_NotReadySince = -1;
            m_ReadySince = -1;
        }
    }
}
