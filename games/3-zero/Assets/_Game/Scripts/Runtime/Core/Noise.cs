using UnityEngine;

namespace ZERO
{
    /// <summary>Deterministic value/gradient noise in plain C# (thread-safe, allocation-free).</summary>
    public static class Noise
    {
        static uint Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2147483647u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        static float Grad(uint h, float x, float y, float z)
        {
            switch (h & 15)
            {
                case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y;
                case 4: return x + z; case 5: return -x + z; case 6: return x - z; case 7: return -x - z;
                case 8: return y + z; case 9: return -y + z; case 10: return y - z; case 11: return -y - z;
                case 12: return y + x; case 13: return -y + z; case 14: return y - x; default: return -y - z;
            }
        }

        static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }

        /// <summary>Gradient noise, about [-1,1].</summary>
        public static float Perlin(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);
            float n000 = Grad(Hash(xi, yi, zi), xf, yf, zf);
            float n100 = Grad(Hash(xi + 1, yi, zi), xf - 1, yf, zf);
            float n010 = Grad(Hash(xi, yi + 1, zi), xf, yf - 1, zf);
            float n110 = Grad(Hash(xi + 1, yi + 1, zi), xf - 1, yf - 1, zf);
            float n001 = Grad(Hash(xi, yi, zi + 1), xf, yf, zf - 1);
            float n101 = Grad(Hash(xi + 1, yi, zi + 1), xf - 1, yf, zf - 1);
            float n011 = Grad(Hash(xi, yi + 1, zi + 1), xf, yf - 1, zf - 1);
            float n111 = Grad(Hash(xi + 1, yi + 1, zi + 1), xf - 1, yf - 1, zf - 1);
            float x00 = n000 + (n100 - n000) * u, x10 = n010 + (n110 - n010) * u;
            float x01 = n001 + (n101 - n001) * u, x11 = n011 + (n111 - n011) * u;
            float y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
            return (y0 + (y1 - y0) * w) * 0.97f;
        }

        public static float Perlin(float x, float y) { return Perlin(x, y, 0.37f); }

        /// <summary>Fractal sum, about [-1,1].</summary>
        public static float Fbm(float x, float y, float z, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float a = 1f, f = 1f, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += a * Perlin(x * f, y * f, z * f);
                norm += a; a *= gain; f *= lacunarity;
            }
            return sum / norm;
        }

        public static float Fbm(float x, float y, int octaves) { return Fbm(x, y, 0.37f, octaves); }

        /// <summary>Ridged fractal, [0,1], sharp crests.</summary>
        public static float Ridged(float x, float y, float z, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float a = 1f, f = 1f, sum = 0f, norm = 0f, prev = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Perlin(x * f, y * f, z * f));
                n *= n;
                sum += a * n * prev; prev = n;
                norm += a; a *= gain; f *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Cellular (Worley) noise: x = F1, y = F2 - F1 (thin near cell borders), z = cell id [0,1).</summary>
        public static Vector3 Worley(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float f1 = 9f, f2 = 9f, id = 0f;
            for (int k = -1; k <= 1; k++)
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int cx = xi + i, cy = yi + j, cz = zi + k;
                        uint h = Hash(cx, cy, cz);
                        float px = cx + (h & 1023) / 1023f;
                        float py = cy + ((h >> 10) & 1023) / 1023f;
                        float pz = cz + ((h >> 20) & 1023) / 1023f;
                        float dx = px - x, dy = py - y, dz = pz - z;
                        float d = dx * dx + dy * dy + dz * dz;
                        if (d < f1) { f2 = f1; f1 = d; id = (h >> 8 & 0xFFFF) / 65536f; }
                        else if (d < f2) f2 = d;
                    }
            f1 = Mathf.Sqrt(f1); f2 = Mathf.Sqrt(f2);
            return new Vector3(f1, f2 - f1, id);
        }

        public static float Hash01(int x, int y, int z) { return (Hash(x, y, z) & 0xFFFFFF) / 16777216f; }

        public static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static float Smoother(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
