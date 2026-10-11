using System;

namespace TheMovies.Core
{
    /// <summary>
    /// Turns the raw output of a depth model (relative inverse depth, larger = nearer, any scale) into a calm
    /// depth map for 2D→3D: 0 = far, 1 = near, rows bottom-up as a texture wants them.
    /// - Range from the 2nd to the 98th percentile (single bright or dark pixels don't squash the rest)
    /// - Range and map are smoothed over time, except at scene cuts
    /// - Detects whether the model's rows arrive top-down or bottom-up: in almost every film the lower part
    ///   of the picture is nearer (floor, table, people) than the upper part (sky, ceiling, background)
    /// </summary>
    public sealed class DepthNormalizer
    {
        public readonly int Width;
        public readonly int Height;

        readonly float[] m_Map;
        readonly float[] m_Sample;
        float m_Low, m_High;
        bool m_HasRange;
        bool m_HasMap;
        int m_FlipVotes;
        int m_Votes;

        /// <summary>How much of the new map is taken per update (the rest is the previous one).</summary>
        public float Smoothing = 0.55f;

        /// <summary>Mean change (0..1) above which a new map counts as a scene cut and replaces the old one.</summary>
        public float CutThreshold = 0.22f;

        /// <summary>Model rows arrive top-down (true) or bottom-up; decided from the first maps, then fixed.</summary>
        public bool TopDown { get; private set; } = true;

        public bool OrientationDecided => m_Votes >= 8;

        public DepthNormalizer(int width, int height)
        {
            Width = width;
            Height = height;
            m_Map = new float[width * height];
            m_Sample = new float[(width * height + 3) / 4];
        }

        /// <summary>The current map, 0 (far) .. 1 (near), row 0 = bottom.</summary>
        public float[] Map => m_Map;

        /// <summary>Takes a raw model output (row-major, <see cref="Width"/> × <see cref="Height"/>).</summary>
        public void Add(float[] raw)
        {
            if (raw == null || raw.Length < Width * Height) throw new ArgumentException("Tiefenkarte hat die falsche Größe.");

            // Robust range from every 4th value.
            int n = 0;
            for (int i = 0; i < Width * Height && n < m_Sample.Length; i += 4)
                m_Sample[n++] = raw[i];
            Array.Sort(m_Sample, 0, n);
            float low = m_Sample[(int)(n * 0.02f)];
            float high = m_Sample[Math.Min(n - 1, (int)(n * 0.98f))];
            if (high - low < 1e-6f) high = low + 1e-6f;

            bool cut = false;
            if (m_HasRange)
            {
                float span = Math.Max(m_High - m_Low, 1e-6f);
                cut = Math.Abs(low - m_Low) / span > 0.5f || Math.Abs(high - m_High) / span > 0.5f;
            }
            if (!m_HasRange || cut)
            {
                m_Low = low;
                m_High = high;
                m_HasRange = true;
            }
            else
            {
                m_Low += (low - m_Low) * 0.3f;
                m_High += (high - m_High) * 0.3f;
            }

            if (!OrientationDecided) VoteOrientation(raw);

            float scale = 1f / (m_High - m_Low);
            double change = 0;
            for (int y = 0; y < Height; y++)
            {
                int sourceRow = TopDown ? Height - 1 - y : y;
                for (int x = 0; x < Width; x++)
                {
                    float v = (raw[sourceRow * Width + x] - m_Low) * scale;
                    v = v < 0 ? 0 : v > 1 ? 1 : v;
                    int i = y * Width + x;
                    if (m_HasMap) change += Math.Abs(v - m_Map[i]);
                    m_Map[i] = v;
                }
            }
            change /= Width * Height;
            if (m_HasMap && change < CutThreshold && !cut)
            {
                // Not a cut: blend with the previous map (stored in m_Previous below).
                for (int i = 0; i < m_Map.Length; i++)
                    m_Map[i] = m_Previous[i] + (m_Map[i] - m_Previous[i]) * Smoothing;
            }
            Array.Copy(m_Map, m_Previous ??= new float[m_Map.Length], m_Map.Length);
            m_HasMap = true;
        }

        float[] m_Previous;

        void VoteOrientation(float[] raw)
        {
            int third = Math.Max(1, Height / 3);
            double first = 0, last = 0;
            for (int y = 0; y < third; y++)
            for (int x = 0; x < Width; x++)
            {
                first += raw[y * Width + x];
                last += raw[(Height - 1 - y) * Width + x];
            }
            if (Math.Abs(first - last) < 1e-6 * Width * third) return; // flat picture: no opinion
            m_Votes++;
            // Rows top-down: the first rows are the top of the picture, which is usually farther (smaller values).
            if (first > last) m_FlipVotes++;
            if (m_Votes >= 8) TopDown = m_FlipVotes * 2 <= m_Votes;
        }

        /// <summary>8-bit version of <see cref="Map"/> for an R8 texture.</summary>
        public void ToBytes(byte[] target)
        {
            for (int i = 0; i < m_Map.Length && i < target.Length; i++)
                target[i] = (byte)(m_Map[i] * 255f + 0.5f);
        }
    }

    /// <summary>
    /// Numbers for 2D→3D: how far each eye's picture is shifted. The strength is an angle (the usual comfort
    /// measure for stereo): near parts come out of the screen by up to that much disparity, far parts go behind it
    /// by at most the eye distance (more would make the eyes diverge). The screen plane is placed in between.
    /// </summary>
    public static class Conversion3D
    {
        public const int Levels = 4; // off, light, medium, strong

        /// <summary>Most the eyes can be apart for far parts without diverging (minus a little margin).</summary>
        public const float MaxFarSeparation = 0.058f;

        /// <summary>Crossed disparity of the nearest parts in degrees, per level.</summary>
        public static float NearDisparityDegrees(int level)
        {
            switch (level)
            {
                case 1: return 0.45f;
                case 2: return 0.85f;
                case 3: return 1.3f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Shift per eye in texture units and the depth value that stays on the screen plane, for a picture
        /// <paramref name="screenWidth"/> meters wide seen from <paramref name="viewDistance"/> meters.
        /// A depth d (0 = far, 1 = near) ends up 2 · shift · (d − convergence) · width apart between the eyes.
        /// </summary>
        public static void Parameters(int level, float screenWidth, float viewDistance, out float shiftUv, out float convergence)
        {
            shiftUv = 0f;
            convergence = 0.5f;
            if (level <= 0 || screenWidth <= 0) return;
            viewDistance = Math.Max(viewDistance, 0.5f);
            float near = (float)(2.0 * viewDistance * Math.Tan(NearDisparityDegrees(level) * Math.PI / 360.0));
            float far = Math.Min(MaxFarSeparation, near * 0.6f);
            float range = near + far;
            shiftUv = range / (2f * screenWidth);
            convergence = far / range;
        }
    }
}
