using UnityEngine;

namespace TheMovies.World
{
    /// <summary>
    /// Generates the cinema's textures at startup (carpet, velvet, wall fabric, wood, starry ceiling),
    /// so the project needs no external image assets. All textures tile seamlessly.
    /// </summary>
    public static class ProceduralTextures
    {
        public static Texture2D Carpet(int size = 256)
        {
            var wine = new Color(0.30f, 0.05f, 0.08f);
            var gold = new Color(0.72f, 0.52f, 0.20f);
            var teal = new Color(0.08f, 0.30f, 0.34f);
            return Generate("Carpet", size, (u, v) =>
            {
                // Classic cinema carpet: diamonds with gold outlines and teal centers, on wine red.
                float du = Mathf.Abs(Frac(u * 2f) - 0.5f), dv = Mathf.Abs(Frac(v * 2f) - 0.5f);
                float diamond = du + dv;
                Color c = wine;
                if (Mathf.Abs(diamond - 0.36f) < 0.03f) c = gold;
                else if (diamond < 0.12f) c = teal;
                else if (Mathf.Abs(diamond - 0.22f) < 0.012f) c = Color.Lerp(wine, gold, 0.5f);

                // Small dots between the diamonds
                float cu = Frac(u * 2f + 0.5f) - 0.5f, cv = Frac(v * 2f + 0.5f) - 0.5f;
                if (cu * cu + cv * cv < 0.004f) c = gold;

                float fiber = 0.85f + 0.3f * Noise(u * 64f, v * 64f, 64) * 0.6f + 0.1f * Hash(u * 997f, v * 991f);
                return c * fiber;
            });
        }

        public static Texture2D Velvet(int size = 128)
        {
            return Generate("Velvet", size, (u, v) =>
            {
                float n = 0.86f + 0.10f * Noise(u * 16f, v * 16f, 16) + 0.06f * Hash(u * 613f, v * 617f);
                return new Color(n, n, n);
            });
        }

        public static Texture2D Fabric(int size = 128)
        {
            return Generate("Fabric", size, (u, v) =>
            {
                // Woven acoustic fabric: fine crossing threads plus slow variation
                float weave = 0.5f + 0.25f * Mathf.Sin(u * Mathf.PI * 2f * 48f) * Mathf.Sin(v * Mathf.PI * 2f * 48f);
                float n = 0.80f + 0.08f * weave + 0.12f * Noise(u * 8f, v * 8f, 8);
                return new Color(n, n, n);
            });
        }

        public static Texture2D Wood(int size = 256)
        {
            var light = new Color(0.55f, 0.33f, 0.18f);
            var dark = new Color(0.30f, 0.16f, 0.08f);
            return Generate("Wood", size, (u, v) =>
            {
                float warp = Noise(u * 4f, v * 1f, 4) * 3f;
                float grain = 0.5f + 0.5f * Mathf.Sin((v * 24f + warp) * Mathf.PI * 2f);
                grain = Mathf.Pow(grain, 3f);
                float fine = 0.9f + 0.1f * Hash(u * 211f, v * 1999f);
                return Color.Lerp(light, dark, grain * 0.7f) * fine;
            });
        }

        public static Texture2D StarryCeiling(int size = 512)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Stars", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            var night = new Color(0.010f, 0.012f, 0.03f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / size, v = (float)y / size;
                    float cloud = Noise(u * 4f, v * 4f, 4) * 0.5f + Noise(u * 9f, v * 9f, 9) * 0.25f;
                    pixels[y * size + x] = night + new Color(0.02f, 0.015f, 0.04f) * cloud;
                }
            }

            var rng = new System.Random(1234);
            int stars = size * size / 900;
            for (int s = 0; s < stars; s++)
            {
                int cx = rng.Next(size), cy = rng.Next(size);
                float brightness = 0.35f + (float)rng.NextDouble() * 0.65f;
                float radius = rng.NextDouble() < 0.08 ? 2.2f : 1.1f;
                var tint = Color.Lerp(new Color(1f, 0.85f, 0.65f), new Color(0.75f, 0.85f, 1f), (float)rng.NextDouble());
                int r = Mathf.CeilToInt(radius);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d / radius);
                        if (a <= 0) continue;
                        int px = (cx + dx + size) % size, py = (cy + dy + size) % size;
                        pixels[py * size + px] += tint * (brightness * a * a);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        static Texture2D Generate(string name, int size, System.Func<float, float, Color> pixel)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 8;
            return tex;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        static float Hash(float x, float y)
        {
            float h = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Tileable value noise in [0,1]; <paramref name="period"/> is the repeat in lattice cells.</summary>
        static float Noise(float x, float y, int period)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = Lattice(x0, y0, period), b = Lattice(x0 + 1, y0, period);
            float c = Lattice(x0, y0 + 1, period), d = Lattice(x0 + 1, y0 + 1, period);
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        static float Lattice(int x, int y, int period)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            return Hash(x * 1.31f + 0.17f, y * 1.73f + 0.31f);
        }
    }
}
